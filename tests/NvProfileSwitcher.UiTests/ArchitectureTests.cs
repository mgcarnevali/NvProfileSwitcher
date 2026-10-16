using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using NvProfileSwitcher.Services;
using NvProfileSwitcher.Services.Editing;
using NvProfileSwitcher.Shell;
using NvProfileSwitcher.ViewModels;
using NvProfileSwitcher.Views;

namespace NvProfileSwitcher.UiTests;

// Structural rules of the migration: MainWindow and the Views have no code-behind logic,
// ViewModels never depend on each other and the window host has no domain knowledge.
public sealed class ArchitectureTests
{
    private static readonly Assembly AppAssembly = typeof(App).Assembly;

    private static string SourceRoot([CallerFilePath] string testFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(testFile)!, "..", "..", "src", "NvProfileSwitcher"));

    private static string ReadSource(string relativePath) =>
        File.ReadAllText(Path.Combine(SourceRoot(), relativePath)).ReplaceLineEndings("\n");

    private static string ConstructorOnlyCodeBehind(string ns, string type, string baseType) =>
        $$"""
        using Avalonia.Controls;

        namespace {{ns}};

        public partial class {{type}} : {{baseType}}
        {
            public {{type}}() => InitializeComponent();
        }

        """.ReplaceLineEndings("\n");

    [Fact]
    public void MainWindowCodeBehindOnlyInitializesComponent() =>
        Assert.Equal(
            ConstructorOnlyCodeBehind("NvProfileSwitcher", "MainWindow", "Window"),
            ReadSource("MainWindow.axaml.cs"));

    [Theory]
    [InlineData("ProfilesView")]
    [InlineData("ProfileSettingsView")]
    [InlineData("HotkeysView")]
    [InlineData("ApplicationSettingsView")]
    [InlineData("StatusBarView")]
    public void ViewCodeBehindOnlyInitializesComponent(string view) =>
        Assert.Equal(
            ConstructorOnlyCodeBehind("NvProfileSwitcher.Views", view, "UserControl"),
            ReadSource(Path.Combine("Views", view + ".axaml.cs")));

    [Fact]
    public void EveryViewIsCoveredByTheCodeBehindRule()
    {
        var views = Directory.GetFiles(Path.Combine(SourceRoot(), "Views"), "*.axaml.cs")
            .Select(path => Path.GetFileName(path)[..^".axaml.cs".Length])
            .Order()
            .ToArray();
        Assert.Equal(
            ["ApplicationSettingsView", "HotkeysView", "ProfileSettingsView", "ProfilesView", "StatusBarView"],
            views);
    }

    [Fact]
    public void ViewModelsDoNotDependOnOtherViewModels()
    {
        var viewModels = AppAssembly.GetTypes()
            .Where(type => type.Namespace == typeof(MainWindowViewModel).Namespace && !type.IsNested)
            .ToArray();
        Assert.NotEmpty(viewModels);

        // The only exceptions: the Hotkeys panel owns its four field ViewModels and Profile
        // Settings owns the profile hotkey field ViewModel, which depend on no ViewModel themselves.
        (Type Owner, Type Item)[] ownedItems =
        [
            (typeof(HotkeysViewModel), typeof(HotkeyFieldViewModel)),
            (typeof(ProfileSettingsViewModel), typeof(HotkeyFieldViewModel)),
        ];

        foreach (var type in viewModels.Where(type => type != typeof(MainWindowViewModel)))
        {
            foreach (var dependency in Dependencies(type).Where(dependency => !ownedItems.Contains((type, dependency))))
                Assert.False(
                    viewModels.Contains(dependency),
                    $"{type.Name} depends on {dependency.Name}.");
        }
    }

    // Coordinators hold no state: only the read-only references to what they call.
    [Fact]
    public void CoordinatorsHoldNoState()
    {
        var coordinators = AppAssembly.GetTypes()
            .Where(type => type.Namespace == typeof(Composition.StartupCoordinator).Namespace &&
                           type.Name.EndsWith("Coordinator", StringComparison.Ordinal))
            .ToArray();
        Assert.Contains(typeof(Composition.DisplayRefreshCoordinator), coordinators);

        foreach (var field in coordinators.SelectMany(type =>
                     type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)))
            Assert.True(field.IsInitOnly, $"{field.DeclaringType!.Name}.{field.Name} is not read-only.");
    }

    [Fact]
    public void MainWindowHostHasNoDomainDependencies()
    {
        var constructors = typeof(MainWindowHost).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.All(constructors, constructor => Assert.Empty(constructor.GetParameters()));

        foreach (var dependency in Dependencies(typeof(MainWindowHost)))
        {
            var ns = dependency.Namespace ?? string.Empty;
            Assert.True(
                ns == "System" || ns.StartsWith("System.", StringComparison.Ordinal) ||
                ns == "Avalonia" || ns.StartsWith("Avalonia.", StringComparison.Ordinal),
                $"MainWindowHost depends on {dependency.FullName}.");
        }
    }

    // Window events that MainWindowHost translates for the rest of the application.
    private const string HostOwnedWindowEvents = "Opened|Activated|Deactivated|Closing|Closed|PropertyChanged";

    private static IEnumerable<string> ApplicationSources() =>
        Directory.EnumerateFiles(SourceRoot(), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"));

    // The profile list changes only through the store, whose changes keep the list consistent:
    // nothing outside it can add, remove or move profiles.
    [Fact]
    public void TheProfileListIsReadOnlyOutsideTheStore()
    {
        var members = typeof(ProfileStore).GetProperties(BindingFlags.Instance | BindingFlags.Public);
        Assert.All(members, property => Assert.False(
            property.PropertyType.IsGenericType &&
            property.PropertyType.GetGenericTypeDefinition() is var definition &&
            (definition == typeof(System.Collections.ObjectModel.ObservableCollection<>) || definition == typeof(List<>)),
            $"ProfileStore.{property.Name} exposes a mutable list."));
        Assert.Equal(typeof(System.Collections.ObjectModel.ReadOnlyObservableCollection<ProfileListItem>),
            typeof(ProfileStore).GetProperty(nameof(ProfileStore.Profiles))!.PropertyType);
    }

    // What the hotkeys register (the profile list, each profile's Enabled and Hotkey, and the
    // global hotkeys) changes only in its owners, each of which registers the hotkeys again after
    // the change: the list and Enabled in ProfileListService (and the editor's unsaved profile,
    // which has no hotkey), a profile hotkey through the editor's commit on Save, the global
    // hotkeys in the capture, and the whole configuration in its load and import. Pure copies of
    // profiles that are not in the list yet are allowed in Core.
    [Fact]
    public void OnlyTheOwnersChangeWhatTheHotkeysRegister()
    {
        (string Pattern, string[] Allowed)[] rules =
        [
            (@"\.Enabled\s*=[^=]", ["Services/ProfileListService.cs"]),
            (@"\.Hotkey\s*=[^=]", ["Services/Editing/ProfileEditor.cs", "Core/ProfileEditingCore.cs"]),
            (@"\.(ShowHide|WindowsOverride|ResumeAutomatic|GlobalSwitching)Hotkey\s*=[^=]",
                ["Services/HotkeyCaptureController.cs", "Core/ConfigMappingCore.cs"]),
            (@"\b(store|profiles|Profiles)\.(Load|AddUnsaved|MarkSaved|Remove|Move)\(",
                ["Services/ProfileListService.cs", "Services/Editing/ProfileEditor.cs", "Services/ConfigurationService.cs", "Services/ProfileStore.cs"]),
        ];

        var violations = new List<string>();
        foreach (var path in ApplicationSources())
        {
            var relative = Path.GetRelativePath(SourceRoot(), path).Replace('\\', '/');
            var text = File.ReadAllText(path);
            foreach (var (pattern, allowed) in rules)
                if (!allowed.Contains(relative) && Regex.IsMatch(text, pattern))
                    violations.Add($"{relative}: {pattern}");
        }

        Assert.Empty(violations);
    }

    // Display colors are written only by DisplayColorController, which writes each display once
    // per operation with what it should show: nothing else can write an intermediate state.
    [Fact]
    public void OnlyTheColorControllerWritesDisplayColors()
    {
        var writers = ApplicationSources()
            .Where(path => Regex.IsMatch(File.ReadAllText(path), @"\bnvidia\.Apply\("))
            .Select(path => Path.GetRelativePath(SourceRoot(), path).Replace('\\', '/'))
            .ToList();

        Assert.Equal(["Services/DisplayColorController.cs"], writers);
    }

    [Fact]
    public void MainWindowIsCreatedOnlyByTheCompositionRoot()
    {
        var creators = ApplicationSources()
            .Where(path => Regex.IsMatch(File.ReadAllText(path), @"\bnew\s+MainWindow\b"))
            .Select(path => Path.GetRelativePath(SourceRoot(), path).Replace('\\', '/'))
            .ToArray();
        Assert.Equal(["Composition/AppHost.cs"], creators);
    }

    // Only MainWindowHost subscribes to the main window events it translates. The main window
    // is reachable through the instance created by the composition root, through Owner and
    // through the desktop lifetime's MainWindow, including local aliases of those. Events of
    // other windows and dialogs are not restricted.
    [Fact]
    public void OnlyMainWindowHostSubscribesToMainWindowEvents()
    {
        var hostFile = Path.Combine(SourceRoot(), "Shell", "MainWindowHost.cs");
        var violations = new List<string>();

        foreach (var path in ApplicationSources().Where(path => path != hostFile))
        {
            var source = File.ReadAllText(path);
            var aliases = Regex.Matches(source,
                    @"\bvar\s+(\w+)\s*=\s*new\s+MainWindow\b" +
                    @"|\bvar\s+(\w+)\s*=[^;]*\b(?:Owner|MainWindow)\s*[!?]?\s*;" +
                    @"|\b(?:Owner|MainWindow)\s+is\s*\{\s*\}\s*(\w+)")
                .SelectMany(match => match.Groups.Cast<Group>().Skip(1))
                .Where(group => group.Success)
                .Select(group => Regex.Escape(group.Value));
            var receivers = string.Join("|", aliases.Append("Owner").Append("MainWindow"));
            var subscriptions = Regex.Matches(source,
                $@"\b(?:{receivers})\s*[!?]?\s*\.\s*(?:{HostOwnedWindowEvents})\s*[-+]=");

            violations.AddRange(subscriptions.Select(match =>
                $"{Path.GetRelativePath(SourceRoot(), path)}: {match.Value}"));
        }

        Assert.Empty(violations);
    }

    // NvidiaService owns only the NVIDIA API, displays and driver; DisplayTopologyService only
    // detects topology changes. Neither depends on profiles, configuration, switching or UI.
    [Theory]
    [InlineData(typeof(NvidiaService), "NvProfileSwitcher.Nvidia")]
    [InlineData(typeof(DisplayTopologyService), null)]
    public void DisplayServicesDependOnlyOnTheirOwnConcerns(Type service, string? allowedNamespace)
    {
        foreach (var dependency in Dependencies(service))
        {
            var ns = dependency.Namespace ?? string.Empty;
            Assert.True(
                ns == "System" || ns.StartsWith("System.", StringComparison.Ordinal) ||
                ns == "Avalonia" || ns.StartsWith("Avalonia.", StringComparison.Ordinal) ||
                ns.StartsWith("CommunityToolkit.", StringComparison.Ordinal) ||
                ns == allowedNamespace,
                $"{service.Name} depends on {dependency.FullName}.");
        }
    }

    // The color, switching, preview and startup registration services depend only on services, shared
    // state and platform seams: never on ViewModels, Views, the window host or the coordinators.
    [Theory]
    [InlineData(typeof(DisplayColorController))]
    [InlineData(typeof(ActiveProfileService))]
    [InlineData(typeof(AutomaticSwitchingService))]
    [InlineData(typeof(LivePreviewController))]
    [InlineData(typeof(StartupRegistrationService))]
    public void ServicesDoNotDependOnPresentationOrComposition(Type service)
    {
        string[] forbidden = ["NvProfileSwitcher.ViewModels", "NvProfileSwitcher.Views", "NvProfileSwitcher.Shell", "NvProfileSwitcher.Composition"];
        foreach (var dependency in Dependencies(service))
            Assert.DoesNotContain(dependency.Namespace ?? string.Empty, forbidden);
        Assert.DoesNotContain(typeof(NvProfileSwitcher.Nvidia.IDisplayColorBackend), Dependencies(service));
    }

    // Every service shows its dialogs only through IDialogService: no service depends on
    // ViewModels, Views, the window host or the composition.
    [Fact]
    public void ServicesUseOnlyTheDialogServiceFromTheShell()
    {
        var services = AppAssembly.GetTypes()
            .Where(type => type.Namespace is { } ns && (ns == "NvProfileSwitcher.Services" || ns.StartsWith("NvProfileSwitcher.Services.", StringComparison.Ordinal)))
            .Where(type => !type.IsNested && !type.IsEnum)
            .ToArray();
        Assert.Contains(typeof(ProfileListService), services);

        string[] forbidden = ["NvProfileSwitcher.ViewModels", "NvProfileSwitcher.Views", "NvProfileSwitcher.Composition"];
        foreach (var service in services)
            foreach (var dependency in Dependencies(service))
            {
                var ns = dependency.Namespace ?? string.Empty;
                Assert.False(forbidden.Contains(ns), $"{service.Name} depends on {dependency.FullName}.");
                if (ns == "NvProfileSwitcher.Shell")
                    Assert.True(dependency == typeof(IDialogService), $"{service.Name} depends on {dependency.FullName}.");
            }
    }

    // The editor holds the state of the edited profile and its transitions: it shows no dialogs,
    // has no effects on hotkeys, persistence, the preview or switching, and never reads the
    // displays, so the values it shows and saves never come from colors a display shows.
    [Fact]
    public void TheEditorHoldsOnlyState()
    {
        Type[] forbidden =
        [
            typeof(IDialogService), typeof(HotkeyService), typeof(ConfigurationService), typeof(LivePreviewController), typeof(NvidiaService),
            typeof(ActiveProfileService), typeof(AutomaticSwitchingService), typeof(DisplayColorController),
            typeof(ProfileEditingService), typeof(ProfileListService)
        ];
        foreach (var dependency in Dependencies(typeof(ProfileEditor)))
            Assert.DoesNotContain(dependency, forbidden);
    }

    // The profile panels only present: the actions with effects run in the editing and list services.
    [Theory]
    [InlineData(typeof(ProfilesViewModel))]
    [InlineData(typeof(ProfileSettingsViewModel))]
    public void TheProfilePanelsDelegateTheirActions(Type viewModel)
    {
        Type[] forbidden =
        [
            typeof(ConfigurationService), typeof(ActiveProfileService), typeof(LivePreviewController), typeof(NvidiaService),
            typeof(AutomaticSwitchingService), typeof(DisplayColorController)
        ];
        foreach (var dependency in Dependencies(viewModel))
            Assert.DoesNotContain(dependency, forbidden);
    }

    // The service graph shared by the application and the test fixtures knows no window host,
    // tray, coordinator, ViewModel or View: the window reaches it only as plain facts.
    [Theory]
    [InlineData(typeof(Composition.AppServices))]
    [InlineData(typeof(Composition.ServiceEnvironment))]
    public void TheServiceGraphKnowsNoPresentation(Type type)
    {
        foreach (var dependency in Dependencies(type))
        {
            var ns = dependency.Namespace ?? string.Empty;
            Assert.DoesNotContain(ns, (string[])["NvProfileSwitcher.ViewModels", "NvProfileSwitcher.Views"]);
            Assert.False(ns == "NvProfileSwitcher.Composition" && dependency != typeof(Composition.ServiceEnvironment),
                $"{type.Name} depends on {dependency.Name}.");
            if (ns == "NvProfileSwitcher.Shell")
                Assert.Equal(typeof(IDialogService), dependency);
        }
    }

    [AvaloniaFact]
    public void MainWindowAssignsOneViewModelPerPanel()
    {
        var viewModel = TestViewModels.MainWindow();
        var window = new MainWindow { DataContext = viewModel };
        window.Show();

        Assert.Same(viewModel.Profiles, Single<ProfilesView>(window).DataContext);
        Assert.Same(viewModel.ProfileSettings, Single<ProfileSettingsView>(window).DataContext);
        Assert.Same(viewModel.Hotkeys, Single<HotkeysView>(window).DataContext);
        Assert.Same(viewModel.ApplicationSettings, Single<ApplicationSettingsView>(window).DataContext);
        Assert.Same(viewModel.StatusBar, Single<StatusBarView>(window).DataContext);
        window.Close();
    }

    private static T Single<T>(Window window) where T : Control =>
        Assert.Single(window.GetLogicalDescendants().OfType<T>());

    // Types referenced by constructor parameters and instance fields, including generic arguments.
    private static IEnumerable<Type> Dependencies(Type type)
    {
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var direct = type.GetConstructors(flags).SelectMany(c => c.GetParameters()).Select(p => p.ParameterType)
            .Concat(type.GetFields(flags).Select(f => f.FieldType));
        return direct.SelectMany(Expand).Distinct();
    }

    private static IEnumerable<Type> Expand(Type type)
    {
        if (type.HasElementType)
            return Expand(type.GetElementType()!);
        if (!type.IsGenericType)
            return [type];
        return new[] { type.GetGenericTypeDefinition() }.Concat(type.GetGenericArguments().SelectMany(Expand));
    }
}

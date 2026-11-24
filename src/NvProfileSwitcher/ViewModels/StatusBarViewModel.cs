using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NvProfileSwitcher.Core;
using NvProfileSwitcher.Models;
using NvProfileSwitcher.Services;
using NvProfileSwitcher.Shell;

namespace NvProfileSwitcher.ViewModels;

/// <summary>
/// Status bar: NVIDIA API (available, not detected or unavailable), driver, active profile,
/// global switching and links.
/// Depends only on services and shared state, never on another ViewModel.
/// </summary>
internal sealed partial class StatusBarViewModel : ObservableObject
{
    private readonly NvidiaService nvidia;
    private readonly SwitchingState switching;
    private readonly IDialogService dialogs;

    public StatusBarViewModel(NvidiaService nvidia, SwitchingState switching, IDialogService dialogs)
    {
        this.nvidia = nvidia;
        this.switching = switching;
        this.dialogs = dialogs;
        nvidia.PropertyChanged += OnNvidiaPropertyChanged;
        switching.PropertyChanged += OnSwitchingPropertyChanged;
    }

    public bool IsNvApiAvailable => nvidia.IsAvailable;

    /// <summary>No NVIDIA driver or GPU: not an error, so it is not shown as one.</summary>
    public bool IsNvApiNotDetected => nvidia.Status == NvidiaStatus.NotDetected;

    public string NvApiStatusText => nvidia.Status switch
    {
        NvidiaStatus.Available => "Available",
        NvidiaStatus.NotDetected => "Not detected",
        _ => "Unavailable",
    };

    public string DriverText => IsNvApiNotDetected ? "—" : nvidia.DriverVersion;

    /// <summary>The active profile as last published, not the live switching state.</summary>
    public string ActiveProfileText => ActiveProfileCore.StatusText(switching.DisplayedActiveProfile);

    public bool IsGlobalSwitchingOn => !switching.GlobalPaused;

    public string GlobalSwitchingText => switching.GlobalPaused ? "OFF" : "ON";

    // Concurrent executions are allowed so a link never disables itself while it runs,
    // as with the original Tapped handlers.
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task OpenGitHubAsync() => dialogs.OpenUriAsync(new Uri("https://github.com/mgcarnevali/NvProfileSwitcher"));

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task OpenSupportAsync() => dialogs.OpenUriAsync(new Uri("https://ko-fi.com/mgcarnevali"));

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task ShowAboutAsync() => dialogs.ShowAboutAsync();

    private void OnNvidiaPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NvidiaService.Status))
        {
            OnPropertyChanged(nameof(IsNvApiAvailable));
            OnPropertyChanged(nameof(IsNvApiNotDetected));
            OnPropertyChanged(nameof(NvApiStatusText));
            OnPropertyChanged(nameof(DriverText));
        }
        else if (e.PropertyName == nameof(NvidiaService.DriverVersion))
        {
            OnPropertyChanged(nameof(DriverText));
        }
    }

    private void OnSwitchingPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SwitchingState.DisplayedActiveProfile))
        {
            OnPropertyChanged(nameof(ActiveProfileText));
        }
        else if (e.PropertyName == nameof(SwitchingState.GlobalPaused))
        {
            OnPropertyChanged(nameof(IsGlobalSwitchingOn));
            OnPropertyChanged(nameof(GlobalSwitchingText));
        }
    }
}

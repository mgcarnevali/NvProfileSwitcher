using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NvProfileSwitcher.Core;
using NvProfileSwitcher.Models;
using NvProfileSwitcher.Services;
using NvProfileSwitcher.Shell;

namespace NvProfileSwitcher.ViewModels;

/// <summary>
/// Status bar: NVIDIA API, driver, active profile, global switching and links.
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

    public string NvApiStatusText => nvidia.IsAvailable ? "Available" : "Unavailable";

    public string DriverText => nvidia.DriverVersion;

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
        if (e.PropertyName == nameof(NvidiaService.IsAvailable))
        {
            OnPropertyChanged(nameof(IsNvApiAvailable));
            OnPropertyChanged(nameof(NvApiStatusText));
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

using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NvProfileSwitcher.Services;
using NvProfileSwitcher.Services.Editing;

namespace NvProfileSwitcher.ViewModels;

/// <summary>
/// Profiles panel: the profile list, its selection and the list actions, which
/// <see cref="ProfileListService"/> runs. Depends only on services and shared state, never on
/// another ViewModel.
/// </summary>
internal sealed partial class ProfilesViewModel : ObservableObject
{
    private readonly ProfileStore store;
    private readonly ProfileEditor editor;
    private readonly ProfileEditingService editing;
    private readonly ProfileListService profileList;

    // True while the list is writing a new selection: the list cannot take a different
    // selection back within its own change, so that notification is posted.
    private bool selecting;

    // True while the selection is notified as empty first: after the list wrote a selection
    // or dropped one, its binding keeps the value it read back and ignores a notification of
    // that same value, so the list would not show it.
    private bool bouncing;

    public ProfilesViewModel(
        ProfileStore store,
        ProfileEditor editor,
        ProfileEditingService editing,
        ProfileListService profileList)
    {
        this.store = store;
        this.editor = editor;
        this.editing = editing;
        this.profileList = profileList;
        editor.PropertyChanged += OnEditorPropertyChanged;
    }

    public ReadOnlyObservableCollection<ProfileListItem> Profiles => store.Profiles;

    /// <summary>
    /// The list selection. A new selection is confirmed by the editing service, which may keep
    /// the edited profile selected; the list dropping its selection is ignored.
    /// </summary>
    public ProfileListItem? SelectedProfile
    {
        get => bouncing ? null : editor.SelectedProfile;
        set
        {
            if (value is null || ReferenceEquals(value, editor.SelectedProfile)) return;

            selecting = true;
            try
            {
                _ = editing.RequestSelectionAsync(value);
            }
            finally
            {
                selecting = false;
            }
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task AddAsync() => profileList.AddAsync();

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task RemoveAsync() => profileList.RemoveSelectedAsync();

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task MoveUpAsync(ProfileListItem? profile) => profileList.MoveAsync(profile, -1);

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task MoveDownAsync(ProfileListItem? profile) => profileList.MoveAsync(profile, 1);

    [RelayCommand]
    private void ToggleEnabled(ProfileListItem? profile) => profileList.ToggleEnabled(profile);

    private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ProfileEditor.SelectedProfile))
            return;

        if (selecting)
            Dispatcher.UIThread.Post(NotifySelection);
        else
            NotifySelection();
    }

    private void NotifySelection()
    {
        bouncing = true;
        OnPropertyChanged(nameof(SelectedProfile));
        bouncing = false;
        OnPropertyChanged(nameof(SelectedProfile));
    }
}

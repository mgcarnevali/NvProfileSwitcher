# NvProfileSwitcher (Avalonia) — Architecture

This document describes the architecture as implemented on `prototype/ui-rebuild-v2`: how
the application is composed, who owns each piece of state, the closed step lists of the
coordinators, how the configuration is persisted, the object lifecycle, and every deliberate
deviation from the reference implementation.

**Reference implementation:** `feature/nvtweak-color-sync`. All of its logic lived in
`MainWindow.axaml.cs` (2478 lines, cited below as **MW** with line numbers) plus the tray and
single-instance handling in `App.axaml.cs`. The migration reproduces its behavior; the only
differences are the ones listed in §15.

## Contents

1. [Status](#1-status)
2. [Principles](#2-principles)
3. [Component map](#3-component-map)
4. [Source layout](#4-source-layout)
5. [Responsibilities](#5-responsibilities)
6. [MVVM, Views and code-behind](#6-mvvm-views-and-code-behind)
7. [Window host contract](#7-window-host-contract)
8. [Coordinators](#8-coordinators)
9. [Startup](#9-startup)
10. [Shared state](#10-shared-state)
11. [Profile list consistency and hotkeys](#11-profile-list-consistency-and-hotkeys)
12. [Persistence](#12-persistence)
13. [Lifecycle, events and disposal](#13-lifecycle-events-and-disposal)
14. [Application Settings and Advanced](#14-application-settings-and-advanced)
15. [Deliberate deviations from the reference](#15-deliberate-deviations-from-the-reference)
16. [Reference behavior kept on purpose](#16-reference-behavior-kept-on-purpose)
17. [Findings and decisions](#17-findings-and-decisions)
18. [Risks and validation](#18-risks-and-validation)
19. [NativeAOT](#19-nativeaot)
20. [Open items](#20-open-items)
21. [Resolution of the integral audit](#21-resolution-of-the-integral-audit)
22. [Displays and colors](#22-displays-and-colors)

---

## 1. Status

The commit hashes and Windows CI run numbers in this document belong to the archived
`avalonia-migration` repository, where the migration was developed.

| Phase | Scope | Commits | Validation |
|---|---|---|---|
| 0 | Foundations | `2de105d`, `a15d6f5`, `2bb1473` | Windows CI #62 |
| 1 | Configuration data layer | `b0d5878` | Windows CI #63 |
| 2 | NVIDIA API and displays | `61dff8b` | Windows CI green; manual hardware test |
| 2+ | Status bar threshold (§17.5) | `0252b8c` | Windows CI #68 |
| 3 | Switching and colors | `a9b52b4` | Windows CI #69; artifact tested on hardware |
| 4 | Updates and About | `978c534` | Windows CI #71 |
| 5 | Settings, tray and window | `cb1deea` | Windows CI #72 |
| 6 | Hotkeys | `6dcf603` | Windows CI #73 |
| 7a | Shared composition | `7275964` | Windows CI #74 |
| 7b | Profiles, editor, Live Preview, close guard | `25a34c6`, `833497b` | Windows CI green; manual test of editor, preview and switching |
| 8 | Advanced | `25cc65b` | Windows CI #77 |
| AOT | NativeAOT hardening (§19) | `492b9e0` | Windows CI publishes and smoke-tests the AOT executable |
| 9 | Persistence hardening, profile list changes, documentation | `75402cb`, `07b72cb`, `7e1005c` | Windows CI #79 |
| 10 | Integral refactor (§21): behavior tests, shared service graph, list consistency in the store and hotkeys per profile (R2 retired), preview event, editor and use-case services, single discard confirmation, clean drafts on display changes, deferred icons, update flow | `a361d40`, `5adda63`, `aaddf16`, `2dd06d8`, `a16465f`, `d97be46`, `9e5e5b1`, `a800928`, `886c958`, `245c6d5` | Windows CI #80 |
| 11 | Displays and colors (§22): monitor identity, one color writer in batches, saved values that never come from written colors, one retry for monitors without identity | `bc53720` (superseded by `93fdf2a`), `4b869a9`, `93fdf2a`, `4127598`, `6f04236` and this document | Windows CI #81; hardware test of the preview while disconnecting a monitor |
| 12 | Recovery after resume (§22.4) | `1b746b9`, `9ad94d3` | Windows CI #82; resume simulation on hardware pending (§20) |
| 13 | Installed driver version (§22.5) | `c54ecef`, `e96e57e` (read on display changes) and this document | Local suites (§18); Windows CI and the driver update on hardware pending (§20) |

---

## 2. Principles

1. **Same behavior as the reference.** Every exception is listed in §15 and was approved
   before it was implemented. Odd reference behavior is kept, not "fixed" (§16).
2. **Out of scope:** new features, NVIDIA API retries other than the reference's (it initializes
   again on every `Opened`, §17.1) and the recovery after resume (§22.4), and diagnostic logs. The C++
   application (v2.2.0) is not a data compatibility requirement: its configurations and backups
   are not migrated.
3. `MainWindow.axaml.cs` and the code-behind of the five Views contain only the constructor and
   `InitializeComponent()`.
4. ViewModels never reference each other; they depend only on services and shared state.
5. The approved responsive layout and appearance are not changed. The only approved layout
   changes are the status bar adjustments of §17.5.
6. Code, comments and documentation are in English. There are no diagnostic logs.
7. Everything runs on the UI thread: the watchers use `DispatcherTimer` and the single-instance
   pipe posts to the dispatcher, so there are no locks.

### 2.1 Architectural invariants

Each invariant has one owner, and a test verifies it.

1. **The profile list is consistent by construction.** `ProfileStore` exposes the list read-only,
   so nothing else can change it. Its only changes (`Load`, `AddUnsaved`, `MarkSaved`, `Remove`,
   `Move`) keep the ▲▼ state, the unsaved new profile and the profile override (a position in the
   list) on their profiles. No caller has to remember any of it.
2. **Hotkeys do not depend on positions.** `HotkeyService` remembers which IDs it registered and
   the profile of each profile hotkey ID, and looks up the profile's position when the key is
   pressed. Registering again after a change is a consequence of the change, never an order to
   respect: forgetting it can leave a combination reserved, never trigger the wrong profile.
3. **What the hotkeys register changes only in its owners**, each of which registers the hotkeys
   again afterwards: the list and `Enabled` in `ProfileListService`, a profile hotkey through the
   editor's commit on Save, the global hotkeys in `HotkeyCaptureController`, the whole
   configuration in its load and import. An architecture test restricts those writes to those
   files, and every integration test ends checking that the registered hotkeys are exactly the ones
   the state calls for.
4. **Levels are separate.** `ProfileEditor` holds the edited profile's state and its synchronous
   transitions; the actions with effects beyond it (dialogs, preview, persistence, hotkeys,
   switching) are in `ProfileEditingService` and `ProfileListService`; the ViewModels only present.
5. **One discard confirmation.** `ProfileEditingService.ConfirmAndDiscardAsync` is the only place
   that asks to discard unsaved changes: selection, Add, Cancel, closing, importing and updating.
   While one is open, no other is opened.
6. **One wiring.** `AppServices.Create` builds the service graph over seams and three facts about
   the window, and knows no Shell, coordinator, ViewModel or View. The application and the test
   fixtures use it; tests of pure logic and of a single service keep building only their unit.
7. **No cycles and no forward references.** The live preview depends on the active profile, which
   tells it before a switch with an event; the rule that the preview owns the display lives in
   `ActiveProfileService`, which receives only the fact that the window is active.
8. **One color writer, one write per display per operation.** Only `DisplayColorController`
   writes display colors. It holds what each display should show (the base profile, or the live
   preview on its display) and writes each display once, when the outermost batch of an operation
   ends, with its final values: no display goes through an intermediate state. An architecture test
   restricts the writes to it, and integration tests check one write per display for every flow.
9. **Monitor identity is never made up.** A display has the physical identity of its unique EDID
   serial number, or else the identity of its connection, or no identity; a display without one is
   left out of `NvidiaService.Displays`, so it is never written, edited, synchronized or saved.
10. **Saved values never come from written colors.** They come from the user's edits, other
    profiles, the Windows values and the colors a display shows before this application writes it;
    the editor never reads a display, saving a draft never removes values, and values without a
    monitor id are dropped on load.

---

## 3. Component map

```
Program ──► SingleInstanceService            UpdateService.ApplyPreparedUpdate (--apply-update)
   │
App ──► AppHost (composition root: AppServices + Shell + coordinators + ViewModels; no handlers)
   │
   ├─ AppServices (the service graph, shared with the test fixtures)
   │  ├─ Shared state (observable, UI thread)
   │  │    ProfileStore ──► SwitchingState · AppSettings · NvidiaService (NVIDIA API, displays, driver)
   │  │
   │  ├─ Domain and application services
   │  │    ConfigurationService ─────── ProfileStore, AppSettings, SwitchingState, IDialogService
   │  │    DisplayColorController ───── NvidiaService, ProfileStore, SwitchingState (the only color writer)
   │  │    DisplayProfileSyncService ── ProfileStore, NvidiaService, DisplayColorController, ConfigurationService
   │  │    DisplayTopologyService       (WM_DISPLAYCHANGE debounce, one retry)
   │  │    HotkeyService ────────────── IHotkeyRegistrar, AppSettings, ProfileStore, IDialogService
   │  │    ProfileEditor ────────────── ProfileStore                           (state of the edited profile)
   │  │    ActiveProfileService ─────── DisplayColorController, NvidiaService, ProfileStore, SwitchingState,
   │  │                                 ProfileEditor + foreground executable, "window is active"
   │  │    LivePreviewController ────── ProfileEditor, ActiveProfileService, DisplayColorController, SwitchingState
   │  │                                 + "window shown normally"
   │  │    AutomaticSwitchingService ── IForegroundMonitor, ActiveProfileService, DisplayColorController,
   │  │                                 SwitchingState, AppSettings, ConfigurationService, ProfileStore,
   │  │                                 LivePreviewController
   │  │    HotkeyCaptureController ──── HotkeyService, AppSettings, ProfileStore, ConfigurationService,
   │  │                                 ProfileEditor + "window is active"
   │  │    ProfileEditingService ────── ProfileEditor, ProfileStore, LivePreviewController, ConfigurationService,
   │  │                                 HotkeyService, ActiveProfileService, HotkeyCaptureController,
   │  │                                 DisplayColorController, IDialogService (session actions)
   │  │    ProfileListService ───────── ProfileStore, ProfileEditor, ProfileEditingService, LivePreviewController,
   │  │                                 HotkeyService, ConfigurationService, ActiveProfileService,
   │  │                                 NvidiaService, DisplayColorController, IDialogService (list actions)
   │  │    UpdateCheckService ───────── AppSettings, IDialogService, release lookups, IUpdateInstaller,
   │  │                                 the discard confirmation, shutdown
   │  │    StartupRegistrationService · SavedDisplaysService
   │  │    ConfigurationBackupService ─ IDialogService, ConfigurationService, AppSettings, ProfileEditor,
   │  │                                 ProfileEditingService, LivePreviewController, HotkeyService,
   │  │                                 DisplayProfileSyncService, StartupRegistrationService,
   │  │                                 AutomaticSwitchingService + "set topmost"
   │
   ├─ Shell (platform adapters)
   │    MainWindowHost  (window events, show/hide to tray, topmost, WndProc; no domain types)
   │    TrayService     (TrayIcon + NativeMenu)
   │    DialogService   (dialogs, pickers, links, owner)
   │
   ├─ Coordinators (Composition/; stateless sequences, no logic of their own)
   │    StartupCoordinator · DisplayRefreshCoordinator · WindowActivityCoordinator
   │    HotkeyActionDispatcher · ShutdownCoordinator
   │
   └─ ViewModels (presentation; services only, never another ViewModel)
        ProfilesViewModel · ProfileSettingsViewModel · HotkeysViewModel (4 × HotkeyFieldViewModel)
        ApplicationSettingsViewModel · StatusBarViewModel
        MainWindowViewModel = immutable container of the five (no logic, no subscriptions)

Views (XAML + compiled bindings; code-behind = constructor + InitializeComponent)
   MainWindow ─ ProfilesView · ProfileSettingsView · HotkeysView · ApplicationSettingsView · StatusBarView
   Behaviors: HotkeyCaptureBehavior · TapCommandBehavior
Dialogs: About · AdvancedSettings · UpdateAvailable · RunningApps · ManageDisplays · CenteredDialog
         + message, confirmation and Copy settings dialogs built by DialogService
```

**The graph is acyclic, without forward references.**
- Views → ViewModels → services → state, Core and platform. Coordinators and Shell → services.
  `MainWindowHost` depends on nothing in the domain.
- The two places where two parts must react to each other use a synchronous event in the
  direction that would otherwise close a cycle:
  - Editor → preview: `ProfileEditor` raises `EditingTargetChanging` where the reference called
    `SuspendDraftLivePreview()`, and `DisplayChanged` where it resumed the preview.
  - Active profile → preview: `ActiveProfileService` raises `ProfileSwitching` right before a
    switch applies another profile, where the reference discarded the preview.
  - Color controller → active profile: `DisplayColorController` raises `WriteFailed` when a batch
    that does not throw could not write a display, and the composition makes the active profile
    forget its name, so the next check applies it again.
- `ProfileListService` uses `ProfileEditingService` (Add confirms the discard; Move selects
  another profile); `ProfileEditingService` never uses `ProfileListService`, because discarding the
  unsaved profile only needs the store.
- Window conditions reach services as plain facts (`Func<bool>`, `Action<bool>`) passed in the
  `ServiceEnvironment`, so services never depend on the Shell.

---

## 4. Source layout

```
src/NvProfileSwitcher/
  Program.cs  App.axaml(.cs)  MainWindow.axaml(.cs)  BuildInfo.cs  AssemblyInfo.cs  app.manifest
  Composition/  AppHost  AppPlatform  AppComposition  AppServices (+ ServiceEnvironment)
                StartupCoordinator  DisplayRefreshCoordinator  WindowActivityCoordinator
                HotkeyActionDispatcher  ShutdownCoordinator
  Core/         pure logic, unit-tested:
                ConfigurationCore  HotkeyCore  ProfileApplicationCore  ProfileEditingCore
                SwitchingCore  UpdateCore  NvProfileSwitcherJsonContext          (from the reference)
                DisplayCore (+ FindValues)                                      (reference, extended)
                ConfigMappingCore  OverrideCore  DisplayProfileSyncCore  SavedDisplaysCore
                ActiveProfileCore  HotkeyKeyCore  ProfileDefaultsCore  MonitorIdentityCore
                ProfileValidationCore                                           (extracted from MW)
                NvTweakColorCore
  Models/       AppConfig  ProfileListItem (+ ProfileDisplaySettings)  ExecutableComboItem
                ColorValues  AppSettings  SwitchingState  ActiveProfileDisplay  HotkeyField
                SavedDisplayInfo
  Nvidia/       NvApi (+ NvidiaDisplay, MonitorDescriptor)  IDisplayColorBackend  MonitorIdentity
                NvTweakColorRegistry
  Platform/     ForegroundWatcher  IForegroundMonitor  ForegroundProcess  ProfileIconLoader
                SingleInstanceService  Win32HotkeyRegistrar  IHotkeyRegistrar
                StartupRegistry  IStartupRegistry
  Services/     ConfigurationService  ConfigurationBackupService  ProfileStore  ProfileListService
                NvidiaService  DisplayProfileSyncService  DisplayTopologyService
                DisplayColorController  ActiveProfileService  AutomaticSwitchingService
                HotkeyService  HotkeyCaptureController  StartupRegistrationService
                UpdateService  UpdateInstaller (+ IUpdateInstaller)  UpdateCheckService
                SavedDisplaysService
    Editing/    ProfileEditorSession  ProfileEditor  ProfileEditingService  LivePreviewController
  Shell/        MainWindowHost  TrayService  DialogService
  ViewModels/   MainWindowViewModel  ProfilesViewModel  ProfileSettingsViewModel  HotkeysViewModel
                HotkeyFieldViewModel  ApplicationSettingsViewModel  StatusBarViewModel
  Behaviors/    HotkeyCaptureBehavior  TapCommandBehavior
  Dialogs/      AboutWindow  AdvancedSettingsWindow  UpdateAvailableWindow  RunningAppsWindow
                ManageDisplaysWindow  CenteredDialogWindow
  Styles/       Palette  Icons  Controls  Panels
  Views/        ProfilesView  ProfileSettingsView  HotkeysView  ApplicationSettingsView  StatusBarView

tests/
  NvProfileSwitcher.Core.Tests/   reference Core tests (unchanged) + tests of the extracted Core
                                  and of the services that need no UI, over AppServices with fakes
  NvProfileSwitcher.UiTests/      headless Avalonia: layout, ViewModels with fakes, interaction,
                                  integration over the real composition, architecture
```

Copied code keeps the `NvProfileSwitcher` and `NvProfileSwitcher.Nvidia` namespaces, so the
reference Core tests run unchanged. New code uses the namespace of its folder.

**Not migrated:** `StatusText` (hidden in the reference and never read, §17.6), the duplicated
`ExecutableMatches` in MW (the `SwitchingCore` one is used), the duplicated configuration load
(unified in `ConfigMappingCore`), and the `CenteredMenuFlyoutItemTheme` and
`executablePathDisplay` styles (unused once the Backup menu moved into Advanced).

---

## 5. Responsibilities

### 5.1 State, services and their API

| Class | Owns | Main API | From MW |
|---|---|---|---|
| `ProfileStore` | the list (Windows first), `UnsavedNewProfile`, the physical monitor identities found shared | read-only `Profiles`, `Windows`, `ApplicationProfiles`, `NonUniqueMonitorIds`; `Load`, `AddUnsaved`, `MarkSaved`, `Remove`, `Move`, each keeping the ▲▼ state, the unsaved profile and the profile override consistent (§11); `LoadNonUniqueMonitorIds`, `MarkNonUnique` (§22.1) | 290–372, 2050 |
| `AppSettings` | the five checkboxes, the two "Remember" flags, the four global hotkeys | INPC properties | `loadedConfig`, checkboxes |
| `SwitchingState` | `OverrideMode`, `OverrideProfileIndex`, `GlobalPaused`, `ActiveProfileName`, `DisplayedActiveProfile` | INPC properties | fields 28–30, 49 |
| `ConfigurationService` | config path, `savingBlocked`, `pendingChanges`, the pending load notice | `Load`, `ReportLoadProblem`, `Save`, `Save(profile, replacement, reportFailure)`, `SaveIfPending`, `ImportAsync`, `ExportAsync` (§12) | 326, 1647, 1945–2077 |
| `ConfigurationBackupService` | — | `ImportAsync`, `ExportAsync`: the import and export sequences with pickers and messages | 1945–2047 |
| `NvidiaService` | backend (`IDisplayColorBackend`), `Displays` (identified only), `UnidentifiedCount`, `DriverVersion`, `IsAvailable` | `Initialize` (ordered replacement, §17.2), `ReportUnavailable`, `RefreshDisplays(nonUniqueIds)` (enumerates and identifies, §22.1; returns the identities found shared), `RefreshDriverVersion` (the installed driver, or the API's, §22.5), `GetVibrance`, `GetHue`, `Apply`, `Dispose` | 1696–1781 |
| `DisplayProfileSyncService` | — | `Synchronize(newlySharedIds)`: records the shared identities, `DisplayProfileSyncCore` (adoption, shared identities, values for every display, §22.1), reads a display only when it was never written (§22.3), saves if anything changed; returns the moved monitor ids (`DisplayRemap`) | 1879–1922 |
| `DisplayTopologyService` | debounce timer, whether the change was retried, whether a resume is being settled | `HandleMessage` (WM_DISPLAYCHANGE and the resume notifications of WM_POWERBROADCAST, never marked handled), `RetryOnce` (§22.1), `TopologyChanged`, `Resumed` (§22.4), `Stop` | 572–575, 1857–1877 |
| `DisplayColorController` | base profile, preview layer, displays to write, displays written | `Batch(throwOnFailure)`, `SetBase(profile, allDisplays)`, `RestoreWindows(force)`, `SetPreview`, `ClearPreview`, `RefreshAll`, `HasWritten`; event `WriteFailed`. The only writer of display colors (§22.2) | 1838–1856, 2193–2300 |
| `ActiveProfileService` | — | `Check`, `ReapplyRealColors(allDisplays)`, `Invalidate`, `Reevaluate` (invalidate and check), `PublishActiveProfile`; event `ProfileSwitching`. The only writer of `ActiveProfileName` and `DisplayedActiveProfile`; chooses the base profile of `DisplayColorController`; holds the rule that the preview owns the display; normalizes an invalid override in `Check`, as the reference did | 1741–1752, 1792–1832, 2227–2268 |
| `AutomaticSwitchingService` | foreground monitor, "monitor created" | `Start`, `Stop`, `Dispose`, `IsSwitchingActive`, `SetGlobalEnabled`, `ToggleGlobal`, `ToggleWindowsOverride`, `ResumeAutomatic`, `ToggleProfileOverride`, `ApplyImportedState` | 598–676, 1782–1790, 2028–2039 |
| `ProfileEditorSession` | source, draft | `HasChanges`, `HasUnsavedColorChanges` (pure) | — |
| `ProfileEditor` | `SelectedProfile`, session, `SelectedDisplay`, slider values, draft hotkey | `BeginFirstProfile`, `SetSelection`, `Begin`, `AddUnsaved`, `Commit`, `Discard`, `StopEditing`, `SelectDisplay`, `RefreshDisplays(displays, remaps)` (the draft follows moved monitor ids), `SetName`, `AddExecutable`, `ReplaceExecutable`, `RemoveExecutable`, `SetDraftHotkey`, `SetCurrentValues`, `ResetValues`, `CopyColorsFrom`, `CompleteDraft`, `AcceptValues`; `IsDirty`, `HasUnsavedColorChanges`, `IsEditingUnsavedProfile`; events `EditingTargetChanging`, `DisplayChanged`, `EditingStarted`, `ValuesLoaded`, `DraftChanged`. Every draft change announces itself; no dialogs and no effects outside its state and the store; it never reads a display (§22.3) | 168–250, 1176–1196, 1494–1612 |
| `ProfileEditingService` | whether a discard confirmation is open | `ConfirmAndDiscardAsync` (the single discard confirmation), `RequestSelectionAsync`, `SetValues`, `Reset`, `CopySettingsAsync`, `CancelAsync`, `SaveAsync` | 168–250, 1302–1612 |
| `ProfileListService` | — | `AddAsync`, `RemoveSelectedAsync`, `MoveAsync(profile, direction)`, `ToggleEnabled` | 265–316, 1176–1260 |
| `LivePreviewController` | `IsActive` | `Preview` (begin and show), `Accept`, `Discard`, `Suspend`, `Resume`, `ReapplyIfPreviewing`. Writes nothing itself: the preview is the controller's layer over the base profile | 1605–1612, 2193–2300 |
| `HotkeyService` | window handle, "registered", the registered IDs and the profile of each profile hotkey ID, `IsMessageDialogOpen` | `Attach`, `RegisterAll`, `UnregisterAll`, `Refresh`, `TryTest`, `HandleMessage`, `ShowMessageAsync`, `Format`; event `Triggered` | 551–597, 835–924 |
| `HotkeyCaptureController` | field in capture, caret timer | `Begin`, `End`, `EndUnlessMessageOpen`, `FocusLost`, `KeyPressed`, `StoredHotkey`, `CaptureTarget` | 698–1003 |
| `StartupRegistrationService` | — | `Apply()` writes or deletes `HKCU\…\Run\NvProfileSwitcher` | 499 |
| `UpdateCheckService` | whether an installation is running | `CheckAsync(manual)`, `InstallAsync(release, progress)` (§14.1) | 2339 |
| `SavedDisplaysService` | — | `GetSavedDisplays`, `Remove(monitorId)` | 2128–2157 |

**Who writes the profile override** (`OverrideMode`, `OverrideProfileIndex`):
- `AutomaticSwitchingService`: the override toggles, Resume and the global switch (transitions
  in `OverrideCore`), saving when the override is remembered;
- `ActiveProfileService.Check`: an invalid profile override returns to Automatic, as the reference did;
- `ProfileStore`: the index follows its profile when the list changes (`OverrideCore.AfterRemove`
  and `AfterMove`);
- `ConfigurationService`: the load and the import restore it (`ConfigMappingCore.ApplyState`).

No ViewModel writes it.

### 5.2 Seams

These are the only interfaces; each one exists because a test replaces it:

- `IDisplayColorBackend`: the NVIDIA API (implemented by `NvApi`);
- `IForegroundMonitor`: the foreground window hook (implemented by `ForegroundWatcher`);
- `IHotkeyRegistrar`: `RegisterHotKey`, `UnregisterHotKey` and key names;
- `IStartupRegistry`: the Run key;
- `IDialogService`: every dialog, picker and link;
- `IUpdateInstaller`: preparing, launching, cancelling and discarding an update (implemented by
  `UpdateInstaller` over `UpdateService`);
- plain delegates and values: the foreground executable (`Func<string>`), the release queries,
  the configuration path, the executable path, shutdown (`Func<bool>`, whether the application
  closes) and the single instance.

`AppPlatform` groups all of them. `AppHost.Start` passes the real implementations;
`TestComposition` passes fakes to the same `AppHost.Compose`, and `SwitchingFixture` passes fakes to
the same `AppServices.Create`, so the integration and service tests run the real graph.

---

## 6. MVVM, Views and code-behind

### 6.1 CommunityToolkit.Mvvm 8.4.2

- Used: `ObservableObject`, `[ObservableProperty]` on partial properties, `[RelayCommand]` and
  async commands. All of it is source-generated, without reflection, so it is trim- and
  NativeAOT-safe.
- Not used: `ObservableValidator` (reflection-based) and `Messenger` (communication between
  services is explicit).
- `ProfileListItem` keeps its own INPC implementation from the reference.
- Bindings are always `{CompiledBinding}` with `x:DataType`.

### 6.2 Views and behaviors

- `MainWindow.axaml.cs` is exactly `public MainWindow() => InitializeComponent();` and each View
  has the same shape. An architecture test compares the files with that exact form.
- `MainWindow.axaml` only adds `x:DataType` and one `DataContext="{CompiledBinding …}"` per View;
  the layout is unchanged.
- Input events without a `Command` use attached behaviors (`AvaloniaProperty.RegisterAttached` +
  `AddHandler`), with no code-behind:
  - `HotkeyCaptureBehavior.Field`: PointerPressed, GotFocus (only `NavigationMethod.Tab`),
    LostFocus and KeyDown of a `Border.hotkeyField` → `HotkeyFieldViewModel`;
  - `TapCommandBehavior.Command`: `Tapped` of the `TextBlock.footerLink` links.
- The secondary dialogs (About, UpdateAvailable, Advanced, RunningApps, ManageDisplays) keep their
  reference code-behind; the rule applies to MainWindow and the Views.

### 6.3 ViewModels and their dependencies

| View | ViewModel | Depends on |
|---|---|---|
| `ProfilesView` | `ProfilesViewModel` | `ProfileStore` (the list), `ProfileEditor` (the selection), `ProfileEditingService` (selecting), `ProfileListService` (the list actions) |
| `ProfileSettingsView` | `ProfileSettingsViewModel` | `ProfileEditor`, `ProfileEditingService`, `HotkeyService` and `HotkeyCaptureController` (the profile hotkey field and Cancel), `IDialogService` (the executable pickers) |
| `HotkeysView` | `HotkeysViewModel` (4 × `HotkeyFieldViewModel`) | `HotkeyCaptureController`, `HotkeyService` |
| `ApplicationSettingsView` | `ApplicationSettingsViewModel` | `AppSettings`, `ConfigurationService`, `StartupRegistrationService`, `TrayService`, `MainWindowHost` (`SetTopmost`, `IsVisible`), `IDialogService`, `UpdateCheckService`, `SavedDisplaysService`, `ConfigurationBackupService` (one per Advanced button), development-build flag |
| `StatusBarView` | `StatusBarViewModel` | `NvidiaService`, `SwitchingState`, `IDialogService` |
| `MainWindow` | `MainWindowViewModel` | the five ViewModels; it neither calls nor listens to them |

The profile ViewModels only present: binding reentrancy (R1), the executable selector, the slider
formats and `CanCancel`; their commands delegate to the services. An architecture test keeps them
free of `ConfigurationService`, `ActiveProfileService`, `LivePreviewController`, `NvidiaService`,
`AutomaticSwitchingService` and `DisplayColorController`. The application settings remain direct
calls: each checkbox is one save and one effect.

**Editing is shared without coupling ViewModels:**
- **Selection:** `ProfileEditor.SelectedProfile` is the only source of truth.
  `ProfilesViewModel.SelectedProfile` requests a new one through `ProfileEditingService`. With
  "Keep editing", the editor keeps the previous profile and the ViewModel notifies again through
  `Dispatcher.Post` (list reentrancy, R1).
- **Draft:** `ProfileSettingsViewModel` listens to `EditingStarted`, `ValuesLoaded` and
  `DraftChanged` and loads its controls from the editor; each slider change goes back through
  `ProfileEditingService.SetValues`, which also previews it.
- **Profile hotkey:** `HotkeyCaptureController` writes it with `ProfileEditor.SetDraftHotkey`.
  Cancel's visibility reads `IsDirty` and `CaptureTarget`.
- **Profile change with a preview:** `LivePreviewController` listens to `EditingTargetChanging`.

---

## 7. Window host contract

`MainWindowHost` (Shell) is the only adapter of the main window.

| May | May not |
|---|---|
| Use the `Window` API: Show, Hide, Activate, Close, WindowState, ShowInTaskbar, ShowActivated, Topmost | Reference domain services, ViewModels, profiles, configuration, NVIDIA API or hotkeys |
| Translate window events into its own: `Opened`, `Activated`, `Deactivated`, `Minimized`, `Restored`, `Hidden`, `HidingToTray`, `TrayIconVisibilityRequested(bool)`, `Closing`, `Closed`. It is the **only** subscriber to the main window's `Opened`, `Activated`, `Deactivated`, `Closing`, `Closed` and `PropertyChanged` | Decide policies, such as whether minimizing hides to the tray |
| Install the WndProc hook once, on `Opened`; expose the window handle; dispatch raw messages (`AddMessageHandler`, `RemoveMessageHandler`, `DispatchMessage` → handled) | Interpret messages (WM_HOTKEY, WM_DISPLAYCHANGE) |
| Expose the `TopLevel` (owner, StorageProvider, Launcher) to `DialogService` | Show content dialogs |
| Run the close guard mechanics (`SetCloseGuard`): cancel, await the confirmation, close again | Know which condition asks for confirmation |
| Keep `hidingForTray`, `startHiddenToTray`, `allowCloseAfterConfirm`, `wndProcHookInstalled` | Keep any other behavioral state |

- **Infrastructure references** (not behavioral state): the attached `window`; `messageHandlers`
  (replaced on each add or remove, never mutated, so a dispatch walks the list it started with);
  `WindowHandle`; the two close-guard delegates.
- **Show and hide**, as in the reference:
  - `ShowFromTray()` hides the tray icon first, as `App.ShowMainWindow()` did;
  - `HideToTray()` raises `HidingToTray` before hiding;
  - `StartHiddenToTray()` is called after `Attach` and before the window is shown; without an
    attached window it throws, so an ordering mistake cannot silently ignore `--minimized`. It
    sets `ShowActivated = false`.
- **Hidden start:** on `Opened` the host installs the hook, raises `Opened` (all startup steps run)
  and only then, if it started hidden, clears the flag and hides to the tray: the reference order.
  The flag is consumed once; if a startup step throws, the window is not hidden, as in the reference.
- **Close guard:** `SetCloseGuard(confirmationRequired, confirmAsync)` requires both delegates and
  can be set once. While `confirmationRequired()` is true, the window closes only after
  `confirmAsync()` confirms.
- **Properties:** `IsActive`, `IsVisible`, `IsMinimized`, `IsShownNormally`, `SetTopmost(bool)`.
- **Size:** there is no line limit; every member must map to a row of the table above or to an
  infrastructure reference.
- **Architecture tests:** parameterless constructor and only `System`/`Avalonia` field types; no
  file outside the host subscribes to those six events of the main window, followed through every
  access path (the instance created in `AppHost`, `Owner`, the desktop lifetime's `MainWindow` and
  local aliases); the main window is created only in `AppHost`.

---

## 8. Coordinators

Common rules, checked by the architecture tests and by review:
- no state (only `readonly` dependencies);
- each handler is a sequence of service calls in the reference order;
- the only logic allowed is reading a condition the reference evaluated at that point;
- the step lists below are closed: a new step goes to a service, not to a coordinator.

There is no line limit: a coordinator that touches many subsystems is a long sequence, not a
design problem, and the rules above keep it from growing logic of its own.

| Coordinator | Trigger | Steps |
|---|---|---|
| `StartupCoordinator` | `LoadBeforeWindow()` | `configuration.Load()`; `startupRegistration.Apply()`; `editor.BeginFirstProfile()` |
| | `PrepareWindow()` (after `Attach`) | `host.SetTopmost(settings.AlwaysOnTop)` |
| | `host.Opened` (every opening, also from the tray, §17.1) | 1. `hotkeys.Attach(handle)` (registers only on the first opening with a handle); 2. inside `try`, in one batch that throws a failed write: `nvidia.Initialize()`, `activeProfile.PublishActiveProfile()`, `displayRefresh.Refresh()`, then if `!IsSwitchingActive` `colors.RestoreWindows(force: GlobalPaused)` (option C, §17.7); after the batch `automaticSwitching.Start()`; `catch` → `nvidia.ReportUnavailable()`; 3. `updateCheck.CheckAsync(manual: false)` without awaiting; 4. `configuration.ReportLoadProblem()` |
| `DisplayRefreshCoordinator` | `Refresh()` from `Opened` and `topology.TopologyChanged` (the latter first runs `nvidia.RefreshDriverVersion()`, §22.5) | if NVIDIA API not initialized, return; read whether the draft had changes; in one batch (§22.2): inside `try`/`catch` that keeps the current state: `preview.Discard()`, `nvidia.RefreshDisplays(nonUniqueIds)`, `colors.RefreshAll()`, `displaySync.Synchronize(newlySharedIds)`, `editor.RefreshDisplays(displays, remaps)`, and if `IsSwitchingActive`: `activeProfile.Reevaluate()`; then, if the draft had no changes, `editor.Begin(source, selected monitor)` (§15, 10-C7); after the batch, if a display has no identity, `topology.RetryOnce()` (§22.1) |
| | `Recover()` from `topology.Resumed` | if NVIDIA API not initialized, return; `nvidia.Initialize()` (`catch` → `nvidia.ReportUnavailable()`, return); in one batch: `Refresh()`, then if `GlobalPaused` `colors.RestoreWindows(force: true)` (§22.4) |
| `WindowActivityCoordinator` | Activated | `nvidia.RefreshDriverVersion()`; `preview.Resume()` |
| | Deactivated | `preview.Suspend()`; `capture.EndUnlessMessageOpen()` |
| | Minimized | `capture.End()`; `preview.Suspend()`; with Minimize to tray `host.HideToTray()`, otherwise hide the tray icon |
| | Restored | `Dispatcher.Post(preview.Resume)` |
| | Hidden | `preview.Suspend()` |
| | HidingToTray | `capture.End()` |
| `HotkeyActionDispatcher` | `hotkeys.Triggered` | ShowHide → `preview.Discard()` + (visible ? `HideToTray` : `ShowFromTray`); WindowsOverride, ResumeAutomatic, GlobalSwitching (`ToggleGlobal`) and Profile(i) → the matching `AutomaticSwitchingService` method |
| `ShutdownCoordinator` | close guard | confirmation required while `editor.IsDirty`; confirming runs `editing.ConfirmAndDiscardAsync()` |
| | Closing | `configuration.SaveIfPending()` (§12.4); `automaticSwitching.Stop()`; in one batch that throws a failed write: `preview.Suspend()`, `colors.RestoreWindows()` |
| | Closed | `hotkeys.UnregisterAll()`; `host.RemoveMessageHook()`; `topology.Stop()`; `automaticSwitching.Dispose()`; `nvidia.Dispose()`; unregister the single-instance show handler; `tray.Dispose()` |

`TrayService` (Shell) owns its menu (Open, Global Switching ☑, Check for updates, About, Exit) and
calls the host, switching (`ToggleGlobal`), update check and dialogs directly. `AppHost` only
builds and attaches; it subscribes no handlers of its own.

---

## 9. Startup

1. `Program.Main`: `--apply-update` runs the update helper and exits; the single-instance mutex
   sends "SHOW" and exits in a second instance; the primary starts listening.
2. `App.OnFrameworkInitializationCompleted` → `AppHost.Start(desktop)` builds `AppPlatform` and
   calls `Compose`:
   1. the window host and `DialogService`, then `AppServices.Create` (state first, then the
      configuration, NVIDIA, display, color, hotkey, editor, active profile, preview, switching,
      capture, editing, list, update, startup registration, saved displays and backup services),
      and the topology and hotkey message handlers on the host;
   2. the tray and the coordinators;
   3. `StartupCoordinator.LoadBeforeWindow()`;
   4. the ViewModels and the window (`new MainWindow { DataContext = … }`), `host.Attach(window)`,
      `StartupCoordinator.PrepareWindow()`;
   5. the single-instance show handler (a request that arrived earlier is handled now).
3. With `--minimized`, `host.StartHiddenToTray()`.
4. `Opened` runs the steps of §8.

---

## 10. Shared state

| State | Owner | Notification |
|---|---|---|
| Profiles and the unsaved new profile | `ProfileStore` | `ReadOnlyObservableCollection` + INPC |
| Settings and global hotkeys | `AppSettings` | INPC |
| Override, pause, active profile, published active profile | `SwitchingState` | INPC |
| NVIDIA API, identified displays, how many have no identity, driver | `NvidiaService` | INPC |
| Selection, draft, display and values in edit | `ProfileEditor` | events + INPC |
| Whether a discard confirmation is open | `ProfileEditingService` | — |
| Whether the preview is active | `LivePreviewController` | — |
| What each display shows (base profile, preview layer), the displays to write and the ones written | `DisplayColorController` | `WriteFailed` |
| Physical monitor identities found shared | `ProfileStore` | — |
| Hotkey capture | `HotkeyCaptureController` | INPC |
| Registered hotkeys, hotkey message dialog open | `HotkeyService` | — |
| Configuration persistence state | `ConfigurationService` | — |
| Whether an update is being installed | `UpdateCheckService` | — |
| Window | `MainWindowHost` | events |

**Window facts** (passed in the `ServiceEnvironment`):
- "the window is active": `ActiveProfileService` (with the editor's unsaved color changes, the
  preview owns the display and automatic switching waits) and `HotkeyCaptureController` (a capture
  starts only then);
- "the window is shown normally": the preview may resume;
- "set topmost": the import applies the imported Always on top.

---

## 11. Profile list consistency and hotkeys

Profile hotkeys used to be registered with ID `4000 + application profile index` and released by
walking the current list, so every change that shifted positions had to unregister them **before**
the change (R2, enforced in phase 9 with a scope in `ProfileListService`). Phase 10 removed the
cause instead:

- **`HotkeyService` registers per profile.** It still gives each profile hotkey an ID from 4000 at
  registration time, but it remembers the IDs it registered and the profile each one belongs to.
  `UnregisterAll` releases exactly what was registered (and the test ID); `HandleMessage` looks up
  the profile's current position when the key is pressed and ignores a profile no longer in the
  list. A list change can no longer make a hotkey trigger another profile, whatever the order of
  the calls.
- **`ProfileStore` keeps the list consistent.** The list is read-only outside the store; `Remove`
  and `Move` keep the profile override on its profile (`OverrideCore.AfterRemove`, `AfterMove`)
  and every change updates the ▲▼ state and the unsaved new profile.
- **Registering again is a consequence, not a protocol.** After a change of what should be
  registered, its owner calls `HotkeyService.Refresh(showAggregateMessage)`:

| Change | Owner | Registers again |
|---|---|---|
| Remove | `ProfileListService.RemoveSelectedAsync` | after the change, with the message |
| Move | `ProfileListService.MoveAsync` | after the change, with the message (as the reference, which reported hotkeys that cannot be enabled) |
| Enable or disable | `ProfileListService.ToggleEnabled` | after the change, with the message |
| A profile hotkey | `ProfileEditingService.SaveAsync` (through `ProfileEditor.Commit`) | after the save, with the message |
| Global hotkeys | `HotkeyCaptureController` | when the capture ends |
| The whole list | `ConfigurationBackupService.ImportAsync` | released before the file is written, registered again at the end, also when the import fails, before its message |
| Discarding the unsaved new profile | `ProfileEditor.Discard` | nothing: the unsaved profile has no registered hotkey, and the others stay with their profiles |

- **Guarantees, each checked by a test:**
  - an architecture test (`OnlyTheOwnersChangeWhatTheHotkeysRegister`) restricts the writes of
    `Enabled`, `Hotkey`, the global hotkeys and the store's changes to the owners above;
  - every `TestComposition` checks, when disposed, that the registered hotkeys are exactly the ones
    the state calls for, so a path that forgets to register again fails its own test;
  - integration tests press the hotkeys after Move, Remove and discarding the unsaved profile and
    check which profile each one triggers.
- **Forgetting a refresh** cannot trigger a wrong profile: at worst a combination stays reserved
  until the next registration.

---

## 12. Persistence

### 12.1 Compatibility with the reference

- Same path (`%APPDATA%\NvProfileSwitcher\config.json`), types, JSON names, enums as strings,
  indented output and `NvProfileSwitcherJsonContext` (source-generated).
- Same atomic write: a `.tmp` file (`.import.tmp` for imports) moved over `config.json`.
- Kept as in the reference: a missing file starts with defaults and is not created; the load does
  not validate (the import does, strictly); development builds force `CheckForUpdates` to false and
  save it that way; `OverrideProfileId` is saved only when the override is remembered.
- Every application profile gets a new `Id` when it is created and keeps it in every save; the
  import rejects empty or repeated `Id`s. The load does not validate, so it reads `Id`s as they are
  (a missing one is read as empty). Configurations of the C++ application are not migrated (§2).
- Fixtures that load and save produce byte-identical JSON.
- *Phase 11:* the optional `"Non-unique Monitor Ids"` list (§22.1) is written only when it has
  entries, so configurations without shared monitor identities keep their exact format; the import
  accepts it absent, null or as a list of non-empty strings. Display values without a monitor id
  are dropped on load (§22.3).

### 12.2 Loading

| `config.json` | Session | File | Notice (once, on the first `Opened`) |
|---|---|---|---|
| missing | defaults | not created | none |
| valid | loaded | normal saves | none |
| exists but cannot be read (locked, permissions) | defaults | **protected**: nothing is saved over it in this session | "could not be read … your configuration file was not modified, and changes made in this session will not be saved. Restart NvProfileSwitcher once the file is accessible." + path |
| invalid JSON or content | defaults | an exact copy `config.invalid-yyyyMMdd-HHmmss.json` is written next to it (`CreateNew`, flushed, then read back and compared byte for byte); the next save replaces `config.json` | "is not valid … The original file was kept for manual recovery as:" + copy path |
| invalid, and the copy failed | defaults | **protected** | "is not valid … A copy of it could not be kept, so your configuration file was not modified and changes made in this session will not be saved." + path |

A name collision adds `-1`, `-2`, … to the copy name. The notice is shown with
`ShowMessageAsync("NvProfileSwitcher", …)` after the startup steps of `Opened`, once per session.

### 12.3 Saving

- `bool Save(bool reportFailure = true)` writes the whole state and returns true only once
  `config.json` was replaced. It never throws.
- The unsaved new profile is left out of every save until "Save profile" (§15, 7b-3).
- **Protected mode:** a save writes nothing, records the pending changes and returns false
  without a notice (the load notice already explained it).
- **Failure notice:** "Your changes could not be saved to the configuration file. They remain
  active until NvProfileSwitcher is closed." It is shown on the first failure of a streak; after
  a successful save, the next failure is reported again. Callers that pass `reportFailure: false`
  report the failure themselves.
- **Save profile** saves first and applies only once saved: `Save(source, replacement, false)`
  writes the state with the edited copy in place of the profile without changing anything. On
  failure, "Cannot save profile — The profile could not be saved to the configuration file." is
  shown and the draft, hotkeys and preview stay as they were. On success, in one color batch:
  commit the draft, register the hotkeys again, accept the preview, reevaluate and begin editing
  again. The draft's display values replace the profile's values for the same monitors and keep
  the others (§22.3).

| Caller | Save | On failure |
|---|---|---|
| Remove, Toggle (`ProfileListService`) | `Save()` | notice; the reevaluation is skipped, as in the reference |
| Move (`ProfileListService`) | `Save()` | notice |
| Save profile (`ProfileEditingService`) | `Save(source, saved, false)` | "Cannot save profile" message; nothing is applied |
| Application Settings checkboxes | `Save()` | notice; Start with Windows / Start minimized write the Run key only after a successful save; disabling Minimize to tray hides the icon only after a successful save |
| Advanced Save (Remember flags) | `Save()` | notice; the flags stay applied |
| Global hotkey stored (`HotkeyCaptureController`) | `Save()` | notice |
| Remove a saved display (`SavedDisplaysService`) | `Save()` | notice |
| Display synchronization (`DisplayProfileSyncService`) | `Save()` when something changed | notice |
| Global switching / override toggles (`AutomaticSwitchingService`) | `Save()` when remembered | notice |
| Export (`ConfigurationBackupService`) | `Save(false)` before the picker | "Export configuration — The configuration could not be exported."; nothing is exported |
| Closing (`ShutdownCoordinator`) | `SaveIfPending()` | silent (§12.4) |

### 12.4 Closing

`SaveIfPending()` runs first in `Closing`, before anything that might throw: when the last save
failed (or the session is protected), it tries once more without a notice. It does nothing when
everything was saved. In protected mode it writes nothing.

### 12.5 Import and export

- **Export** saves (as the reference did, before the picker, even if it is cancelled), then copies
  `config.json` to the chosen file (`NvProfileSwitcher-Config.json`). A failed save exports nothing.
- **Import:** pick the file → strict validation (`ConfigurationCore.TryValidateImportedConfiguration`;
  "Invalid configuration file" otherwise) → confirm the discard of unsaved changes (§15, 8-D1) →
  with the hotkeys released (§11): discard the preview, write the JSON exactly as read through
  `.import.tmp`, apply it with `CreateProfiles` + `ApplyState` (never `Load()`), apply topmost,
  synchronize displays, apply the startup registration and select Windows → register the hotkeys
  again, also on failure → `ApplyImportedState()` clears `ActiveProfileName` and then forces Windows (paused) or starts the
  monitor and checks (active), as MW 2028–2039.
- A successful import clears the protected mode and the pending changes: the file then matches the
  state. Any failure from the write on shows "Import configuration — The configuration could not be
  imported." after the hotkeys were registered again.
- The load and `ApplyState` never reset `ActiveProfileName`; at startup it keeps the initial
  "Windows", as MW 28.

---

## 13. Lifecycle, events and disposal

### 13.1 Object lifetimes

- **Application-lifetime objects** — state, services, coordinators, Shell adapters, ViewModels and
  the main window — are created once by `AppHost.Compose` and live until the process exits. Their
  subscriptions to each other are never removed, and they do not implement `IDisposable` for
  that: they share a single lifetime, and nothing outlives the graph.
- **Native and OS resources** are released explicitly, in `ShutdownCoordinator.OnClosed`, in the
  reference order: the hotkeys, the WndProc hook, the topology timer, the foreground hook
  (`AutomaticSwitchingService.Dispose` → `IForegroundMonitor.Dispose`, only if it was created), the
  NVIDIA API (`NvidiaService.Dispose` → `IDisplayColorBackend.Dispose`), the single-instance show
  handler and the tray icon. `Program` disposes `SingleInstanceService` in its `finally`.
- Only the classes that own such a resource implement `IDisposable`: `NvidiaService`,
  `AutomaticSwitchingService`, `TrayService`, `SingleInstanceService`, `NvApi`/`IDisplayColorBackend`
  and `ForegroundWatcher`/`IForegroundMonitor`. `ProfileListItem` loads its icon the first time a
  list row reads it and disposes a loaded icon when its executables change; drafts and copies never
  load one.
- **Rule for temporary objects:** an object with a shorter lifetime may subscribe to an
  application-lifetime object when it needs to, provided it removes every such subscription when
  its own lifetime ends; otherwise the longer-lived object would keep it alive.
- **Current code** (verified):
  - dialogs subscribe only to their own controls and receive what they need as values and
    callbacks (Advanced gets `AdvancedSettingsOptions`; Manage displays gets the list and a remove
    callback), so they hold no subscription to a service;
  - attached behaviors subscribe to their element and, in `HotkeyCaptureBehavior`, to the field
    ViewModel's `FocusRequested`; they remove those handlers when the attached value changes;
  - the update dialog receives its install action as a callback.
- `MainWindowHost.AddMessageHandler`/`RemoveMessageHandler` is the only explicit add/remove API;
  the topology and hotkey handlers stay for the application's lifetime, and the hook itself is
  removed on `Closed`.

### 13.2 Window events and "suspend/resume"

- As in the reference, "suspend and resume" are the window events (preview and capture, §8), the
  watcher's Start/Stop with the global switch, and WM_DISPLAYCHANGE with its 500 ms debounce
  (enumerates displays and handles again).
- The return from sleep or hibernation (`WM_POWERBROADCAST` with a resume notification) initializes
  the NVIDIA API again and recovers the displays and colors (§22.4). Otherwise the NVIDIA API is
  initialized again only on every `Opened`, also when shown from the tray, which retries after a
  failure (§17.1).
- The WndProc hook uses `Win32Properties.AddWndProcHookCallback` / `RemoveWndProcHookCallback`.

### 13.3 Closing

- Close or Exit → the close guard (§7, §8): with unsaved editor changes it asks through the single
  discard confirmation; "Keep editing" keeps the window open (Exit from the tray included);
  "Discard" discards and closes. A second close request while the confirmation is open does not
  open another one; the open one decides. If a confirmed close does not happen, the host keeps its
  guard for the next one.
- `Closing` → `Closed` run the steps of §8. Exit from the tray hides the icon and calls
  `TryShutdown`.

---

## 14. Application Settings and Advanced

**Application Settings:** Start with Windows, Start minimized, Minimize to tray, Always on top and
Check for updates save at once. Start with Windows and Start minimized rewrite the Run entry
(`"exe" [--minimized]`) after a successful save; it is also rewritten at every startup. Check for
updates moved here from the reference's Advanced dialog; in development builds it is disabled,
unchecked, with the tooltip "Update checks are disabled in development builds.". "Advanced…" opens
the dialog.

**Advanced Settings** (`AdvancedSettingsWindow`, width 390):

```
Advanced Settings
Updates     [Check for updates]
State       ☐ Remember Global Switching state
            ☐ Remember Windows Override state
Displays    [Manage displays...]
Backup      [Import...]  [Export...]
                                       [Cancel] [Save]
```

- Four vertical sections with the title on top; margin 20, 8 inside a section, 12 between
  sections and 20 before Cancel/Save. The two "Remember" use the Application Settings spacing.
  Import/Export, Cancel and Save have `MinWidth` 92.
- Check for updates, Manage displays, Import and Export act at once; Save and Cancel only affect
  the two "Remember" flags.
- While Advanced is open it owns every dialog and picker, including a hotkey message after an
  import (`DialogService`).
- After an applied import the dialog reloads the two "Remember" flags from the imported state, so
  a later Save does not revert the import; when the import was not applied it keeps what the user
  checked.
- `ManageDisplaysWindow`: saved monitors of all profiles, connected first (with their label); only
  disconnected ones can be removed, with confirmation, from every profile, and the configuration
  is saved.

---

### 14.1 Updates

- `UpdateCheckService.CheckAsync(manual)` checks the latest release (automatic: silent and only when
  enabled; manual: reports every outcome; development builds never check) and offers a newer one
  with `UpdateAvailableWindow`, which receives the install action.
- `UpdateCheckService.InstallAsync(release, progress)`:
  1. `IUpdateInstaller.PrepareAsync` downloads, verifies and stages the update; nothing in the
     installation changes;
  2. unsaved editor changes are confirmed for discard, through the single discard confirmation;
     "Keep editing" discards the staged update;
  3. `Launch` starts the helper or the installer and the application closes through the platform,
     with nothing awaited in between, so the user cannot change anything again.
  4. If the application still does not close, a launched portable update is cancelled: its helper is
     still waiting for this process to exit, so it has not changed any file, and it is ended and
     its files deleted. A launched installer is left alone: it closes the application itself.
- When the update is not launched or the application does not close, the dialog is as it was
  before Update was clicked, with no new text. A failure to prepare or launch is reported in the
  dialog as before. While one installation runs, another one (a second update dialog) does nothing.
- **Starting again after an installed update.** The installer is started elevated (`runas`) and
  runs `/SILENT`; its `[Run]` entry is `skipifsilent`, because whatever an elevated Setup starts
  is elevated too, or runs as the administrator who approved the prompt. Instead, `Launch` starts
  a copy of the running executable from the update folder, with the user's normal privileges:
  `--relaunch-after-install <installer pid> "<executable>" "<update folder>"` waits up to an hour
  for the installer to exit, deletes the installer files and starts the installed executable (the
  new version, or the previous one when the installation failed or was cancelled). It starts
  nothing while the installer runs. A cancelled UAC prompt starts neither the installer nor the
  relauncher. An interactive installation still offers "Launch NvProfileSwitcher", which Setup
  runs without elevation.
- **Temporary files.** Each update uses its own folder in `%TEMP%\NvProfileSwitcher\Updates`. A
  failed, discarded or cancelled update deletes it at once. After a portable update the helper
  deletes the package, the extracted files and the backup. What a process still running from that
  folder keeps (the helper or the relauncher) is deleted when the primary instance starts
  (`UpdateService.DeleteLeftoverUpdates`, before any update of that process exists); a folder
  still in use then is deleted on a later start.

---

## 15. Deliberate deviations from the reference

Each deviation was approved before it was implemented and is covered by tests. Later phases may
supersede an earlier decision; that is noted in place.

**Phase 0–5**
- **0-1** "Check for updates" moved to Application Settings and saves at once; Displays and Backup
  moved into Advanced (§14).
- **2-1** NVIDIA API instance replaced in order, without leaks (§17.2).
- **2-2** Partial NVIDIA API initialization cleaned up (§17.3).
- **2-3** Status bar breakpoints and active profile trimming (§17.5).
- **3-1** On a later `Opened`, Windows is restored only when switching is not already active
  (option C, §17.7).

**Phase 6 — Hotkeys**
- **6-1** Ending a capture still registers every hotkey again (retrying the failed ones), but the
  aggregate message appears only at startup and after a real capture; the reference showed it on
  every deactivation or minimize.
- **6-2** Hotkey messages use `ShowMessageAsync("NvProfileSwitcher", …)`: modal over the visible
  window, standalone and centered when it is hidden or minimized. The reference was always modal.
- **6-3** A failed save of a global hotkey no longer closes the application. *Superseded by 9-2:*
  it is now reported.
- **6-4** The end of a capture is centralized in `HotkeyCaptureController.EndCore()`, with `End()`
  (accept, Esc, the same combination, minimize, hide to tray) and `EndUnlessMessageOpen()`
  (deactivation, focus loss). Only the focus loss of the capturing field ends it; in the reference
  any field did, and could leave the other with the caret.
- **6-5** After any validation message, a field still capturing gets the focus back; the reference
  did so only after a conflict.

**Phase 7 — Profiles and editor**
- **7-1** Confirmations (discard, remove, Copy settings, Cannot save) use
  `ShowDialogOrStandaloneAsync`: identical with the window visible, standalone when it is hidden
  (Exit from the tray with a dirty draft).
- **7-2** A failed save in Move no longer closes the application. *Superseded by 9-2:* it is now
  reported.
- **7b-1** Moving the edited profile keeps its draft and resumes the Live Preview afterwards. The
  reference lost the moved selection and started the draft over without asking, losing unsaved
  changes. Moving another profile still selects it and asks to discard unsaved changes.
- **7b-2** Discarding the unsaved new profile updates ▲▼; the reference left ▼ enabled on the new
  last profile.
- **7b-3** The unsaved new profile is not written to `config.json` until it is saved. The
  reference wrote it with any save (Move, Toggle of another profile, display sync, settings, global
  hotkeys) and could leave it in the file after a discard. `OverrideProfileId` still resolves over
  the full list, which `OverrideProfileIndex` refers to.
- **7b-4** Discarding the unsaved new profile keeps hotkeys and override on their profiles when it
  had been moved up; the reference left the profiles below with the IDs and index of their old
  positions until the next full registration. Now guaranteed by the store and the per-profile
  hotkeys (§11): nothing needs to be registered again.

**Phase 8 — Advanced**
- **8-D1** With unsaved changes, the import confirms the discard after validating the file and
  before changing anything; "Keep editing" cancels it. The reference imported first and asked
  afterwards, and keeping the changes left the editor on a profile that no longer existed.
- **8-D2** After an applied import, Advanced shows the imported "Remember" flags (§14).
- **8-D3** A failed save in Advanced's Save or when removing a saved display no longer closes the
  application. *Superseded by 9-2:* it is now reported.
- **8-D4** Advanced owns every dialog and picker while it is open (§14).
- **8-D6** `SavedDisplayInfo` moved unchanged to `Models/` so `SavedDisplaysCore` does not depend on
  `Dialogs/`.

**Phase 9 — Hardening**
- **9-1 Protected load.** A configuration that exists but cannot be read, or is invalid and cannot
  be copied, is never saved over in that session, and the user is told once (§12.2). The reference
  started with defaults and overwrote the file on its first save.
- **9-2 Failed saves are reported** with one notice per failure streak (§12.3). The reference
  ignored them, or closed the application in some paths.
- **9-3 Exact copy of an invalid configuration** before it can be replaced (§12.2).
- **9-4 Save profile saves first, then applies.** A failed save leaves the state, hotkeys and
  preview unchanged and the draft still unsaved, with its own message. The reference copied the
  draft first and ignored the failure, so the editor looked saved while `config.json` was not.
- **9-5 One silent retry on close** when the last save failed (§12.4).
- **9-6 Export after a failed save** exports nothing and reports it; the file would not match the
  current settings.
- **9-7 Profile list changes in one service** (§11). No visible change. *Superseded by phase 10:*
  the scope is gone; the store keeps the list consistent and the hotkeys stay with their profiles.

**Phase 10 — Integral refactor**
- **10-C6 A single discard confirmation at a time.** While the confirmation is open, another request
  (a second close from the tray, Add, an update) keeps the changes and opens nothing; the open one
  decides. The reference opened a second confirmation, and two answers discarded and closed twice.
- **10-C7 A clean draft stays clean when the displays change.** On a topology change, as on every
  opening, a draft without changes starts over on the refreshed displays, so values the display
  synchronization added to its profile do not count as unsaved. A draft with any user change is
  never touched. The reference did this only when the window opened, so connecting a monitor made
  closing ask to discard nothing.
- **10-C9 Unsaved changes are confirmed before an update is launched** (§14.1). The reference
  launched the helper first; keeping the changes left the dialog on "Restarting" with its buttons
  disabled and the helper waiting until it gave up. A launched portable update is cancelled if the
  application does not close, and a second installation does not start while one runs.
- **10-preview A topology change does not flash the real colors on the previewed display.** The
  reference discarded the preview first, so the display showed the active profile for the length
  of the refresh before the preview came back. *Superseded by phase 11:* the detached preview and
  its restorations are gone; the refresh is one batch (11-1).
- **10-icons** Profile icons are loaded only when a list row shows them (§13.1). No visible change.

**Phase 11 — Displays and colors (§22)**
- **11-1 Each display is written once per operation, with its final values.** No display shows an
  intermediate state: a topology change, moving the edited profile or showing the window again keep
  the preview on its display without the real colors in between, and a switch, an override, Save,
  Remove, closing and startup write every display once. The reference wrote the real colors before
  resuming a preview and wrote every display twice on a switch during a preview.
- **11-2 Ending a preview writes only its display**, unless the real colors are another profile now
  (Cancel, deactivating, minimizing, hiding, choosing another display or profile). The reference
  wrote the real colors to every display.
- **11-3 A display that fails does not keep the others from their values.** The first failure is
  reported once all displays were written, where the reference reported it (startup, closing,
  Global OFF), and is otherwise ignored as before, with the active profile applied again by the next
  check. The reference stopped at the failing display.
- **11-4 Monitors without a serial number, or with one another connected monitor shares, are
  identified by their connection** and keep their own values; a shared serial is recorded in the
  configuration ("Non-unique Monitor Ids") and its values are copied to each connection. The
  reference gave them no identity, so they were never applied or saved correctly.
- **11-5 A monitor on a new connection adopts the values of the only free saved connection of its
  model** when the enumeration leaves no doubt (§22.1). The reference had no connection identity.
- **11-6 A display whose identity cannot be read is left out**: it is not written, edited,
  synchronized or saved, and the refresh runs once more two seconds later. The reference offered it
  in the editor and added values with an empty monitor id to new profiles and drafts.
- **11-7 Saved values never come from written colors** (§22.3): the synchronization reads only
  displays this application has not written; saving a draft keeps the values it does not have; the
  editor never reads a display nor shows another display's values; values without a monitor id are
  dropped on load. The reference read the display, replaced all values on save and kept empty ids.

- **11-8 Recovery after resume** (§22.4). The reference had none; its attempt was reverted.
- **11-9 The status bar shows the driver installed in Windows** (§22.5). The reference showed the
  driver of the API library loaded at start until it was restarted.

**Version 3.0**
- **3.0-1 Interface texts unified.** Hotkeys, Global Switching, Windows Override, Support and
  Running Applications are the names of their features wherever they appear; messages say "hotkey"
  ("shortcut" is kept for Windows shortcuts in the installer). Changed texts: "Check for updates
  automatically" (the Application Settings checkbox; the actions keep "Check for updates"),
  "Global Switching", "Windows Override", "Resume auto switching", "Remember Windows Override
  state", "Support", "NVIDIA API:", "+ Running apps..." and the tooltip "Reset to NVIDIA
  defaults" without a final period. While Global Switching is off the active profile shows
  "Windows"; the reference showed "Windows (global off)", and "Global Switching: OFF" already
  shows the state. Configuration keys, internal names and behavior are unchanged.
- **3.0-2 New monitors start from the NVIDIA Control Panel settings** (§22.6). The brightness,
  contrast and gamma of a monitor new to the Windows profile come from the NVIDIA Control Panel
  when they can be used; the reference, and earlier versions, started them at 100, 100 and 1.00.
---

## 16. Reference behavior kept on purpose

- Configuration: §12.1.
- `ReapplyRealColors` with an **invalid** profile override applies Windows, without falling back to
  Automatic (it differs from `SwitchingCore`; R10).
- `Opened` repeats when the window is shown from the tray: the NVIDIA API, displays and the update
  check run again (§17.1).
- Every `Opened` starts an independent update check without a guard; showing the window while a
  check is pending, or while the update dialog is open standalone, may show a second
  `UpdateAvailableWindow` (`UpdateIntegrationTests.OpeningWhileACheckIsPendingStartsAnotherCheck`).
- After a failed validation the capture continues with the hotkeys unregistered until it ends;
  ending without a new hotkey shows the previous value, "None" included.
- Remove and Toggle skip the reevaluation when their save fails.
- Export saves before the picker even if it is cancelled; the unsaved new profile is not exported.
- Removing a disconnected display that is in the edited profile leaves the draft dirty without
  updating Cancel until the next change (Manage displays is not a display refresh, so 10-C7 does not
  apply).
- The double `BeginEditing` at startup is kept. *Phase 11:* the editor no longer reads the display
  and the double application on a switch is gone (11-1, 11-7).
- Move registers the hotkeys again, which reports hotkeys that cannot be enabled, although moving no
  longer needs it (§11).
- `NvApi.Apply` is `SetVibrance && SetHue && SetGamma` per display, short-circuited, and its result
  is ignored. An exception is swallowed in `Check` and `ReapplyRealColors`, reports "Unavailable"
  in `Opened`, and propagates in `Closing` and Global OFF. *Phase 11:* it no longer stops the other
  displays (11-3).
- Running apps and the `.exe` picker stay modal over the main window.

---

## 17. Findings and decisions

### 17.1 `Opened` repeats when the window is shown again

In Avalonia 12.1.2, `Window.Hide()` resets `_shown` and `ShowCore()` calls `OnOpened` each time the
window goes from hidden to visible (verified in the decompiled `Avalonia.Controls.dll` and by a
headless test); minimize and restore do not. The reference therefore re-ran its `Opened` handler
on every show from the tray: hotkeys did nothing thanks to the hook guard (MW 557), the NVIDIA API
was initialized again, displays were refreshed and Windows restored, and updates were checked
again. The migration reproduces all of it; the hidden start is consumed once by the host.

### 17.2 Ordered replacement of the NVIDIA API instance

The reference created a new `NvApi` on every `Opened` without releasing the previous one.
`NvidiaService.Initialize()` creates the new instance and releases the previous one only if the new
one initialized. The NVIDIA API reference count never reaches zero while the application runs and
exactly one instance is alive. When creation fails the current instance is kept and the footer
shows "Unavailable", as in the reference. That `NvAPI_Unload` unloads only at a zero count is taken
from NVIDIA's documentation and is checked on hardware.

### 17.3 Cleanup of a partial NVIDIA API initialization

If the `NvApi` constructor fails after `NativeLibrary.Load` (a missing export or a failing
`NvAPI_Initialize`), it frees the module and rethrows, without calling `NvAPI_Unload`. These are
the only changes to the copied `NvApi.cs`: it implements `IDisplayColorBackend`, has an `internal`
constructor taking the library name for tests, and the cleanup `try/catch`. The test loads a
private copy of a system library under a unique path and checks the module is not left loaded.

### 17.4 Phase 3 split of switching and colors

- `ColorApplicationService` only applied values (*phase 11:* replaced by `DisplayColorController`,
  §22.2); `ActiveProfileService` decides which profile is active (`SwitchingCore`,
  `ActiveProfileCore.RealColorsTarget`) and is the only writer of the active name;
  `AutomaticSwitchingService` owns the monitor, Global and the overrides.
- `AutomaticSwitchingService` tracks "monitor created", because the reference created the watcher
  on the first `Opened`: before that, Global ON, `Stop` and `Dispose` do not touch it.
  `IsSwitchingActive` = created and not paused; both the refresh and option C read it.
- `ReapplyRealColors` reads the foreground process only in Automatic mode; `Check` always does.
- `NvidiaService` without an initialized API returns `null`/`false` per display and `RefreshDisplays`
  throws; callers check `IsInitialized`, as the reference checked `nv is null`.
- `SwitchingState.DisplayedActiveProfile` is the snapshot published at the reference's
  `UpdateActiveProfileFooter()` points; the status bar formats it with `ActiveProfileCore.StatusText`.

### 17.5 Status bar

- The single-line threshold uses real values: `max-width:738` (worst case Unavailable/Unknown,
  measured on Windows with Segoe UI, the effective font since `fonts:Inter` is not resolved).
- The active profile has `CharacterEllipsis`, `MaxWidth="160"` and a tooltip with the full text.
- Three width modes: one row; up to 896 DIP the links move to a second row on the right; up to
  726 DIP "Active profile" and "Global Switching" also move to the second row, on the left and
  without their leading separator.
- Base styles are declared before the `ContainerQuery`s and the position of block B is not a local
  value, so the queries win.
- Windows-only tests check overlap and trimming at all sizes and at the 737–739, 895–897 and
  725–727 DIP edges.

### 17.6 `StatusText`

Hidden in the reference (`IsVisible="False"`), written 34 times and never read by code, bindings,
styles or tests. It was removed without migrating its messages; the control flow around each write
(guard `return`s, swallowing `catch` blocks, the preview's `nv.Apply` call) is kept and tested.

### 17.7 `Opened` after the first one (option C)

In the reference the display refresh reevaluated and applied the current profile, and then
`RestoreWindowsProfiles(force: globalPaused)` applied Windows without updating the active name,
leaving the colors and the footer out of sync (persistently with a profile override). With option
C, `Opened` restores Windows only when switching is not active (first `Opened` or paused): the
exact negation of the refresh's reevaluation condition, so exactly one of the two applies.

### 17.8 Live Preview and display topology changes

Replacing the display combo's items and choosing the display again runs its handler (discard, load
values, resume), so the reference resumes the preview after a refresh. The migration reproduces it
through `ProfileEditor.RefreshDisplays`.

### 17.9 Binding reentrancy (R1)

The profile list and the executable combo return to the previous selection by notifying an empty
value first, because after a control write the binding keeps the value it read and ignores a
notification of the same value (verified headless). Loaded values are clamped to the slider ranges,
as the reference sliders did.

---

## 18. Risks and validation

| # | Risk | Mitigation |
|---|---|---|
| R1 | Binding reentrancy (list revert, combo `null` on `ItemsSource` change, slider setters during load) | explicit loads; `Dispatcher.Post` on revert; headless tests (§17.9) |
| R2 | *Retired in phase 10.* Hotkey IDs followed list positions | hotkeys registered per profile and a list consistent in the store (§11); the registered hotkeys checked after every integration test; hotkeys pressed after every list change |
| R3 | NVIDIA API effects are not observable in CI | fake backend that records every `Apply` and reports what each display shows; hardware checklist |
| R4 | NativeAOT | compiled bindings only; no reflection APIs of the toolkit; AOT publish with warnings as errors (§19) |
| R5 | The window handle exists only after `Opened`; hidden start | hook and registration on `Opened`; headless order tests |
| R6 | Async dialogs and hotkey capture | `HotkeyService.IsMessageDialogOpen` as the single source; focus-loss tests with a dialog open |
| R7 | Dialogs from the tray with the window hidden | `ShowDialogOrStandaloneAsync`; manual test |
| R8 | Accidental saves while loading or importing | ViewModels save only on user actions; tests that loading writes nothing and importing writes once |
| R9 | Rounding and culture | tests with `es-AR` and invariant culture |
| R10 | Intended difference between `ReapplyRealColors` and `SwitchingCore` | `ActiveProfileCoreTests`, `ActiveProfileServiceTests` |
| R11 | Order of the preview discard on `EditingTargetChanging` and `ProfileSwitching` | synchronous events at the reference points; sequence tests |
| R12 | Import with Advanced open | "Remember" reload (8-D2); test |
| R13 | Logic drifting back into MainWindow, Views, the host, the coordinators or the profile ViewModels | architecture tests; closed tables of §7 and §8 |
| R14 | Losing a configuration that could not be loaded, or changes that could not be saved | protected mode, exact copy, notices and close retry (§12); persistence tests with locked and invalid files |
| R15 | Concurrent confirmations, updates or closes | single discard confirmation (10-C6); single installation; close-guard and update tests with held dialogs |
| R16 | The test wiring drifting from the application's | `AppServices.Create` shared by the application and the fixtures; `TestComposition` over `AppHost.Compose` |
| R17 | A display showing an intermediate or stale state (flash of the real colors, preview left behind) | one writer in batches (§22.2); architecture test; `ColorWriteTests` check one write per display with its final value for every flow |
| R18 | Monitor identity: identical monitors merged, settings lost on a new connection, a made-up identity | physical, connection or no identity (§22.1); shared serials recorded; adoption only without doubt; `MonitorIdentityCoreTests`, `DisplayProfileSyncCoreTests` |
| R19 | Written colors becoming saved values | the editor never reads displays; the synchronization reads only displays never written; upsert on save; empty ids dropped (§22.3); tests and mutations |

**Test suites**
- `NvProfileSwitcher.Core.Tests`: the reference Core tests unchanged, the extracted Core, the
  configuration persistence (`ConfigurationPersistenceTests`, `ConfigurationServiceTests`), the
  store, hotkey, editor, preview, switching and update services over `AppServices` with fakes. On
  Linux, 4 tests that depend on Windows paths fail by design; they pass on the Windows CI.
- `NvProfileSwitcher.UiTests` (headless): the layout tests, ViewModels over the composed
  application, interaction, integration over the real composition (`TestComposition`: profile
  editing and behavior, backup, persistence, updates, hotkeys, switching, displays), and
  Windows-only measurement tests (skipped elsewhere).
- **What the tests check is the result:** the colors each display shows, the configuration file,
  the selection and the draft, which hotkeys are registered and which profile each one triggers.
  Call sequences are checked only where the sequence is the behavior (one write per display and
  operation, the import order).
- The fakes keep the rules of what they replace: the hotkey registrar keeps the registrations Win32
  would hold (a combination cannot be registered twice; an ID registered twice keeps both until it
  is released), and every `TestComposition` checks the registered hotkeys when disposed.
- Byte-identical configuration round trips.
- Architecture tests (`ArchitectureTests`): exact code-behind of MainWindow and every View; no
  ViewModel depends on another; the profile ViewModels only delegate; coordinators hold no state;
  the host has no domain dependencies; the profile list is read-only outside the store; only the
  owners change what the hotkeys register; the editor holds only state; the service graph knows no
  presentation; every service uses only `IDialogService` from the Shell; the main window is created
  only by the composition root; only the host subscribes to the main window's events; display
  services depend only on their own concerns; one ViewModel per panel; only `DisplayColorController`
  writes display colors; the editor does not depend on `NvidiaService`. The dependency checks read
  constructor parameters and fields; a dependency hidden in a delegate is reviewed, not tested.
- Targeted mutations were run on every change of phase 10 where a regular test could pass without
  checking anything (a missing hotkey refresh, positional hotkeys, the preview event, the
  reevaluation after Save, the real colors after Cancel, the single confirmation, the clean draft on
  display changes, the cancelled update); every one was detected. Phase 11 added mutations of the
  refresh batch, deferred writes, the switch batch, writing past a failed display, seeding from
  written displays, replacing all values on save, keeping empty ids, showing another display's
  values and the identity retry (missing and unbounded); every one was detected.

Local results at the end of phase 11 (Linux): Core 427/431 (the 4 Windows-path tests), UI 309 passed
and 37 skipped (Windows only), Release build without warnings, linux-x64 AOT publish without
warnings.

**Hardware checklist** (Windows with an NVIDIA GPU): normal and `--minimized` start; switching by
process; overrides by hotkey, also after moving and removing profiles; Global ON/OFF; preview,
Cancel and Save; Add/Remove/Move/Toggle; connecting and disconnecting a monitor while editing, with
the preview on the monitor that stays connected (no flash) and on the monitor disconnected; a monitor
without serial number moved to another port or dock; two identical monitors;
Import and Export from Advanced; manual update, also with unsaved changes; closing with a dirty
draft, and Exit from the tray twice while the confirmation is open; second instance.

---

## 19. NativeAOT

- `StaticLink.Avalonia` 3.119.4-7922.1, aligned with SkiaSharp 3.119.4; `StaticLink.Avalonia.Native`
  is not referenced.
- `<IlcTreatWarningsAsErrors>true</IlcTreatWarningsAsErrors>`: any IL2xxx/IL3xxx warning fails the
  publish.
- `ci.yml` runs on pull requests to `main` and manually: it builds, runs the Core and UI tests,
  validates the NativeAOT publish of the portable win-x64 executable and runs a 15-second smoke
  test of it. It uploads nothing.
- `release.yml` runs only manually; pushing a tag starts nothing. No workflow uploads Actions
  artifacts. Its `publish` input chooses the run:
  - `none` (default): a development build, `NvProfileSwitcher-dev-<sha>` (the latest tag's
    version, "Dev" and the revision in About): build, Core and UI tests, NativeAOT publish,
    smoke test and installer. Nothing is kept after the run.
  - `draft` or `prerelease-draft` with `version` `MAJOR.MINOR.PATCH`, only from `main`: the
    release build of that version. Before building, the run fails when the tag `v<version>` or a
    release or draft for it already exists in this repository, or when the version is not newer
    than every release. It then creates the portable ZIP and SHA-256 files for the ZIP and the
    installer, checks the names, the checksums, the ZIP contents (only
    `NvProfileSwitcher/NvProfileSwitcher.exe`, the smoke-tested executable) and the versions the
    updater relies on (§14.1), and creates a draft release in this repository with those four
    files, through `GITHUB_TOKEN` (`contents: write`). Publications run one at a time.
  - The draft is published by hand. The workflow creates no tag: GitHub creates `v<version>` on
    the commit that was built, and the run summary says whether it already exists. A draft that
    is deleted instead of published may leave that tag, which must then be deleted too.
- Hardware tests use a local build or the files of a draft release; development runs of
  `release.yml` keep nothing to download.
- The JSON configuration uses the source-generated `NvProfileSwitcherJsonContext`; bindings are
  compiled; the toolkit is used only through its source generators.

---

## 20. Open items

- Windows CI and the hardware checklist (§18), with a local build or a draft release (§19), for
  phases 10 and 11; for phase 12, the resume simulation (§22.4) on hardware; for phase 13, the
  Windows CI and a driver update while the application runs.
- The body scrollbar stays pending; it is not touched.
- Possible later improvement, outside the migration: ignore the automatic update check while
  another one runs or the update dialog is open (§16).
- Releases are built and published from this repository with `release.yml` (§19); the source is
  licensed under the GNU GPL v3.0 (`LICENSE`).

---

## 21. Resolution of the integral audit

Every finding of the integral audit of phase 10, with its resolution.

| Finding | Resolution |
|---|---|
| Profile hotkeys depended on list positions; every list change had to unregister first (R2) | Fixed at the cause: hotkeys registered per profile and a list consistent in the store (§11); R2 retired |
| Profile operations spread over two ViewModels, the editor, the shutdown coordinator and the backup service; the editor exposed steps its callers had to chain | `ProfileEditor` (state), `ProfileEditingService` (session), `ProfileListService` (list); ViewModels only present (§5, §6.3) |
| Preview ↔ active profile cycle resolved with a forward reference, and the preview rule written in the composition root | `ProfileSwitching` event; the rule in `ActiveProfileService` (§3) |
| Calls that always went together (begin and apply, invalidate and check, unregister and register, the global toggle in two places) | One method each in its owner (§5.1) |
| Test fixtures and view tests with their own copies of the wiring; a view test driving a window the host was not attached to | `AppServices.Create` shared; view tests over the composed application (§5.2) |
| Tests that confirmed calls instead of results; a hotkey fake that only logged | Result-based tests, a registrar with Win32 rules and a hotkey check after every integration test (§18) |
| Architecture rules easy to bypass (regular expressions, closed type lists) | The list read-only at compile time; rules over every service and over the owners of what the hotkeys register (§18) |
| A second close opened a second discard confirmation | 10-C6 |
| Connecting a monitor made a clean draft count as unsaved | 10-C7 |
| An update left waiting forever when the close was cancelled | 10-C9 |
| Disconnecting a monitor flashed the real colors on the previewed display (found on hardware after the audit) | 10-preview, then 11-1 (§22) |
| Profile icons loaded through Win32 for every draft and copy, and never released | Loaded when a list row shows them (§13.1) |
| `Id`s of profiles without one (C++ configurations) | Not an issue for configurations of this application, which always writes unique `Id`s; C++ configurations are out of scope (§2, §12.1) |
| Coordinators with many dependencies | Kept: they are sequences; the line limit is replaced by the content rules (§8) |
| Explicit sequences that reproduce the reference order | Kept: they are the requirement |
| Persistence, NVIDIA API lifecycle, single instance and foreground hook | Reviewed and kept |
| The unsaved new profile in the list but left out of saves | Kept: the list must show it; taking it out would add more special cases |
| Icons inside the profile model instead of a row ViewModel | Kept: the deferred load removes the cost; a row ViewModel would touch every use of the model |
| Cosmetic: namespaces of the copied Core, duplicated modifier constants, slider ranges in three places, message texts in services, dialogs built in code, `Program.SingleInstance`, the double release query, `ApplicationSettingsViewModel` using the host and the tray | Kept as they are |

---

## 22. Displays and colors

Phase 11 replaced the order-dependent color writes, the string monitor id that could be empty and
the reads of the display into saved values, which together caused the flash of the real colors on a
previewed display during a topology change, double writes, preview values left on a display and
values saved from colors the application had written.

### 22.1 Monitor identity

`MonitorIdentity` (Win32) reads, for each GDI display, the device path of its active display path and
the EDID Windows keeps in the registry; `MonitorIdentityCore` (pure) decides the identity:

| Monitor | Identity |
|---|---|
| EDID with a serial number (descriptor, or numeric other than 0 and 0xFFFFFFFF) that no other connected monitor shares and that was never found shared | physical, `{hardware}-{serial}` (unchanged from the reference) |
| readable EDID without serial number, or with a serial found shared | connection, `{hardware}@{instance}`: the model on the connection Windows identifies it by |
| no device path, or an EDID that could not be read | none: left out of `NvidiaService.Displays` |

- **Shared serials.** Two connected monitors with the same physical identity record it in
  `ProfileStore.NonUniqueMonitorIds`, saved as `"Non-unique Monitor Ids"`. From then on that
  identity always uses the connection identity, also with only one of the monitors connected, so
  identities do not change with the topology. The values saved for it are copied to the connection
  identity of each connected monitor that shared it, and then removed.
- **Adoption.** A connected connection identity without values in any profile adopts the values of
  a saved connection identity of the same model that is not connected, only when, for that model,
  the whole enumeration has exactly one such new connection and one such saved one, and every
  display has an identity. The values are renamed in every profile and in the open draft, whose
  selection follows. Swapping the cables of identical monitors without serial number swaps their
  values: they cannot be told apart.
- **No identity.** A display without identity is never written, edited, synchronized or saved.
  Windows may not report the identity right after a change and sends no further message once it
  can, so the refresh runs once more after `DisplayTopologyService.RetryDelay` (2 s), once per
  topology change.

### 22.2 One color writer

`DisplayColorController` is the only writer of display colors. Its state is the base profile (null
for Windows), chosen by `ActiveProfileService`, and the preview layer (monitor id and values), set by
`LivePreviewController`. What a display shows:

1. while global switching is paused, nothing is written unless the operation forces it (the forced
   Windows restores), and the preview is never shown;
2. the preview values on the previewed display;
3. otherwise the base profile's values for the display; a display without them is not written.

Changes mark the displays they affect: `SetPreview` and `ClearPreview` their display, `SetBase` every
display when the profile changes or every display is asked for (switches, `RefreshAll` after a
topology change, the forced and closing restores). A **batch** defers the writes until the outermost
batch ends, so each marked display is written once, with its final values. Startup, the display
refresh, closing, switches (`Check`), overrides, Save, Remove, Move and the preview discard are
batches; each display fails on its own and the first failure is thrown at the end. A batch that
throws (startup, closing, `Check`) keeps the reference's handling; the others ignore the failure, as
the reference ignored failed applications there, and raise `WriteFailed`, which makes the next check
apply the active profile again. Outside a batch a change is written at once.

`ActiveProfileService.ReapplyRealColors(allDisplays: false)` is used when a preview ends (Discard,
Cancel): only the previewed display is written, unless the real colors are another profile now.

### 22.3 Saved values

- The synchronization reads vibrance and hue, and the NVIDIA Control Panel settings (§22.6), to
  start a monitor's Windows values only when `DisplayColorController.HasWritten` is false for it;
  otherwise the NVIDIA defaults are used.
- The editor never reads a display: the values of the selected display come from the draft, the
  saved profile, the Windows profile or the NVIDIA defaults, in that order, never from another
  display.
- Saving a draft (`ProfileEditingCore.CopyProfile`) replaces the profile's values for the monitors
  the draft has and keeps the others.
- Values without a monitor id are dropped on load, so they are never written again.

### 22.4 Recovery after resume

After sleep or hibernation, the NVIDIA API session, its display handles and the colors the driver
shows may no longer be valid. Windows sends `WM_POWERBROADCAST` with `PBT_APMRESUMESUSPEND` and/or
`PBT_APMRESUMEAUTOMATIC`, and usually `WM_DISPLAYCHANGE` too.

- `DisplayTopologyService` handles the two resume notifications like a display change: they restart
  the same 500 ms debounce and mark it as a resume, so all the messages of one resume end in a
  single `Resumed` instead of `TopologyChanged`. Other power notifications are ignored.
- `DisplayRefreshCoordinator.Recover` initializes the NVIDIA API again with the ordered replacement
  of `Opened` (a new instance runs `NvAPI_Initialize` before the previous one is released), then, in
  one batch, runs the display refresh (new enumeration and handles, synchronization, editor, preview
  and active profile) and, while global switching is paused, restores Windows forcibly. Each display
  is written once, with the active profile, the preview on its display or Windows. A failed
  initialization reports the API as unavailable and keeps the current instance, as on opening.
- No logs, no persistence and no retries other than the identity retry of §22.1.
- **Simulation:** `tools/Simulate-Resume.ps1` posts the same resume notifications to every top-level
  window of the running application, visible or hidden in the tray, so it runs the same recovery
  without suspending Windows. Changing a display's colors from outside first (for example, digital
  vibrance in the NVIDIA Control Panel) shows the recovery restoring them.

### 22.5 Installed driver version

The NVIDIA API library (`nvapi64.dll`) a process loads stays loaded until it ends: initializing the
API again (on `Opened` or after resume) gets the same library from Windows, which keeps reporting the
driver it came with after another one is installed. The status bar therefore shows the driver
installed in Windows:

- `InstalledDriverVersion.Read` (Platform) reads the file version of `nvapi64.dll` in the system
  folder without loading it, and `DriverVersionCore.FromFileVersion` converts it to the NVIDIA form:
  the last digit of the third part and the four digits of the fourth (32.0.16.1742 is 617.42).
- `NvidiaService` uses it when the API is initialized and in `RefreshDriverVersion`, which runs every
  time the window is activated and on every display topology change (`WM_DISPLAYCHANGE`, §8):
  installing a driver restarts the display adapter, which changes the display mode, so the new
  version is read then even while the window is not active. The version the API reports is the
  fallback when the file cannot be read. Without an initialized API nothing is read, and "Unknown"
  never replaces a known version.
- There is no polling. The session of the API itself stays the one loaded at start until the
  application is restarted.

### 22.6 Initial values from the NVIDIA Control Panel

The NVIDIA Control Panel keeps the brightness, contrast and gamma it sets for each display in
`HKCU\Software\NVIDIA Corporation\Global\NVTweak\Devices\{device id}-0\Color`: nine DWORD
values, 3538946 to 3538954, brightness, contrast and gamma × 100 for red, green and blue, on the
same scale as this application. The NVIDIA API does not report them.

- **Only for new monitors.** `DisplayProfileSyncCore.Synchronize` uses them for the Windows values
  of a monitor that has none, as it reads vibrance and hue, and only when
  `DisplayColorController.HasWritten` is false for it. Once the values are saved, this application
  owns them: a monitor with Windows values, an adopted one or one known in the session is never read
  again, and nothing follows later changes in the NVIDIA Control Panel. Application profiles keep
  their values; a monitor new to one takes its Windows values, as before.
- **Device id.** `NvApi.GetNvTweakDeviceId` calls `NvAPI_SYS_GetLUIDFromDisplayID` for the display;
  the device id is the second 32-bit word of the LUID without its top four bits
  (`NvTweakColorCore.DeviceIdFromLuid`).
- **Read only.** `NvTweakColorRegistry.Read` opens the key without write access and returns the
  kind and data of each value. `NvTweakColorCore.Parse` (pure) uses them only when all nine exist as
  DWORDs, each attribute has the same value for the three channels, brightness and contrast are
  80–120 and gamma 30–280. Nothing is averaged or taken in part: a missing device id, key or value,
  another type, different channels or a value out of range leave the neutral 100, 100 and 1.00.
- There is no polling and nothing is written to the registry. The reader is passed through
  `AppPlatform.ReadNvTweakColors`, so tests use a fake registry.

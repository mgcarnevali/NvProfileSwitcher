namespace GameProfileSwitcher;

public sealed class MainForm : Form
{
    private readonly AppSettings _settings;
    private readonly ColorController _colors = new();
    private readonly ProcessWatcher _watcher;
    private readonly NotifyIcon _tray;

    private readonly ListBox _profiles = new();
    private readonly TextBox _name = new();
    private readonly TextBox _exe = new();
    private readonly NumericUpDown _vibrance = new();
    private readonly NumericUpDown _brightness = new();
    private readonly NumericUpDown _contrast = new();
    private readonly NumericUpDown _gamma = new();
    private readonly CheckBox _enabled = new() { Text = "Enabled" };
    private readonly CheckBox _startWindows = new() { Text = "Start with Windows" };
    private readonly Label _status = new() { AutoSize = true };
    private readonly Label _active = new() { AutoSize = true };

    private bool _reallyExit;
    private bool _loading;

    public MainForm(bool startMinimized)
    {
        _settings = ProfileStore.Load();
        _watcher = new ProcessWatcher(() => _settings.Profiles);
        _watcher.ProfileChanged += OnProfileChanged;

        Text = "Game Profile Switcher v0.1";
        Width = 820;
        Height = 560;
        MinimumSize = new Size(760, 500);
        StartPosition = FormStartPosition.CenterScreen;

        BuildUi();

        _tray = new NotifyIcon
        {
            Text = "Game Profile Switcher",
            Icon = SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = BuildTrayMenu()
        };
        _tray.DoubleClick += (_, _) => ShowFromTray();

        var ok = _colors.Initialize();
        _status.Text = _colors.Status;
        if (ok)
        {
            try { _colors.Apply(_settings.DesktopProfile); }
            catch (Exception ex) { _status.Text = ex.Message; }
        }

        RefreshList();
        _startWindows.Checked = _settings.StartWithWindows;
        _watcher.Start();

        if (startMinimized || _settings.StartMinimized)
        {
            Shown += (_, _) => Hide();
        }
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(12)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66));
        Controls.Add(root);

        var left = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3 };
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(left, 0, 0);

        _profiles.Dock = DockStyle.Fill;
        _profiles.SelectedIndexChanged += (_, _) => LoadSelected();
        left.Controls.Add(_profiles, 0, 0);

        var leftButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        var add = new Button { Text = "Add game", AutoSize = true };
        var remove = new Button { Text = "Remove", AutoSize = true };
        add.Click += (_, _) => AddProfile();
        remove.Click += (_, _) => RemoveProfile();
        leftButtons.Controls.AddRange(new Control[] { add, remove });
        left.Controls.Add(leftButtons, 0, 1);

        _startWindows.CheckedChanged += (_, _) =>
        {
            if (_loading) return;
            _settings.StartWithWindows = _startWindows.Checked;
            StartupManager.SetEnabled(_startWindows.Checked);
            Save();
        };
        left.Controls.Add(_startWindows, 0, 2);

        var right = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 10,
            Padding = new Padding(18, 0, 0, 0)
        };
        right.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        right.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        root.Controls.Add(right, 1, 0);

        AddRow(right, 0, "Profile name", _name);
        AddRow(right, 1, "Game EXE", _exe, MakeBrowseButton());

        SetupNumeric(_vibrance, 0, 100, 1, 50, 0);
        SetupNumeric(_brightness, 0, 1, 0.01m, 0.50m, 2);
        SetupNumeric(_contrast, 0, 1, 0.01m, 0.50m, 2);
        SetupNumeric(_gamma, 0.50m, 3.00m, 0.05m, 1.00m, 2);

        AddRow(right, 2, "Digital Vibrance", _vibrance);
        AddRow(right, 3, "Brightness", _brightness);
        AddRow(right, 4, "Contrast", _contrast);
        AddRow(right, 5, "Gamma", _gamma);
        right.Controls.Add(_enabled, 1, 6);

        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        var save = new Button { Text = "Save profile", AutoSize = true };
        var test = new Button { Text = "Apply now", AutoSize = true };
        var normal = new Button { Text = "Restore Desktop", AutoSize = true };
        save.Click += (_, _) => SaveSelected();
        test.Click += (_, _) => ApplySelected();
        normal.Click += (_, _) => ApplyDesktop();
        buttons.Controls.AddRange(new Control[] { save, test, normal });
        right.Controls.Add(buttons, 1, 7);

        var note = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(470, 0),
            Text = "v0.1 targets the Windows/NVIDIA primary display. Digital Vibrance uses NVAPI; Brightness/Contrast/Gamma use the Windows hardware gamma ramp."
        };
        right.Controls.Add(note, 1, 8);

        var statusPanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, Dock = DockStyle.Fill };
        statusPanel.Controls.Add(_active);
        statusPanel.Controls.Add(_status);
        right.Controls.Add(statusPanel, 1, 9);
    }

    private static void AddRow(TableLayoutPanel panel, int row, string label, Control control, Control? extra = null)
    {
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        control.Dock = DockStyle.Fill;
        panel.Controls.Add(control, 1, row);
        if (extra is not null) panel.Controls.Add(extra, 2, row);
    }

    private static void SetupNumeric(NumericUpDown n, decimal min, decimal max, decimal increment, decimal value, int decimals)
    {
        n.Minimum = min;
        n.Maximum = max;
        n.Increment = increment;
        n.Value = value;
        n.DecimalPlaces = decimals;
    }

    private Button MakeBrowseButton()
    {
        var button = new Button { Text = "...", Width = 38 };
        button.Click += (_, _) =>
        {
            using var dlg = new OpenFileDialog { Filter = "Executables (*.exe)|*.exe|All files (*.*)|*.*" };
            if (dlg.ShowDialog(this) == DialogResult.OK)
                _exe.Text = dlg.FileName;
        };
        return button;
    }

    private ContextMenuStrip BuildTrayMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => ShowFromTray());
        menu.Items.Add("Restore Desktop", null, (_, _) => ApplyDesktop());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) =>
        {
            _reallyExit = true;
            Close();
        });
        return menu;
    }

    private void RefreshList(GameProfile? select = null)
    {
        _profiles.BeginUpdate();
        _profiles.Items.Clear();
        foreach (var p in _settings.Profiles) _profiles.Items.Add(p);
        _profiles.EndUpdate();

        if (select is not null)
            _profiles.SelectedItem = select;
        else if (_profiles.Items.Count > 0)
            _profiles.SelectedIndex = 0;
    }

    private GameProfile? Selected => _profiles.SelectedItem as GameProfile;

    private void LoadSelected()
    {
        if (Selected is not { } p) return;
        _loading = true;
        _name.Text = p.Name;
        _exe.Text = p.ExePath;
        _vibrance.Value = Math.Clamp(p.DigitalVibrance, 0, 100);
        _brightness.Value = (decimal)Math.Clamp(p.Brightness, 0, 1);
        _contrast.Value = (decimal)Math.Clamp(p.Contrast, 0, 1);
        _gamma.Value = (decimal)Math.Clamp(p.Gamma, 0.5, 3);
        _enabled.Checked = p.Enabled;
        _loading = false;
    }

    private void AddProfile()
    {
        var p = new GameProfile { Name = "New Game", DigitalVibrance = 50, Brightness = .5, Contrast = .5, Gamma = 1.0 };
        _settings.Profiles.Add(p);
        Save();
        RefreshList(p);
    }

    private void RemoveProfile()
    {
        if (Selected is not { } p) return;
        _settings.Profiles.Remove(p);
        Save();
        RefreshList();
    }

    private void SaveSelected()
    {
        if (Selected is not { } p) return;
        p.Name = string.IsNullOrWhiteSpace(_name.Text) ? "Unnamed" : _name.Text.Trim();
        p.ExePath = _exe.Text.Trim();
        p.DigitalVibrance = (int)_vibrance.Value;
        p.Brightness = (double)_brightness.Value;
        p.Contrast = (double)_contrast.Value;
        p.Gamma = (double)_gamma.Value;
        p.Enabled = _enabled.Checked;
        Save();
        RefreshList(p);
        _status.Text = "Saved";
    }

    private void ApplySelected()
    {
        SaveSelected();
        if (Selected is not { } p) return;
        TryApply(p);
    }

    private void ApplyDesktop() => TryApply(_settings.DesktopProfile);

    private void OnProfileChanged(GameProfile? profile)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => OnProfileChanged(profile));
            return;
        }

        if (profile is null)
        {
            _active.Text = "Active profile: Desktop / Normal";
            TryApply(_settings.DesktopProfile);
        }
        else
        {
            _active.Text = $"Active profile: {profile.Name} ({profile.ProcessName}.exe)";
            TryApply(profile);
        }
    }

    private void TryApply(GameProfile p)
    {
        try
        {
            _colors.Apply(p);
            _status.Text = _colors.Status;
        }
        catch (Exception ex)
        {
            _status.Text = "Apply failed: " + ex.Message;
        }
    }

    private void Save() => ProfileStore.Save(_settings);

    private void ShowFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_reallyExit && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        try { ApplyDesktop(); } catch { }
        _watcher.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _colors.Dispose();
        base.OnFormClosing(e);
    }
}

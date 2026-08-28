using System.Drawing.Drawing2D;

namespace GameProfileSwitcher;

public sealed class MainForm : Form
{
    private readonly AppSettings _settings;
    private readonly ColorController _colors = new();
    private readonly ProcessWatcher _watcher;
    private readonly NotifyIcon _tray;
    private readonly Icon? _appIcon;
    private readonly Dictionary<string, Icon?> _gameIcons = new(StringComparer.OrdinalIgnoreCase);

    private readonly ListBox _profiles = new();
    private readonly TextBox _name = new();
    private readonly TextBox _exe = new();
    private readonly CheckBox _enabled = new() { Text = "Enable automatic profile" };
    private readonly CheckBox _startWindows = new() { Text = "Start with Windows" };
    private readonly CheckBox _startMinimized = new() { Text = "Start minimized to tray" };

    private readonly TrackBar _vibrance = new();
    private readonly TrackBar _brightness = new();
    private readonly TrackBar _contrast = new();
    private readonly TrackBar _gamma = new();
    private readonly Label _vibranceValue = new();
    private readonly Label _brightnessValue = new();
    private readonly Label _contrastValue = new();
    private readonly Label _gammaValue = new();

    private readonly Label _status = new();
    private readonly Label _active = new();
    private readonly Panel _statusDot = new();

    private bool _reallyExit;
    private bool _loading;
    private bool _automaticSwitchingPaused;

    private static readonly Color Back = Color.FromArgb(22, 24, 28);
    private static readonly Color PanelBack = Color.FromArgb(30, 33, 38);
    private static readonly Color PanelBack2 = Color.FromArgb(36, 40, 46);
    private static readonly Color Border = Color.FromArgb(55, 60, 68);
    private static readonly Color TextMain = Color.FromArgb(235, 238, 242);
    private static readonly Color TextMuted = Color.FromArgb(155, 163, 174);
    private static readonly Color Accent = Color.FromArgb(70, 190, 120);
    private static readonly Color AccentHover = Color.FromArgb(82, 207, 135);
    private static readonly Color Danger = Color.FromArgb(220, 84, 84);

    public MainForm(bool startMinimized)
    {
        _settings = ProfileStore.Load();
        _watcher = new ProcessWatcher(() => _settings.Profiles);
        _watcher.ProfileChanged += OnProfileChanged;

        Text = "Game Profile Switcher v0.2.4";
        Width = 900;
        Height = 775;
        MinimumSize = new Size(860, 750);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Back;
        ForeColor = TextMain;
        Font = new Font("Segoe UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
        DoubleBuffered = true;

        _appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        if (_appIcon is not null)
            Icon = _appIcon;

        BuildUi();

        _tray = new NotifyIcon
        {
            Text = "Game Profile Switcher",
            Icon = _appIcon ?? SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = BuildTrayMenu()
        };
        _tray.DoubleClick += (_, _) => ShowFromTray();

        var ok = _colors.Initialize();
        SetStatus(ok ? _colors.Status : _colors.Status, ok);
        if (ok)
        {
            try
            {
                _colors.Apply(_settings.DesktopProfile);
                SetStatus(_colors.Status, true);
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, false);
            }
        }

        RefreshList();
        _loading = true;
        _startWindows.Checked = _settings.StartWithWindows;
        _startMinimized.Checked = _settings.StartMinimized;
        _loading = false;

        _active.Text = "Active profile: Desktop / Normal";
        _watcher.Start();

        if (startMinimized || _settings.StartMinimized)
            Shown += (_, _) => Hide();
    }

    private void BuildUi()
    {
        SuspendLayout();

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(18, 12, 18, 14),
            BackColor = Back
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildMainArea(), 0, 1);
        root.Controls.Add(BuildFooter(), 0, 2);

        ResumeLayout(true);
    }

    private Control BuildHeader()
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            Padding = new Padding(2, 0, 2, 9),
            BackColor = Back
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var titleWrap = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };

        var title = new Label
        {
            Text = "Game Profile Switcher",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 16f, FontStyle.Bold),
            ForeColor = TextMain,
            Margin = Padding.Empty
        };
        var subtitle = new Label
        {
            Text = "Automatic display color profiles for your games",
            AutoSize = true,
            ForeColor = TextMuted,
            Margin = new Padding(1, 2, 0, 0)
        };
        titleWrap.Controls.Add(title);
        titleWrap.Controls.Add(subtitle);
        header.Controls.Add(titleWrap, 0, 0);

        var version = new Label
        {
            Text = "v0.2.3",
            AutoSize = true,
            ForeColor = Accent,
            BackColor = PanelBack2,
            Padding = new Padding(10, 5, 10, 5),
            Margin = new Padding(0, 4, 0, 0)
        };
        header.Controls.Add(version, 1, 0);

        return header;
    }

    private Control BuildMainArea()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            BackColor = Back
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 255));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        root.Controls.Add(BuildSidebar(), 0, 0);
        root.Controls.Add(BuildEditor(), 1, 0);
        return root;
    }

    private Control BuildSidebar()
    {
        var card = NewCard(new Padding(12));
        card.Margin = new Padding(0, 0, 12, 0);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = PanelBack,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        card.Controls.Add(layout);

        layout.Controls.Add(SectionTitle("GAME PROFILES"), 0, 0);

        _profiles.Dock = DockStyle.Fill;
        _profiles.BorderStyle = BorderStyle.None;
        _profiles.BackColor = PanelBack;
        _profiles.ForeColor = TextMain;
        _profiles.Font = new Font("Segoe UI", 10f);
        _profiles.IntegralHeight = false;
        _profiles.ItemHeight = 46;
        _profiles.DrawMode = DrawMode.OwnerDrawFixed;
        _profiles.Margin = new Padding(0, 8, 0, 8);
        _profiles.SelectedIndexChanged += (_, _) => LoadSelected();
        _profiles.DrawItem += DrawProfileItem;
        layout.Controls.Add(_profiles, 0, 1);

        var buttons = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Margin = new Padding(0, 0, 0, 8)
        };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        var add = NewButton("+  Add game", primary: true);
        var remove = NewButton("Remove", primary: false);
        remove.ForeColor = Color.FromArgb(235, 180, 180);
        add.Click += (_, _) => AddProfile();
        remove.Click += (_, _) => RemoveProfile();
        buttons.Controls.Add(add, 0, 0);
        buttons.Controls.Add(remove, 1, 0);
        layout.Controls.Add(buttons, 0, 2);

        var desktop = NewButton("Restore Desktop", primary: false);
        desktop.Dock = DockStyle.Fill;
        desktop.Click += (_, _) => ApplyDesktop();
        layout.Controls.Add(desktop, 0, 3);

        return card;
    }

    private Control BuildEditor()
    {
        var card = NewCard(new Padding(18));
        card.Margin = new Padding(0);

        var editor = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 8,
            BackColor = PanelBack,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        editor.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        editor.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        editor.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        editor.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        editor.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        editor.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        editor.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        editor.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        card.Controls.Add(editor);

        var top = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            Margin = Padding.Empty
        };
        top.Controls.Add(SectionTitle("PROFILE SETTINGS"), 0, 0);
        editor.Controls.Add(top, 0, 0);

        editor.Controls.Add(BuildTextField("Profile name", _name), 0, 1);
        editor.Controls.Add(BuildExeField(), 0, 2);

        _enabled.AutoSize = true;
        _enabled.ForeColor = TextMain;
        _enabled.BackColor = PanelBack;
        _enabled.Margin = new Padding(2, 4, 0, 6);
        editor.Controls.Add(_enabled, 0, 3);

        var sliders = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            Margin = new Padding(0, 2, 0, 0),
            Padding = Padding.Empty
        };

        ConfigureSlider(_vibrance, 0, 100, 50, 1);
        ConfigureSlider(_brightness, 0, 100, 50, 1);
        ConfigureSlider(_contrast, 0, 100, 50, 1);
        ConfigureSlider(_gamma, 50, 300, 100, 5);

        _vibrance.Scroll += (_, _) => UpdateSliderLabels();
        _brightness.Scroll += (_, _) => UpdateSliderLabels();
        _contrast.Scroll += (_, _) => UpdateSliderLabels();
        _gamma.Scroll += (_, _) => UpdateSliderLabels();

        sliders.Controls.Add(BuildSliderRow("Digital Vibrance", _vibrance, _vibranceValue), 0, 0);
        sliders.Controls.Add(BuildSliderRow("Brightness", _brightness, _brightnessValue), 0, 1);
        sliders.Controls.Add(BuildSliderRow("Contrast", _contrast, _contrastValue), 0, 2);
        sliders.Controls.Add(BuildSliderRow("Gamma", _gamma, _gammaValue), 0, 3);
        editor.Controls.Add(sliders, 0, 4);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 8, 0, 6)
        };
        var save = NewButton("Save profile", primary: true);
        var apply = NewButton("Apply now", primary: false);
        save.Click += (_, _) => SaveSelected();
        apply.Click += (_, _) => ApplySelected();
        actions.Controls.Add(save);
        actions.Controls.Add(apply);
        editor.Controls.Add(actions, 0, 5);

        var divider = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Border, Margin = new Padding(0, 4, 0, 10) };
        editor.Controls.Add(divider, 0, 6);

        var startup = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Margin = Padding.Empty
        };
        StyleCheckBox(_startWindows);
        StyleCheckBox(_startMinimized);

        _startWindows.CheckedChanged += (_, _) =>
        {
            if (_loading) return;
            _settings.StartWithWindows = _startWindows.Checked;
            StartupManager.SetEnabled(_startWindows.Checked);
            Save();
        };
        _startMinimized.CheckedChanged += (_, _) =>
        {
            if (_loading) return;
            _settings.StartMinimized = _startMinimized.Checked;
            Save();
        };

        startup.Controls.Add(_startWindows);
        startup.Controls.Add(_startMinimized);
        editor.Controls.Add(startup, 0, 7);

        UpdateSliderLabels();
        return card;
    }

    private Control BuildFooter()
    {
        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 2,
            Margin = new Padding(0, 10, 0, 0),
            Padding = new Padding(2, 0, 10, 0),
            BackColor = Back
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));

        var activeWrap = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty
        };
        _statusDot.Size = new Size(9, 9);
        _statusDot.BackColor = Accent;
        _statusDot.Margin = new Padding(0, 6, 8, 0);
        _active.AutoSize = true;
        _active.ForeColor = TextMain;
        _active.Margin = Padding.Empty;
        activeWrap.Controls.Add(_statusDot);
        activeWrap.Controls.Add(_active);

        _status.AutoSize = true;
        _status.ForeColor = TextMuted;
        _status.TextAlign = ContentAlignment.MiddleRight;
        _status.Dock = DockStyle.Fill;

        footer.Controls.Add(activeWrap, 0, 0);
        footer.Controls.Add(_status, 1, 0);
        return footer;
    }

    private Control BuildTextField(string caption, TextBox box)
    {
        var wrap = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            Margin = new Padding(0, 6, 0, 3)
        };
        wrap.Controls.Add(FieldLabel(caption), 0, 0);
        StyleTextBox(box);
        box.Dock = DockStyle.Top;
        wrap.Controls.Add(box, 0, 1);
        return wrap;
    }

    private Control BuildExeField()
    {
        var wrap = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            Margin = new Padding(0, 4, 0, 3)
        };
        wrap.Controls.Add(FieldLabel("Game executable"), 0, 0);

        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            Margin = Padding.Empty
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        StyleTextBox(_exe);
        _exe.Dock = DockStyle.Fill;
        row.Controls.Add(_exe, 0, 0);
        var browse = NewButton("Browse...", primary: false);
        browse.Margin = new Padding(8, 0, 0, 0);
        browse.Click += (_, _) =>
        {
            using var dlg = new OpenFileDialog
            {
                Filter = "Executables (*.exe)|*.exe|All files (*.*)|*.*",
                Title = "Select game executable"
            };
            if (dlg.ShowDialog(this) == DialogResult.OK)
                _exe.Text = dlg.FileName;
        };
        row.Controls.Add(browse, 1, 0);
        wrap.Controls.Add(row, 0, 1);
        return wrap;
    }

    private Control BuildSliderRow(string caption, TrackBar slider, Label valueLabel)
    {
        var wrap = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 2,
            Margin = new Padding(0, 2, 0, 2)
        };
        wrap.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        wrap.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));

        var label = FieldLabel(caption);
        label.Margin = new Padding(0, 0, 0, 0);
        wrap.Controls.Add(label, 0, 0);

        valueLabel.AutoSize = false;
        valueLabel.Width = 58;
        valueLabel.Height = 24;
        valueLabel.TextAlign = ContentAlignment.MiddleCenter;
        valueLabel.ForeColor = Accent;
        valueLabel.BackColor = PanelBack2;
        valueLabel.Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold);
        valueLabel.Margin = new Padding(6, 0, 0, 0);
        wrap.Controls.Add(valueLabel, 1, 0);

        slider.Dock = DockStyle.Fill;
        slider.AutoSize = false;
        slider.Margin = new Padding(0, 0, 6, 0);
        slider.BackColor = PanelBack;
        slider.TickStyle = TickStyle.None;
        slider.Height = 26;
        wrap.Controls.Add(slider, 0, 1);
        wrap.SetColumnSpan(slider, 2);

        return wrap;
    }

    private static void ConfigureSlider(TrackBar slider, int min, int max, int value, int smallChange)
    {
        slider.Minimum = min;
        slider.Maximum = max;
        slider.Value = Math.Clamp(value, min, max);
        slider.SmallChange = smallChange;
        slider.LargeChange = Math.Max(smallChange, (max - min) / 10);
    }

    private void UpdateSliderLabels()
    {
        _vibranceValue.Text = $"{_vibrance.Value}%";
        _brightnessValue.Text = $"{_brightness.Value}%";
        _contrastValue.Text = $"{_contrast.Value}%";
        _gammaValue.Text = (_gamma.Value / 100.0).ToString("0.00");
    }

    private void DrawProfileItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _profiles.Items.Count) return;
        var p = (GameProfile)_profiles.Items[e.Index];
        bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;

        var bg = selected ? PanelBack2 : PanelBack;
        using var bgBrush = new SolidBrush(bg);
        e.Graphics.FillRectangle(bgBrush, e.Bounds);

        if (selected)
        {
            using var accentBrush = new SolidBrush(Accent);
            e.Graphics.FillRectangle(accentBrush, new Rectangle(e.Bounds.Left, e.Bounds.Top + 5, 3, e.Bounds.Height - 10));
        }

        var iconRect = new Rectangle(e.Bounds.Left + 12, e.Bounds.Top + 8, 30, 30);
        var gameIcon = GetGameIcon(p.ExePath);
        if (gameIcon is not null)
        {
            e.Graphics.DrawIcon(gameIcon, iconRect);
        }
        else
        {
            using var circle = new SolidBrush(selected ? Accent : Color.FromArgb(72, 78, 88));
            e.Graphics.FillEllipse(circle, iconRect);

            using var fallbackFont = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold);
            string initial = string.IsNullOrWhiteSpace(p.Name) ? "?" : p.Name.Trim()[0].ToString().ToUpperInvariant();
            var initialSize = e.Graphics.MeasureString(initial, fallbackFont);
            using var initialBrush = new SolidBrush(Color.White);
            e.Graphics.DrawString(initial, fallbackFont, initialBrush,
                iconRect.Left + (iconRect.Width - initialSize.Width) / 2,
                iconRect.Top + (iconRect.Height - initialSize.Height) / 2 - 1);
        }

        using var gameFont = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold);
        using var exeFont = new Font("Segoe UI", 8f, FontStyle.Regular);
        using var gameBrush = new SolidBrush(TextMain);
        using var exeBrush = new SolidBrush(TextMuted);

        e.Graphics.DrawString(p.Name, gameFont, gameBrush, e.Bounds.Left + 50, e.Bounds.Top + 7);
        var exeName = string.IsNullOrWhiteSpace(p.ProcessName) ? "No executable selected" : p.ProcessName + ".exe";
        e.Graphics.DrawString(exeName, exeFont, exeBrush, e.Bounds.Left + 50, e.Bounds.Top + 25);

        e.DrawFocusRectangle();
    }


    private Icon? GetGameIcon(string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            return null;

        if (_gameIcons.TryGetValue(exePath, out var cached))
            return cached;

        try
        {
            var extracted = Icon.ExtractAssociatedIcon(exePath);
            _gameIcons[exePath] = extracted;
            return extracted;
        }
        catch
        {
            _gameIcons[exePath] = null;
            return null;
        }
    }

    private void ClearGameIconCache()
    {
        foreach (var icon in _gameIcons.Values)
            icon?.Dispose();
        _gameIcons.Clear();
        _profiles.Invalidate();
    }

    private static Panel NewCard(Padding padding)
    {
        return new Panel
        {
            Dock = DockStyle.Fill,
            Padding = padding,
            BackColor = PanelBack
        };
    }

    private static Label SectionTitle(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = TextMuted,
        Font = new Font("Segoe UI Semibold", 8.5f, FontStyle.Bold),
        Margin = new Padding(0, 0, 0, 2)
    };

    private static Label FieldLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = TextMuted,
        Margin = new Padding(0, 0, 0, 4)
    };

    private static void StyleTextBox(TextBox box)
    {
        box.BorderStyle = BorderStyle.FixedSingle;
        box.BackColor = PanelBack2;
        box.ForeColor = TextMain;
        box.Font = new Font("Segoe UI", 9.5f);
        box.Margin = Padding.Empty;
    }

    private static void StyleCheckBox(CheckBox box)
    {
        box.AutoSize = true;
        box.ForeColor = TextMain;
        box.BackColor = PanelBack;
        box.Margin = new Padding(0, 0, 18, 0);
    }

    private static Button NewButton(string text, bool primary)
    {
        var b = new Button
        {
            Text = text,
            AutoSize = true,
            Height = 34,
            Padding = new Padding(12, 4, 12, 4),
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Accent : PanelBack2,
            ForeColor = primary ? Color.FromArgb(15, 22, 17) : TextMain,
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 0, 8, 0),
            Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold),
            UseVisualStyleBackColor = false
        };
        b.FlatAppearance.BorderSize = 1;
        b.FlatAppearance.BorderColor = primary ? Accent : Border;
        b.FlatAppearance.MouseOverBackColor = primary ? AccentHover : Color.FromArgb(45, 50, 57);
        b.FlatAppearance.MouseDownBackColor = primary ? Color.FromArgb(58, 170, 105) : Color.FromArgb(52, 57, 65);
        return b;
    }

    private ContextMenuStrip BuildTrayMenu()
    {
        var menu = new ContextMenuStrip
        {
            BackColor = PanelBack,
            ForeColor = TextMain,
            Renderer = new ToolStripProfessionalRenderer(new DarkMenuColors())
        };
        menu.Items.Add("Open Game Profile Switcher", null, (_, _) => ShowFromTray());
        menu.Items.Add("Restore Desktop", null, (_, _) => ApplyDesktop());

        var pauseItem = new ToolStripMenuItem("Pause automatic switching");
        pauseItem.Click += (_, _) =>
        {
            _automaticSwitchingPaused = !_automaticSwitchingPaused;

            if (_automaticSwitchingPaused)
            {
                _watcher.Stop();
                ApplyDesktop();
                _active.Text = "Active profile: Desktop / Normal (Paused)";
                pauseItem.Text = "Resume automatic switching";
            }
            else
            {
                _watcher.Reset();
                _watcher.Start();
                _active.Text = "Active profile: Desktop / Normal";
                pauseItem.Text = "Pause automatic switching";
            }
        };
        menu.Items.Add(pauseItem);

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
        foreach (var p in _settings.Profiles)
            _profiles.Items.Add(p);
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
        _brightness.Value = Math.Clamp((int)Math.Round(p.Brightness * 100), 0, 100);
        _contrast.Value = Math.Clamp((int)Math.Round(p.Contrast * 100), 0, 100);
        _gamma.Value = Math.Clamp((int)Math.Round(p.Gamma * 100), 50, 300);
        _enabled.Checked = p.Enabled;
        UpdateSliderLabels();
        _loading = false;
    }

    private void AddProfile()
    {
        var p = new GameProfile
        {
            Name = "New Game",
            DigitalVibrance = 50,
            Brightness = .5,
            Contrast = .5,
            Gamma = 1.0
        };
        _settings.Profiles.Add(p);
        Save();
        RefreshList(p);
    }

    private void RemoveProfile()
    {
        if (Selected is not { } p) return;
        var result = MessageBox.Show(
            this,
            $"Remove '{p.Name}'?",
            "Game Profile Switcher",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (result != DialogResult.Yes) return;

        _settings.Profiles.Remove(p);
        Save();
        RefreshList();
    }

    private void SaveSelected()
    {
        if (Selected is not { } p) return;

        p.Name = string.IsNullOrWhiteSpace(_name.Text) ? "Unnamed" : _name.Text.Trim();
        p.ExePath = _exe.Text.Trim();
        p.DigitalVibrance = _vibrance.Value;
        p.Brightness = _brightness.Value / 100.0;
        p.Contrast = _contrast.Value / 100.0;
        p.Gamma = _gamma.Value / 100.0;
        p.Enabled = _enabled.Checked;

        Save();
        ClearGameIconCache();
        RefreshList(p);
        SetStatus("Profile saved", true);
    }

    private void ApplySelected()
    {
        SaveSelected();
        if (Selected is not { } p) return;
        TryApply(p);
    }

    private void ApplyDesktop()
    {
        _active.Text = "Active profile: Desktop / Normal";
        TryApply(_settings.DesktopProfile);
    }

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
            _active.Text = $"Active profile: {profile.Name}";
            TryApply(profile);
        }
    }

    private void TryApply(GameProfile p)
    {
        try
        {
            _colors.Apply(p);
            SetStatus(_colors.Status, true);
        }
        catch (Exception ex)
        {
            SetStatus("Apply failed: " + ex.Message, false);
        }
    }

    private void SetStatus(string message, bool ok)
    {
        _status.Text = message;
        _status.ForeColor = ok ? TextMuted : Color.FromArgb(235, 145, 145);
        _statusDot.BackColor = ok ? Accent : Danger;
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
        ClearGameIconCache();
        _appIcon?.Dispose();
        _colors.Dispose();
        base.OnFormClosing(e);
    }

    private sealed class DarkMenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => PanelBack;
        public override Color ImageMarginGradientBegin => PanelBack;
        public override Color ImageMarginGradientMiddle => PanelBack;
        public override Color ImageMarginGradientEnd => PanelBack;
        public override Color MenuItemSelected => PanelBack2;
        public override Color MenuItemBorder => Border;
        public override Color SeparatorDark => Border;
        public override Color SeparatorLight => Border;
    }
}

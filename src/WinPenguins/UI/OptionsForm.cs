using System.Diagnostics;
using System.Drawing;
using WinPenguins.Assets;
using WinPenguins.Config;
using WinPenguins.Platform;

namespace WinPenguins.UI;

public sealed class OptionsForm : Form
{
    private readonly AppSettings _original;
    private readonly Action<AppSettings> _applyCallback;
    private readonly SoundPlayerService _soundPlayer;

    // Tab 1: Penguins & Sound
    private readonly TrackBar _countSlider;
    private readonly Label _countValueLabel;
    private readonly TrackBar _speedSlider;
    private readonly Label _speedValueLabel;
    private readonly TrackBar _alphaSlider;
    private readonly Label _alphaValueLabel;
    private readonly TrackBar _santaSlider;
    private readonly Label _santaValueLabel;
    private readonly CheckBox _idlePausesCheck;
    private readonly CheckBox _parachutesCheck;
    private readonly CheckBox _neverSplatCheck;
    private readonly TrackBar _splatSlider;
    private readonly Label _splatValueLabel;

    private readonly CheckBox _soundEnabledCheck;
    private readonly CheckBox _actionSoundsCheck;
    private readonly TrackBar _volumeSlider;
    private readonly Label _volumeValueLabel;
    private readonly ComboBox _builtInSoundCombo;
    private readonly TextBox _customSoundBox;

    // Tab 2: Display & Integration
    private readonly ComboBox _scaleCombo;
    private readonly ComboBox _layerCombo;
    private readonly ComboBox _fpsCombo;
    private readonly CheckBox _interactiveMouseCheck;
    private readonly CheckBox _cursorAvoidCheck;
    private readonly CheckBox _ignoreMaxCheck;
    private readonly CheckBox _hideFullscreenCheck;
    private readonly CheckBox _pauseBatterySaverCheck;
    private readonly CheckBox _primaryOnlyCheck;
    private readonly CheckBox _startupCheck;

    // Tab 3: Themes & Hotkeys
    private readonly ComboBox _themeCombo;
    private readonly CheckBox _enableHotkeyCheck;
    private readonly ComboBox _hotkeyCombo;
    private readonly Label _hotkeyStatusLabel;
    private readonly TextBox _ignoredProcessesBox;

    private bool _applied;

    public AppSettings ResultingSettings { get; private set; }

    public OptionsForm(
        Icon appIcon,
        AppSettings currentSettings,
        SoundPlayerService soundPlayer,
        Action<AppSettings> applyCallback)
    {
        _original = currentSettings.Clone();
        ResultingSettings = currentSettings.Clone();
        _soundPlayer = soundPlayer;
        _applyCallback = applyCallback;

        Text = "WinPenguins Options";
        Icon = appIcon;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f);
        ClientSize = new Size(800, 610);

        var rootLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 1,
            RowCount = 2
        };
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var tabs = new TabControl
        {
            Dock = DockStyle.Fill
        };

        // ===========================
        // Tab 1: Penguins & Sound
        // ===========================
        var tabBehaviorSound = new TabPage("Penguins & Sound")
        {
            Padding = new Padding(10),
            UseVisualStyleBackColor = true
        };
        var tab1Grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1
        };
        tab1Grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        tab1Grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        var behaviorGroup = new GroupBox
        {
            Text = "Population && Behavior",
            Dock = DockStyle.Fill,
            Padding = new Padding(10)
        };
        var behaviorTable = CreateTwoColumnGrid();
        int behaviorRow = 0;

        (_countSlider, _countValueLabel) = CreateSliderRow(
            behaviorTable, ref behaviorRow, "Penguin count:", AppSettings.MinPenguins, AppSettings.MaxPenguins, 10,
            val => val.ToString());
        (_speedSlider, _speedValueLabel) = CreateSliderRow(
            behaviorTable, ref behaviorRow, "Speed:", 0, AppSettings.MaxFrameDelayMs - AppSettings.MinFrameDelayMs, 15,
            val => $"{45.0 / (AppSettings.MaxFrameDelayMs - val):0.0}x");
        (_alphaSlider, _alphaValueLabel) = CreateSliderRow(
            behaviorTable, ref behaviorRow, "Opacity:", 15, 255, 32,
            val => $"{val * 100 / 255}%");
        (_santaSlider, _santaValueLabel) = CreateSliderRow(
            behaviorTable, ref behaviorRow, "Santa penguins:", 0, 100, 10,
            val => $"{val}%");

        _idlePausesCheck = AddOptionCheck(behaviorTable, ref behaviorRow, "Occasional idle pauses and glances");
        _parachutesCheck = AddOptionCheck(behaviorTable, ref behaviorRow, "Parachutes (soft spawns && mid-air saves)");
        _neverSplatCheck = AddOptionCheck(behaviorTable, ref behaviorRow, "Never splatter from high falls");

        (_splatSlider, _splatValueLabel) = CreateSliderRow(
            behaviorTable, ref behaviorRow, "Splat fall distance:", 100, 2000, 200,
            val => $"{val} px");
        _neverSplatCheck.CheckedChanged += (_, _) => _splatSlider.Enabled = !_neverSplatCheck.Checked;

        behaviorGroup.Controls.Add(behaviorTable);

        var soundGroup = new GroupBox
        {
            Text = "Audio",
            Dock = DockStyle.Fill,
            Padding = new Padding(10)
        };
        var soundTable = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            AutoScroll = true
        };
        soundTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        soundTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        soundTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        for (int r = 0; r < 7; r++)
        {
            soundTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }
        soundTable.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        int soundRow = 0;
        _soundEnabledCheck = AddOptionCheck(soundTable, ref soundRow, "Enable explosion / custom sound", span: 3);
        _actionSoundsCheck = AddOptionCheck(soundTable, ref soundRow, "Play grab && click sounds (Eep / Quack)", span: 3);

        (_volumeSlider, _volumeValueLabel) = CreateSliderRow(
            soundTable, ref soundRow, "Volume:", 0, 100, 10,
            val => $"{val}%", span: 3);

        soundTable.Controls.Add(new Label
        {
            Text = "Explosion sound:",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 10, 8, 6)
        }, 0, soundRow);

        _builtInSoundCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            MinimumSize = new Size(140, 0),
            Dock = DockStyle.Fill,
            Margin = new Padding(3, 6, 6, 6)
        };
        _builtInSoundCombo.Items.AddRange(new object[]
        {
            new ComboItem<string>("boom", "Boom (Classic)"),
            new ComboItem<string>("eep", "Eep"),
            new ComboItem<string>("quack", "Quack")
        });
        soundTable.Controls.Add(_builtInSoundCombo, 1, soundRow);

        var testSoundBtn = new Button
        {
            Text = "Test",
            AutoSize = true,
            MinimumSize = new Size(74, 28),
            Margin = new Padding(3, 5, 3, 6)
        };
        soundTable.Controls.Add(testSoundBtn, 2, soundRow);
        soundRow++;

        soundTable.Controls.Add(new Label
        {
            Text = "Custom .wav:",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 8, 8, 4)
        }, 0, soundRow);

        _customSoundBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(3, 6, 6, 4)
        };
        soundTable.Controls.Add(_customSoundBox, 1, soundRow);

        var browseBtn = new Button
        {
            Text = "Browse...",
            AutoSize = true,
            MinimumSize = new Size(74, 28),
            Margin = new Padding(3, 4, 3, 4)
        };
        browseBtn.Click += OnBrowseSound;
        soundTable.Controls.Add(browseBtn, 2, soundRow);
        soundRow++;

        var clearBtn = new Button
        {
            Text = "Clear custom",
            AutoSize = true,
            MinimumSize = new Size(110, 28),
            Anchor = AnchorStyles.Top | AnchorStyles.Left,
            Margin = new Padding(3, 4, 3, 4)
        };
        clearBtn.Click += (_, _) => _customSoundBox.Text = string.Empty;
        soundTable.Controls.Add(clearBtn, 1, soundRow);
        soundTable.SetColumnSpan(clearBtn, 2);

        testSoundBtn.Click += (_, _) =>
            _soundPlayer.PlayTest(GetSelectedBuiltInSound(), _customSoundBox.Text.Trim(), _volumeSlider.Value);

        soundGroup.Controls.Add(soundTable);

        tab1Grid.Controls.Add(behaviorGroup, 0, 0);
        tab1Grid.Controls.Add(soundGroup, 1, 0);
        tabBehaviorSound.Controls.Add(tab1Grid);

        // ===========================
        // Tab 2: Display & Integration
        // ===========================
        var tabDisplay = new TabPage("Display & Integration")
        {
            Padding = new Padding(10),
            UseVisualStyleBackColor = true
        };
        var displayGroup = new GroupBox
        {
            Text = "Windows 11 Display && Interaction",
            Dock = DockStyle.Fill,
            Padding = new Padding(12)
        };
        var displayTable = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoScroll = true
        };
        displayTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        displayTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        displayTable.Controls.Add(new Label
        {
            Text = "Penguin size:",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 6, 12, 6)
        }, 0, 0);
        _scaleCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 300,
            Margin = new Padding(3, 4, 3, 6)
        };
        _scaleCombo.Items.AddRange(new object[]
        {
            new ComboItem<SpriteScaleMode>(SpriteScaleMode.Auto, "Auto (Match per-monitor DPI)"),
            new ComboItem<SpriteScaleMode>(SpriteScaleMode.X1, "1x (Classic 32 px)"),
            new ComboItem<SpriteScaleMode>(SpriteScaleMode.X1_5, "1.5x (48 px)"),
            new ComboItem<SpriteScaleMode>(SpriteScaleMode.X2, "2x (64 px)"),
            new ComboItem<SpriteScaleMode>(SpriteScaleMode.X3, "3x (96 px)")
        });
        displayTable.Controls.Add(_scaleCombo, 1, 0);

        displayTable.Controls.Add(new Label
        {
            Text = "Layer:",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 6, 12, 6)
        }, 0, 1);
        _layerCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 300,
            Margin = new Padding(3, 4, 3, 6)
        };
        _layerCombo.Items.AddRange(new object[]
        {
            new ComboItem<OverlayLayerMode>(OverlayLayerMode.AboveWindows, "Above windows (walk on titlebars)"),
            new ComboItem<OverlayLayerMode>(OverlayLayerMode.DesktopOnly, "Desktop only (behind open windows)")
        });
        displayTable.Controls.Add(_layerCombo, 1, 1);

        displayTable.Controls.Add(new Label
        {
            Text = "Refresh rate:",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 6, 12, 6)
        }, 0, 2);
        _fpsCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 300,
            Margin = new Padding(3, 4, 3, 10)
        };
        _fpsCombo.Items.AddRange(new object[]
        {
            new ComboItem<int>(60, "60 FPS (Low power)"),
            new ComboItem<int>(120, "120 FPS (Recommended)"),
            new ComboItem<int>(240, "240 FPS (Ultra smooth)")
        });
        displayTable.Controls.Add(_fpsCombo, 1, 2);

        int dispRow = 3;
        _interactiveMouseCheck = AddOptionCheck(displayTable, ref dispRow, "Interactive mouse (click to hop, drag && throw across monitors)");
        _cursorAvoidCheck = AddOptionCheck(displayTable, ref dispRow, "Penguins avoid mouse cursor (scatter away when approached)");
        _ignoreMaxCheck = AddOptionCheck(displayTable, ref dispRow, "Ignore maximized windows (walk on taskbar & floating popups)");
        _hideFullscreenCheck = AddOptionCheck(displayTable, ref dispRow, "Hide during fullscreen apps / games");
        _pauseBatterySaverCheck = AddOptionCheck(displayTable, ref dispRow, "Pause automatically when Windows Battery Saver is active");
        _primaryOnlyCheck = AddOptionCheck(displayTable, ref dispRow, "Primary monitor only");
        _startupCheck = AddOptionCheck(displayTable, ref dispRow, "Launch automatically with Windows");

        displayGroup.Controls.Add(displayTable);
        tabDisplay.Controls.Add(displayGroup);

        // ===========================
        // Tab 3: Themes & Hotkeys
        // ===========================
        var tabThemes = new TabPage("Themes & Hotkeys")
        {
            Padding = new Padding(10),
            UseVisualStyleBackColor = true
        };
        var tab3Layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3
        };
        tab3Layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tab3Layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tab3Layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        // 3a. Themes
        var themeGroup = new GroupBox
        {
            Text = "Sprite Themes (PNG Theme Packs)",
            Dock = DockStyle.Fill,
            Padding = new Padding(10),
            AutoSize = true
        };
        var themeTable = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            AutoSize = true
        };
        themeTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        themeTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        themeTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        themeTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        themeTable.Controls.Add(new Label
        {
            Text = "Active theme:",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 6, 8, 6)
        }, 0, 0);

        _themeCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Dock = DockStyle.Fill,
            Margin = new Padding(3, 4, 8, 6)
        };
        themeTable.Controls.Add(_themeCombo, 1, 0);

        var openThemesBtn = new Button
        {
            Text = "Open Folder",
            AutoSize = true,
            MinimumSize = new Size(96, 28),
            Margin = new Padding(3, 2, 4, 6)
        };
        openThemesBtn.Click += OnOpenThemesFolder;
        themeTable.Controls.Add(openThemesBtn, 2, 0);

        var createSampleBtn = new Button
        {
            Text = "Create Sample",
            AutoSize = true,
            MinimumSize = new Size(108, 28),
            Margin = new Padding(3, 2, 3, 6)
        };
        createSampleBtn.Click += OnCreateSampleTheme;
        themeTable.Controls.Add(createSampleBtn, 3, 0);

        var themeHelpLabel = new Label
        {
            Text = $"Place custom folders containing theme.json + 32-bit transparent PNG sprite sheets in:\r\n{ThemeDiscovery.UserThemesDirectory}",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(3, 2, 3, 2)
        };
        themeTable.Controls.Add(themeHelpLabel, 0, 1);
        themeTable.SetColumnSpan(themeHelpLabel, 4);
        themeGroup.Controls.Add(themeTable);

        // 3b. Global Hotkey
        var hotkeyGroup = new GroupBox
        {
            Text = "Global Pause / Hide Hotkey",
            Dock = DockStyle.Fill,
            Padding = new Padding(10),
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 8)
        };
        var hotkeyTable = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            AutoSize = true
        };
        hotkeyTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        hotkeyTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        hotkeyTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _enableHotkeyCheck = new CheckBox
        {
            Text = "Enable global pause / resume shortcut",
            AutoSize = true,
            Margin = new Padding(3, 4, 12, 4)
        };
        hotkeyTable.Controls.Add(_enableHotkeyCheck, 0, 0);
        hotkeyTable.SetColumnSpan(_enableHotkeyCheck, 3);

        hotkeyTable.Controls.Add(new Label
        {
            Text = "Shortcut:",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 8, 8, 4)
        }, 0, 1);

        _hotkeyCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDown,
            Width = 170,
            Margin = new Padding(3, 5, 10, 4)
        };
        foreach (string preset in GlobalHotkeyParser.Presets)
        {
            _hotkeyCombo.Items.Add(preset);
        }
        hotkeyTable.Controls.Add(_hotkeyCombo, 1, 1);

        _hotkeyStatusLabel = new Label
        {
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 8, 3, 4)
        };
        hotkeyTable.Controls.Add(_hotkeyStatusLabel, 2, 1);

        _enableHotkeyCheck.CheckedChanged += (_, _) =>
        {
            _hotkeyCombo.Enabled = _enableHotkeyCheck.Checked;
            UpdateHotkeyStatusLabel();
        };
        _hotkeyCombo.TextChanged += (_, _) => UpdateHotkeyStatusLabel();
        hotkeyGroup.Controls.Add(hotkeyTable);

        // 3c. Ignored Processes
        var ignoreProcGroup = new GroupBox
        {
            Text = "Ignored Applications (No Collisions / No Fullscreen Pause)",
            Dock = DockStyle.Fill,
            Padding = new Padding(10)
        };
        var ignoreProcTable = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        ignoreProcTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        ignoreProcTable.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        ignoreProcTable.Controls.Add(new Label
        {
            Text = "Process names separated by commas or newlines (without or with .exe, e.g.: spotify, discord, obs64):",
            AutoSize = true,
            Margin = new Padding(3, 2, 3, 6)
        }, 0, 0);

        _ignoredProcessesBox = new TextBox
        {
            Multiline = true,
            AcceptsReturn = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            Margin = new Padding(3, 2, 3, 2)
        };
        ignoreProcTable.Controls.Add(_ignoredProcessesBox, 0, 1);
        ignoreProcGroup.Controls.Add(ignoreProcTable);

        tab3Layout.Controls.Add(themeGroup, 0, 0);
        tab3Layout.Controls.Add(hotkeyGroup, 0, 1);
        tab3Layout.Controls.Add(ignoreProcGroup, 0, 2);
        tabThemes.Controls.Add(tab3Layout);

        tabs.TabPages.Add(tabBehaviorSound);
        tabs.TabPages.Add(tabDisplay);
        tabs.TabPages.Add(tabThemes);

        // Footer buttons
        var buttonBar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 4,
            Margin = new Padding(0, 8, 0, 0)
        };
        buttonBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        buttonBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        buttonBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        buttonBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var resetBtn = new Button
        {
            Text = "Reset Defaults",
            AutoSize = true,
            MinimumSize = new Size(110, 32),
            Anchor = AnchorStyles.Left
        };
        resetBtn.Click += (_, _) => LoadFromSettings(new AppSettings());

        var okBtn = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            AutoSize = true,
            MinimumSize = new Size(86, 32),
            Margin = new Padding(6, 0, 0, 0)
        };
        okBtn.Click += (_, _) => ApplyCurrent();

        var cancelBtn = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            AutoSize = true,
            MinimumSize = new Size(86, 32),
            Margin = new Padding(6, 0, 0, 0)
        };

        var applyBtn = new Button
        {
            Text = "Apply",
            AutoSize = true,
            MinimumSize = new Size(86, 32),
            Margin = new Padding(6, 0, 0, 0)
        };
        applyBtn.Click += (_, _) =>
        {
            ApplyCurrent();
            _applied = true;
        };

        buttonBar.Controls.Add(resetBtn, 0, 0);
        buttonBar.Controls.Add(okBtn, 1, 0);
        buttonBar.Controls.Add(cancelBtn, 2, 0);
        buttonBar.Controls.Add(applyBtn, 3, 0);

        rootLayout.Controls.Add(tabs, 0, 0);
        rootLayout.Controls.Add(buttonBar, 0, 1);

        Controls.Add(rootLayout);
        AcceptButton = okBtn;
        CancelButton = cancelBtn;

        PopulateThemeList(currentSettings.ThemeName);
        LoadFromSettings(currentSettings);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (DialogResult == DialogResult.Cancel && _applied)
        {
            _applied = false;
            _applyCallback(_original.Clone());
        }

        base.OnFormClosing(e);
    }

    private void ApplyCurrent()
    {
        AppSettings updated = BuildSettings();
        ResultingSettings = updated;
        _applyCallback(updated);
    }

    private AppSettings BuildSettings()
    {
        string rawHotkey = _hotkeyCombo.Text.Trim();
        string normalizedHotkey = GlobalHotkeyParser.TryParse(rawHotkey, out _, out _, out string clean)
            ? clean
            : _original.PauseHotkey;

        var settings = new AppSettings
        {
            PenguinCount = _countSlider.Value,
            FrameDelayMs = AppSettings.MaxFrameDelayMs - _speedSlider.Value,
            Opacity = (byte)_alphaSlider.Value,
            SantaPercent = _santaSlider.Value,
            EnableIdlePauses = _idlePausesCheck.Checked,
            EnableParachutes = _parachutesCheck.Checked,
            NeverSplat = _neverSplatCheck.Checked,
            SplatDistance = _splatSlider.Value,
            ThemeName = GetSelectedThemeName(),
            ScaleMode = ((ComboItem<SpriteScaleMode>)_scaleCombo.SelectedItem!).Value,
            LayerMode = ((ComboItem<OverlayLayerMode>)_layerCombo.SelectedItem!).Value,
            TargetFps = ((ComboItem<int>)_fpsCombo.SelectedItem!).Value,
            InteractiveMouse = _interactiveMouseCheck.Checked,
            CursorAvoidance = _cursorAvoidCheck.Checked,
            IgnoreMaximizedWindows = _ignoreMaxCheck.Checked,
            HideInFullscreen = _hideFullscreenCheck.Checked,
            PauseOnBatterySaver = _pauseBatterySaverCheck.Checked,
            PrimaryMonitorOnly = _primaryOnlyCheck.Checked,
            LaunchAtStartup = _startupCheck.Checked,
            SoundEnabled = _soundEnabledCheck.Checked,
            EnableActionSounds = _actionSoundsCheck.Checked,
            SoundVolume = _volumeSlider.Value,
            BuiltInSound = GetSelectedBuiltInSound(),
            CustomSoundPath = _customSoundBox.Text.Trim(),
            EnablePauseHotkey = _enableHotkeyCheck.Checked,
            PauseHotkey = normalizedHotkey,
            IgnoredProcesses = _ignoredProcessesBox.Text.Trim()
        };
        settings.Normalize();
        return settings;
    }

    private void LoadFromSettings(AppSettings s)
    {
        AppSettings copy = s.Clone();
        copy.Normalize();

        _countSlider.Value = copy.PenguinCount;
        _speedSlider.Value = AppSettings.MaxFrameDelayMs - copy.FrameDelayMs;
        _alphaSlider.Value = copy.Opacity;
        _santaSlider.Value = copy.SantaPercent;
        _idlePausesCheck.Checked = copy.EnableIdlePauses;
        _parachutesCheck.Checked = copy.EnableParachutes;
        _neverSplatCheck.Checked = copy.NeverSplat;
        _splatSlider.Value = Math.Clamp(copy.SplatDistance, _splatSlider.Minimum, _splatSlider.Maximum);
        _splatSlider.Enabled = !copy.NeverSplat;

        PopulateThemeList(copy.ThemeName);
        SelectComboValue(_scaleCombo, copy.ScaleMode);
        SelectComboValue(_layerCombo, copy.LayerMode);
        SelectComboValue(_fpsCombo, copy.TargetFps);
        _interactiveMouseCheck.Checked = copy.InteractiveMouse;
        _cursorAvoidCheck.Checked = copy.CursorAvoidance;
        _ignoreMaxCheck.Checked = copy.IgnoreMaximizedWindows;
        _hideFullscreenCheck.Checked = copy.HideInFullscreen;
        _pauseBatterySaverCheck.Checked = copy.PauseOnBatterySaver;
        _primaryOnlyCheck.Checked = copy.PrimaryMonitorOnly;
        _startupCheck.Checked = copy.LaunchAtStartup;

        _soundEnabledCheck.Checked = copy.SoundEnabled;
        _actionSoundsCheck.Checked = copy.EnableActionSounds;
        _volumeSlider.Value = copy.SoundVolume;
        SelectComboValue(_builtInSoundCombo, copy.BuiltInSound);
        _customSoundBox.Text = copy.CustomSoundPath;

        _enableHotkeyCheck.Checked = copy.EnablePauseHotkey;
        _hotkeyCombo.Text = copy.PauseHotkey;
        _hotkeyCombo.Enabled = copy.EnablePauseHotkey;
        _ignoredProcessesBox.Text = copy.IgnoredProcesses;

        UpdateHotkeyStatusLabel();
        UpdateAllSliderLabels();
    }

    private void PopulateThemeList(string selectedThemeName)
    {
        _themeCombo.Items.Clear();
        IReadOnlyList<ThemeDescriptor> themes = ThemeDiscovery.ListThemes();
        foreach (ThemeDescriptor theme in themes)
        {
            string label = theme.IsBuiltIn ? $"{theme.Name} (Built-in)" : $"{theme.Name} (Custom PNG)";
            _themeCombo.Items.Add(new ComboItem<string>(theme.Name, label));
        }

        SelectComboValue(_themeCombo, selectedThemeName);
    }

    private string GetSelectedThemeName() =>
        (_themeCombo.SelectedItem as ComboItem<string>)?.Value ?? "Classic";

    private string GetSelectedBuiltInSound() =>
        (_builtInSoundCombo.SelectedItem as ComboItem<string>)?.Value ?? "boom";

    private void UpdateHotkeyStatusLabel()
    {
        if (!_enableHotkeyCheck.Checked)
        {
            _hotkeyStatusLabel.ForeColor = SystemColors.GrayText;
            _hotkeyStatusLabel.Text = "Disabled";
            return;
        }

        if (GlobalHotkeyParser.TryParse(_hotkeyCombo.Text, out _, out _, out string normalized))
        {
            _hotkeyStatusLabel.ForeColor = Color.DarkGreen;
            _hotkeyStatusLabel.Text = $"Valid: {normalized}";
        }
        else
        {
            _hotkeyStatusLabel.ForeColor = Color.Firebrick;
            _hotkeyStatusLabel.Text = "Use Modifier+Key (e.g. Ctrl+Alt+P)";
        }
    }

    private void UpdateAllSliderLabels()
    {
        _countValueLabel.Text = _countSlider.Value.ToString();
        _speedValueLabel.Text = $"{45.0 / (AppSettings.MaxFrameDelayMs - _speedSlider.Value):0.0}x";
        _alphaValueLabel.Text = $"{_alphaSlider.Value * 100 / 255}%";
        _santaValueLabel.Text = $"{_santaSlider.Value}%";
        _splatValueLabel.Text = $"{_splatSlider.Value} px";
        _volumeValueLabel.Text = $"{_volumeSlider.Value}%";
    }

    private void OnBrowseSound(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Select Sound File",
            Filter = "Wave Files (*.wav)|*.wav",
            CheckFileExists = true
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _customSoundBox.Text = dlg.FileName;
        }
    }

    private void OnOpenThemesFolder(object? sender, EventArgs e)
    {
        Directory.CreateDirectory(ThemeDiscovery.UserThemesDirectory);
        Process.Start(new ProcessStartInfo("explorer.exe", ThemeDiscovery.UserThemesDirectory)
        {
            UseShellExecute = true
        });
    }

    private void OnCreateSampleTheme(object? sender, EventArgs e)
    {
        string folder = SpriteCatalog.CreateSampleTheme();
        PopulateThemeList("SampleCustom");
        MessageBox.Show(
            this,
            $"Sample PNG theme exported to:\r\n{folder}\r\n\r\nEdit the PNGs or theme.json to customize sprites, or select SampleCustom now to test!",
            "Sample Theme Created",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private static TableLayoutPanel CreateTwoColumnGrid()
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoScroll = true
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        return grid;
    }

    private static (TrackBar Slider, Label ValueLabel) CreateSliderRow(
        TableLayoutPanel table,
        ref int nextRow,
        string caption,
        int min,
        int max,
        int tickFreq,
        Func<int, string> formatter,
        int span = 2)
    {
        var headerRow = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Margin = new Padding(0, 4, 0, 0)
        };
        headerRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        headerRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var title = new Label
        {
            Text = caption,
            AutoSize = true,
            Anchor = AnchorStyles.Left
        };
        var valueLabel = new Label
        {
            Text = formatter(min),
            AutoSize = true,
            Anchor = AnchorStyles.Right,
            Font = new Font("Segoe UI Semibold", 9f)
        };
        headerRow.Controls.Add(title, 0, 0);
        headerRow.Controls.Add(valueLabel, 1, 0);

        var slider = new TrackBar
        {
            Minimum = min,
            Maximum = max,
            TickFrequency = tickFreq,
            AutoSize = false,
            Height = 30,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 2)
        };
        slider.ValueChanged += (_, _) => valueLabel.Text = formatter(slider.Value);

        table.Controls.Add(headerRow, 0, nextRow);
        table.SetColumnSpan(headerRow, span);
        nextRow++;

        table.Controls.Add(slider, 0, nextRow);
        table.SetColumnSpan(slider, span);
        nextRow++;

        return (slider, valueLabel);
    }

    private static CheckBox AddOptionCheck(TableLayoutPanel table, ref int row, string text, int span = 2)
    {
        var chk = new CheckBox
        {
            Text = text,
            AutoSize = true,
            Margin = new Padding(3, 5, 3, 4)
        };
        table.Controls.Add(chk, 0, row);
        table.SetColumnSpan(chk, span);
        row++;
        return chk;
    }

    private static void SelectComboValue<T>(ComboBox combo, T target)
    {
        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is ComboItem<T> item && EqualityComparer<T>.Default.Equals(item.Value, target))
            {
                combo.SelectedIndex = i;
                return;
            }
        }

        if (combo.Items.Count > 0)
        {
            combo.SelectedIndex = 0;
        }
    }

    private sealed record ComboItem<T>(T Value, string Display)
    {
        public override string ToString() => Display;
    }
}

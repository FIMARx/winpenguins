using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using WinPenguins.Assets;
using WinPenguins.Config;
using WinPenguins.Engine;
using WinPenguins.Platform;

namespace WinPenguins.UI;

public enum PenguinQuickAction
{
    SpawnFish,
    SpawnTrampoline,
    SpawnPortals,
    ClearToys,
    Scatter
}

public sealed class OptionsForm : Form
{
    private readonly AppSettings _original;
    private readonly Action<AppSettings> _applyCallback;
    private readonly Action<PenguinQuickAction>? _quickActionCallback;
    private readonly SoundPlayerService _soundPlayer;
    private readonly FluentPalette _palette;
    private readonly System.Windows.Forms.Timer _previewTimer;
    private readonly List<Bitmap> _previewFrames = [];
    private int _previewFrameIndex;
    private string _previewThemeName = "Classic";

    private readonly Font _titleFont = new("Segoe UI Semibold", 15f);
    private readonly Font _sectionFont = new("Segoe UI Semibold", 9.75f);
    private readonly Font _rowTitleFont = new("Segoe UI Semibold", 9.5f);
    private readonly Font _rowDescFont = new("Segoe UI", 8.5f);
    private readonly Font _brandTitleFont = new("Segoe UI Semibold", 11f);
    private readonly Font _badgeFont = new("Segoe UI Semibold", 9.5f);

    private readonly List<(FluentNavButton Button, Panel Page)> _pages = [];

    // Page 1: Penguins & Motion
    private readonly FluentSlider _countSlider;
    private readonly Label _countValueLabel;
    private readonly FluentSlider _speedSlider;
    private readonly Label _speedValueLabel;
    private readonly FluentSlider _alphaSlider;
    private readonly Label _alphaValueLabel;
    private readonly FluentSlider _santaSlider;
    private readonly Label _santaValueLabel;
    private readonly FluentToggleSwitch _idlePausesToggle;
    private readonly FluentToggleSwitch _parachutesToggle;
    private readonly FluentToggleSwitch _neverSplatToggle;
    private readonly FluentSlider _splatSlider;
    private readonly Label _splatValueLabel;

    // Page 2: Mouse, Toys & Snow
    private readonly FluentToggleSwitch _interactiveMouseToggle;
    private readonly FluentToggleSwitch _cursorPerchToggle;
    private readonly FluentToggleSwitch _cursorAvoidToggle;
    private readonly FluentToggleSwitch _snowToggle;

    // Page 3: Audio & Ambient
    private readonly FluentToggleSwitch _musicDanceToggle;
    private readonly FluentToggleSwitch _soundEnabledToggle;
    private readonly FluentToggleSwitch _actionSoundsToggle;
    private readonly FluentSlider _volumeSlider;
    private readonly Label _volumeValueLabel;
    private readonly ComboBox _builtInSoundCombo;
    private readonly TextBox _customSoundBox;
    private readonly FluentToggleSwitch _hideFullscreenToggle;
    private readonly FluentToggleSwitch _pauseFocusToggle;
    private readonly FluentToggleSwitch _pauseBatterySaverToggle;
    private readonly FluentToggleSwitch _pauseRecordingToggle;
    private readonly TextBox _recordingProcBox;

    // Page 4: Display & Monitors
    private readonly ComboBox _scaleCombo;
    private readonly ComboBox _layerCombo;
    private readonly ComboBox _fpsCombo;
    private readonly FluentToggleSwitch _primaryOnlyToggle;
    private readonly FluentToggleSwitch _ignoreMaxToggle;
    private readonly FluentToggleSwitch _startupToggle;
    private readonly TextBox _ignoredProcessesBox;

    // Page 5: Themes & Hotkeys
    private readonly ComboBox _themeCombo;
    private readonly PictureBox _previewPictureBox;
    private readonly Label _previewCaptionLabel;
    private readonly FluentToggleSwitch _enableHotkeyToggle;
    private readonly ComboBox _hotkeyCombo;
    private readonly Label _hotkeyStatusLabel;

    private bool _applied;

    public AppSettings ResultingSettings { get; private set; }

    public OptionsForm(
        Icon appIcon,
        AppSettings currentSettings,
        SoundPlayerService soundPlayer,
        Action<AppSettings> applyCallback,
        Action<PenguinQuickAction>? quickActionCallback = null)
    {
        _original = currentSettings.Clone();
        ResultingSettings = currentSettings.Clone();
        _soundPlayer = soundPlayer;
        _applyCallback = applyCallback;
        _quickActionCallback = quickActionCallback;
        _palette = FluentPalette.DetectCurrent();

        Text = "WinPenguins Options";
        Icon = appIcon;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(800, 540);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f);
        ClientSize = new Size(920, 700);
        FluentUi.ApplyWindowChrome(this, _palette);

        var rootLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = _palette.WindowBg
        };
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var mainSplit = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0)
        };
        mainSplit.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 218));
        mainSplit.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // Sidebar navigation rail
        var sidebar = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = _palette.SidebarBg,
            Padding = new Padding(12, 16, 12, 12)
        };
        sidebar.Paint += (_, e) =>
        {
            using var pen = new Pen(_palette.Divider, 1f);
            e.Graphics.DrawLine(pen, sidebar.Width - 1, 0, sidebar.Width - 1, sidebar.Height);
        };

        var sidebarStack = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = _palette.SidebarBg
        };
        sidebarStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        sidebarStack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        sidebarStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var brandRow = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            ColumnCount = 2,
            Margin = new Padding(4, 0, 4, 18),
            BackColor = _palette.SidebarBg
        };
        brandRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        brandRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var brandIcon = new PictureBox
        {
            Image = appIcon.ToBitmap(),
            Size = new Size(34, 34),
            SizeMode = PictureBoxSizeMode.Zoom,
            Margin = new Padding(0, 2, 10, 0),
            BackColor = _palette.SidebarBg
        };
        var brandTextPanel = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = _palette.SidebarBg
        };
        brandTextPanel.Controls.Add(new Label
        {
            Text = "WinPenguins",
            Font = _brandTitleFont,
            ForeColor = _palette.TextPrimary,
            AutoSize = true,
            Margin = new Padding(0)
        }, 0, 0);
        brandTextPanel.Controls.Add(new Label
        {
            Text = "v2.1 · Windows 11",
            Font = _rowDescFont,
            ForeColor = _palette.TextSecondary,
            AutoSize = true,
            Margin = new Padding(0)
        }, 0, 1);
        brandRow.Controls.Add(brandIcon, 0, 0);
        brandRow.Controls.Add(brandTextPanel, 1, 0);
        sidebarStack.Controls.Add(brandRow, 0, 0);

        var navList = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            BackColor = _palette.SidebarBg
        };
        sidebarStack.Controls.Add(navList, 0, 1);

        sidebarStack.Controls.Add(new Label
        {
            Text = "Tip: Drag any penguin cleanly across monitors, or open the tray menu for quick actions.",
            Font = _rowDescFont,
            ForeColor = _palette.TextSecondary,
            MaximumSize = new Size(188, 0),
            AutoSize = true,
            Margin = new Padding(6, 12, 6, 4)
        }, 0, 2);

        sidebar.Controls.Add(sidebarStack);
        mainSplit.Controls.Add(sidebar, 0, 0);

        var contentHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = _palette.WindowBg
        };
        mainSplit.Controls.Add(contentHost, 1, 0);

        // ===========================
        // Page 1: Penguins & Motion
        // ===========================
        TableLayoutPanel page1 = CreatePage(contentHost, "Penguins & Motion", "Control colony population, movement pace, appearance, and playful physics.");
        int p1Row = 1;

        AddSectionHeader(page1, ref p1Row, "Colony");
        TableLayoutPanel colonyRows = AddCard(page1, ref p1Row);
        int cRow = 0;
        (_countSlider, _countValueLabel) = AddSliderRow(colonyRows, ref cRow, "Penguin count", "Active penguins roaming windows and desktops", AppSettings.MinPenguins, AppSettings.MaxPenguins, v => v.ToString());
        AddDivider(colonyRows, ref cRow);
        (_speedSlider, _speedValueLabel) = AddSliderRow(colonyRows, ref cRow, "Walk & action speed", "Adjust simulation pacing across frames", 0, AppSettings.MaxFrameDelayMs - AppSettings.MinFrameDelayMs, v => $"{45.0 / (AppSettings.MaxFrameDelayMs - v):0.0}x");
        AddDivider(colonyRows, ref cRow);
        (_alphaSlider, _alphaValueLabel) = AddSliderRow(colonyRows, ref cRow, "Sprite opacity", "Blend translucency across your open apps", 15, 255, v => $"{v * 100 / 255}%");
        AddDivider(colonyRows, ref cRow);
        (_santaSlider, _santaValueLabel) = AddSliderRow(colonyRows, ref cRow, "Santa penguins", "Percentage of festive Santa walkers & climbers", 0, 100, v => $"{v}%");

        AddSectionHeader(page1, ref p1Row, "Physics & Lifelike Habits");
        TableLayoutPanel habitRows = AddCard(page1, ref p1Row);
        int hRow = 0;
        _idlePausesToggle = AddToggleRow(habitRows, ref hRow, "Occasional idle pauses & glances", "Walkers briefly stop to look around before continuing");
        AddDivider(habitRows, ref hRow);
        _parachutesToggle = AddToggleRow(habitRows, ref hRow, "Umbrella parachutes", "Allow gentle umbrella spawns and mid-air parachute rescues");
        AddDivider(habitRows, ref hRow);
        _neverSplatToggle = AddToggleRow(habitRows, ref hRow, "Never splat from high falls", "Penguins tumble softly back onto their feet after long drops");
        AddDivider(habitRows, ref hRow);
        (_splatSlider, _splatValueLabel) = AddSliderRow(habitRows, ref hRow, "Safe fall height before splat", "Used only when Never Splat is disabled", 100, 2000, v => $"{v} px");
        _neverSplatToggle.CheckedChanged += (_, _) => _splatSlider.Enabled = !_neverSplatToggle.Checked;

        // ===========================
        // Page 2: Mouse, Toys & Snow
        // ===========================
        TableLayoutPanel page2 = CreatePage(contentHost, "Mouse, Toys & Snow", "Direct pointer interactions, interactive desktop toys, and ambient weather.");
        int p2Row = 1;

        AddSectionHeader(page2, ref p2Row, "Quick Toybox");
        TableLayoutPanel toyBoxRows = AddCard(page2, ref p2Row);
        var toyStrip = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            ColumnCount = 6,
            RowCount = 1,
            BackColor = _palette.CardBg,
            Margin = new Padding(0, 2, 0, 2)
        };
        for (int i = 0; i < 5; i++)
        {
            toyStrip.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        }
        toyStrip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toyStrip.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        toyStrip.Controls.Add(CreateQuickActionButton("Drop Fish", PenguinQuickAction.SpawnFish), 0, 0);
        toyStrip.Controls.Add(CreateQuickActionButton("Trampoline", PenguinQuickAction.SpawnTrampoline), 1, 0);
        toyStrip.Controls.Add(CreateQuickActionButton("Twin Portals", PenguinQuickAction.SpawnPortals), 2, 0);
        toyStrip.Controls.Add(CreateQuickActionButton("Clear Toys", PenguinQuickAction.ClearToys), 3, 0);
        toyStrip.Controls.Add(CreateQuickActionButton("Scatter All", PenguinQuickAction.Scatter), 4, 0);
        toyBoxRows.Controls.Add(toyStrip, 0, 0);

        AddSectionHeader(page2, ref p2Row, "Cursor & Desktop Atmosphere");
        TableLayoutPanel interactRows = AddCard(page2, ref p2Row);
        int iRow = 0;
        _interactiveMouseToggle = AddToggleRow(interactRows, ref iRow, "Interactive mouse", "Click to hop & quack, or grab and throw across monitors");
        AddDivider(interactRows, ref iRow);
        _cursorPerchToggle = AddToggleRow(interactRows, ref iRow, "Cursor perch", "Penguins hop onto a stationary cursor — shake quickly to fling off");
        AddDivider(interactRows, ref iRow);
        _cursorAvoidToggle = AddToggleRow(interactRows, ref iRow, "Avoid mouse cursor", "Walkers scatter away whenever your pointer moves close");
        AddDivider(interactRows, ref iRow);
        _snowToggle = AddToggleRow(interactRows, ref iRow, "Let it snow", "Gentle falling snow settling on windows and scattering underfoot");

        // ===========================
        // Page 3: Audio & Ambient
        // ===========================
        TableLayoutPanel page3 = CreatePage(contentHost, "Audio & Ambient", "Configure sound effects, music-reactive dancing, and smart auto-pause rules.");
        int p3Row = 1;

        AddSectionHeader(page3, ref p3Row, "Music & Sound Effects");
        TableLayoutPanel audioRows = AddCard(page3, ref p3Row);
        int aRow = 0;
        _musicDanceToggle = AddToggleRow(audioRows, ref aRow, "Dance to playing Windows music", "Detect audio beats via WASAPI and hop in rhythm");
        AddDivider(audioRows, ref aRow);
        _soundEnabledToggle = AddToggleRow(audioRows, ref aRow, "Enable explosion / custom sound", "Plays on hard splats and exit bombs");
        AddDivider(audioRows, ref aRow);
        _actionSoundsToggle = AddToggleRow(audioRows, ref aRow, "Play grab, bounce & chirp sounds", "Play subtle Eep and Quack effects when interacting");
        AddDivider(audioRows, ref aRow);
        (_volumeSlider, _volumeValueLabel) = AddSliderRow(audioRows, ref aRow, "Sound volume", "Master WAV output level", 0, 100, v => $"{v}%");
        AddDivider(audioRows, ref aRow);

        _builtInSoundCombo = CreateStyledComboBox(DropDownStyle: ComboBoxStyle.DropDownList, width: 175);
        _builtInSoundCombo.Items.AddRange(new object[]
        {
            new ComboItem<string>("boom", "Boom (Classic)"),
            new ComboItem<string>("eep", "Eep"),
            new ComboItem<string>("quack", "Quack")
        });
        _customSoundBox = new TextBox { Font = Font };
        var testBtn = CreateSecondaryButton("Test", 74);
        testBtn.Click += (_, _) => _soundPlayer.PlayTest(GetSelectedBuiltInSound(), _customSoundBox.Text.Trim(), _volumeSlider.Value);
        var builtInRight = CreateInlinePair(_builtInSoundCombo, testBtn);
        AddControlRow(audioRows, ref aRow, "Explosion sound effect", "Built-in WAV sound when custom .wav is empty", builtInRight);
        AddDivider(audioRows, ref aRow);

        var browseBtn = CreateSecondaryButton("Browse...", 86);
        browseBtn.Margin = new Padding(0, 0, 6, 0);
        browseBtn.Click += OnBrowseSound;
        var clearBtn = CreateSecondaryButton("Clear", 68);
        clearBtn.Click += (_, _) => _customSoundBox.Text = string.Empty;

        var customRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 3,
            Margin = new Padding(0, 4, 0, 2)
        };
        customRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        customRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        customRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        customRow.Controls.Add(FluentUi.WrapTextBox(_customSoundBox, _palette, new Padding(0, 1, 8, 0)), 0, 0);
        customRow.Controls.Add(browseBtn, 1, 0);
        customRow.Controls.Add(clearBtn, 2, 0);

        AddStackedControlRow(audioRows, ref aRow, "Custom .wav sound (optional)", "Overrides built-in explosion sound when set", customRow);

        AddSectionHeader(page3, ref p3Row, "Smart Auto-Pause");
        TableLayoutPanel pauseRows = AddCard(page3, ref p3Row);
        int paRow = 0;
        _hideFullscreenToggle = AddToggleRow(pauseRows, ref paRow, "Hide during fullscreen apps & games", "Automatically pauses overlay when a borderless/fullscreen app is active");
        AddDivider(pauseRows, ref paRow);
        _pauseFocusToggle = AddToggleRow(pauseRows, ref paRow, "Pause during Presentation Mode / Focus Assist", "Respects Windows Quiet Hours and active presentations");
        AddDivider(pauseRows, ref paRow);
        _pauseBatterySaverToggle = AddToggleRow(pauseRows, ref paRow, "Pause on Windows Battery Saver", "Conserves power when laptop battery saver engages");
        AddDivider(pauseRows, ref paRow);
        _pauseRecordingToggle = AddToggleRow(pauseRows, ref paRow, "Pause while recording / streaming apps run", "Stops desktop activity whenever matching executables are open");

        _recordingProcBox = new TextBox { Font = Font };
        _pauseRecordingToggle.CheckedChanged += (_, _) => _recordingProcBox.Enabled = _pauseRecordingToggle.Checked;
        Control recHost = FluentUi.WrapTextBox(_recordingProcBox, _palette, new Padding(0, 6, 0, 2));
        recHost.Dock = DockStyle.Top;
        pauseRows.Controls.Add(recHost, 0, paRow++);
        pauseRows.SetColumnSpan(recHost, 2);

        // ===========================
        // Page 4: Display & Monitors
        // ===========================
        TableLayoutPanel page4 = CreatePage(contentHost, "Display & Monitors", "Multi-monitor DPI scaling, window layering, frame pacing, and exclusions.");
        int p4Row = 1;

        AddSectionHeader(page4, ref p4Row, "Rendering & Scaling");
        TableLayoutPanel renderRows = AddCard(page4, ref p4Row);
        int rRow = 0;

        _scaleCombo = CreateStyledComboBox(ComboBoxStyle.DropDownList, 235);
        _scaleCombo.Items.AddRange(new object[]
        {
            new ComboItem<SpriteScaleMode>(SpriteScaleMode.Auto, "Auto (Follow each monitor DPI)"),
            new ComboItem<SpriteScaleMode>(SpriteScaleMode.X1, "100% (32×32 Classic)"),
            new ComboItem<SpriteScaleMode>(SpriteScaleMode.X1_5, "150% (48×48)"),
            new ComboItem<SpriteScaleMode>(SpriteScaleMode.X2, "200% (64×64 Sharp)"),
            new ComboItem<SpriteScaleMode>(SpriteScaleMode.X3, "300% (96×96 Big)")
        });
        AddControlRow(renderRows, ref rRow, "Sprite size scale", "Per-monitor Auto scales penguins dynamically per screen", _scaleCombo);
        AddDivider(renderRows, ref rRow);

        _layerCombo = CreateStyledComboBox(ComboBoxStyle.DropDownList, 235);
        _layerCombo.Items.AddRange(new object[]
        {
            new ComboItem<OverlayLayerMode>(OverlayLayerMode.AboveWindows, "Above windows (Overlay)"),
            new ComboItem<OverlayLayerMode>(OverlayLayerMode.DesktopOnly, "Desktop wallpaper level only")
        });
        AddControlRow(renderRows, ref rRow, "Overlay layer", "Choose whether penguins walk on open app windows or only on desktop", _layerCombo);
        AddDivider(renderRows, ref rRow);

        _fpsCombo = CreateStyledComboBox(ComboBoxStyle.DropDownList, 235);
        _fpsCombo.Items.AddRange(new object[]
        {
            new ComboItem<int>(60, "60 FPS (Low power)"),
            new ComboItem<int>(120, "120 FPS (Recommended)"),
            new ComboItem<int>(240, "240 FPS (Ultra smooth)")
        });
        AddControlRow(renderRows, ref rRow, "Render frame rate", "High-resolution DirectComposition refresh target", _fpsCombo);

        AddSectionHeader(page4, ref p4Row, "Windows & Startup");
        TableLayoutPanel winRows = AddCard(page4, ref p4Row);
        int wRow = 0;
        _primaryOnlyToggle = AddToggleRow(winRows, ref wRow, "Primary monitor only", "Restrict roaming to your main display instead of all monitors");
        AddDivider(winRows, ref wRow);
        _ignoreMaxToggle = AddToggleRow(winRows, ref wRow, "Ignore maximized windows", "Walk cleanly along taskbar and floating popups instead of window bottoms");
        AddDivider(winRows, ref wRow);
        _startupToggle = AddToggleRow(winRows, ref wRow, "Launch automatically with Windows", "Start minimized to system tray when you sign in");
        AddDivider(winRows, ref wRow);

        _ignoredProcessesBox = new TextBox
        {
            Multiline = true,
            AcceptsReturn = true,
            ScrollBars = ScrollBars.Vertical,
            Font = Font
        };
        Control ignHost = FluentUi.WrapTextBox(_ignoredProcessesBox, _palette, new Padding(0, 6, 0, 2), height: 74);
        ignHost.Dock = DockStyle.Top;
        AddStackedControlRow(winRows, ref wRow, "Ignored applications (No collisions / No fullscreen pause)", "Comma or newline separated process names (e.g. spotify, discord, obs64)", ignHost);

        // ===========================
        // Page 5: Themes & Hotkeys
        // ===========================
        TableLayoutPanel page5 = CreatePage(contentHost, "Themes & Hotkeys", "Switch built-in sprite styles, import custom packs, and bind your global pause shortcut.");
        int p5Row = 1;

        AddSectionHeader(page5, ref p5Row, "Theme Studio");
        TableLayoutPanel themeRows = AddCard(page5, ref p5Row);

        var themeSplit = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2
        };
        themeSplit.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        themeSplit.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var themeLeft = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            Margin = new Padding(0, 0, 14, 0)
        };
        themeLeft.Controls.Add(new Label
        {
            Text = "Active sprite pack",
            Font = _rowTitleFont,
            ForeColor = _palette.TextPrimary,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 6)
        }, 0, 0);

        _themeCombo = CreateStyledComboBox(ComboBoxStyle.DropDownList, 280);
        _themeCombo.Dock = DockStyle.Top;
        _themeCombo.Margin = new Padding(0, 0, 0, 10);
        _themeCombo.SelectedIndexChanged += (_, _) => RefreshThemePreview();
        themeLeft.Controls.Add(_themeCombo, 0, 1);

        var themeActions = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Margin = new Padding(0, 0, 0, 8)
        };
        var importBtn = new FluentButton(_palette, primary: true)
        {
            Text = "Import GIF / Aseprite...",
            AutoSize = true,
            MinimumSize = new Size(182, 32),
            Margin = new Padding(0, 0, 8, 4)
        };
        importBtn.Click += OnImportGifOrAsepriteTheme;
        var createSampleBtn = CreateSecondaryButton("Create Sample", 118);
        createSampleBtn.Margin = new Padding(0, 0, 8, 4);
        createSampleBtn.Click += OnCreateSampleTheme;
        var openThemesBtn = CreateSecondaryButton("Open Folder", 104);
        openThemesBtn.Margin = new Padding(0, 0, 0, 4);
        openThemesBtn.Click += OnOpenThemesFolder;

        themeActions.Controls.Add(importBtn);
        themeActions.Controls.Add(createSampleBtn);
        themeActions.Controls.Add(openThemesBtn);
        themeLeft.Controls.Add(themeActions, 0, 2);

        themeLeft.Controls.Add(new Label
        {
            Text = $"Custom packs folder:\r\n{ThemeDiscovery.UserThemesDirectory}",
            Font = _rowDescFont,
            ForeColor = _palette.TextSecondary,
            AutoSize = true,
            MaximumSize = new Size(440, 0),
            Margin = new Padding(0, 2, 0, 0)
        }, 0, 3);

        var previewStage = new Panel
        {
            Size = new Size(92, 110),
            Padding = new Padding(4),
            Margin = new Padding(0),
            BackColor = _palette.CardBg
        };
        _previewPictureBox = new PictureBox
        {
            Size = new Size(84, 84),
            Location = new Point(4, 2),
            BackColor = Color.FromArgb(24, 30, 44),
            BorderStyle = BorderStyle.FixedSingle,
            SizeMode = PictureBoxSizeMode.CenterImage
        };
        _previewCaptionLabel = new Label
        {
            Text = "Preview",
            Font = _rowDescFont,
            ForeColor = _palette.TextSecondary,
            Location = new Point(4, 90),
            Size = new Size(84, 18),
            TextAlign = ContentAlignment.MiddleCenter
        };
        previewStage.Controls.Add(_previewPictureBox);
        previewStage.Controls.Add(_previewCaptionLabel);

        themeSplit.Controls.Add(themeLeft, 0, 0);
        themeSplit.Controls.Add(previewStage, 1, 0);
        themeRows.Controls.Add(themeSplit, 0, 0);

        AddSectionHeader(page5, ref p5Row, "Global Pause Shortcut");
        TableLayoutPanel hotkeyRows = AddCard(page5, ref p5Row);
        int hkRow = 0;
        _enableHotkeyToggle = AddToggleRow(hotkeyRows, ref hkRow, "Enable global pause / resume shortcut", "Works system-wide from any app");
        AddDivider(hotkeyRows, ref hkRow);

        _hotkeyCombo = CreateStyledComboBox(ComboBoxStyle.DropDown, 175);
        foreach (string preset in GlobalHotkeyParser.Presets)
        {
            _hotkeyCombo.Items.Add(preset);
        }
        _hotkeyStatusLabel = new Label
        {
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Font = _rowDescFont,
            Margin = new Padding(10, 5, 0, 0)
        };
        var hotkeyRight = CreateInlinePair(_hotkeyCombo, _hotkeyStatusLabel);
        AddControlRow(hotkeyRows, ref hkRow, "Key combination", "Requires Modifier + Key (e.g. Ctrl+Alt+P)", hotkeyRight);

        _enableHotkeyToggle.CheckedChanged += (_, _) =>
        {
            _hotkeyCombo.Enabled = _enableHotkeyToggle.Checked;
            UpdateHotkeyStatusLabel();
        };
        _hotkeyCombo.TextChanged += (_, _) => UpdateHotkeyStatusLabel();

        // Populate sidebar navigation items
        AddNavItem(navList, 0, FluentNavIcon.Penguins, "Penguins & Motion", (Panel)page1.Parent!);
        AddNavItem(navList, 1, FluentNavIcon.Toys, "Mouse, Toys & Snow", (Panel)page2.Parent!);
        AddNavItem(navList, 2, FluentNavIcon.Audio, "Audio & Ambient", (Panel)page3.Parent!);
        AddNavItem(navList, 3, FluentNavIcon.Display, "Display & Monitors", (Panel)page4.Parent!);
        AddNavItem(navList, 4, FluentNavIcon.Themes, "Themes & Hotkeys", (Panel)page5.Parent!);
        SelectPage(0);

        // Bottom footer action bar
        var footerPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Height = 58,
            Padding = new Padding(18, 12, 22, 12),
            BackColor = _palette.SidebarBg
        };
        footerPanel.Paint += (_, e) =>
        {
            using var pen = new Pen(_palette.Divider, 1f);
            e.Graphics.DrawLine(pen, 0, 0, footerPanel.Width, 0);
        };

        var buttonBar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            BackColor = _palette.SidebarBg
        };
        buttonBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        buttonBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        buttonBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        buttonBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var resetBtn = CreateSecondaryButton("Reset Defaults", 122);
        resetBtn.Anchor = AnchorStyles.Left;
        resetBtn.Click += (_, _) => LoadFromSettings(new AppSettings());

        var okBtn = new FluentButton(_palette, primary: true)
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Size = new Size(92, 32),
            Margin = new Padding(8, 0, 0, 0)
        };
        okBtn.Click += (_, _) => ApplyCurrent();

        var cancelBtn = CreateSecondaryButton("Cancel", 92);
        cancelBtn.DialogResult = DialogResult.Cancel;
        cancelBtn.Margin = new Padding(8, 0, 0, 0);

        var applyBtn = CreateSecondaryButton("Apply", 92);
        applyBtn.Margin = new Padding(8, 0, 0, 0);
        applyBtn.Click += (_, _) =>
        {
            ApplyCurrent();
            _applied = true;
        };

        buttonBar.Controls.Add(resetBtn, 0, 0);
        buttonBar.Controls.Add(okBtn, 1, 0);
        buttonBar.Controls.Add(cancelBtn, 2, 0);
        buttonBar.Controls.Add(applyBtn, 3, 0);
        footerPanel.Controls.Add(buttonBar);

        rootLayout.Controls.Add(mainSplit, 0, 0);
        rootLayout.Controls.Add(footerPanel, 0, 1);

        Controls.Add(rootLayout);
        AcceptButton = okBtn;
        CancelButton = cancelBtn;

        _previewTimer = new System.Windows.Forms.Timer { Interval = 95 };
        _previewTimer.Tick += OnPreviewTimerTick;

        PopulateThemeList(currentSettings.ThemeName);
        LoadFromSettings(currentSettings);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _previewTimer.Stop();
        if (DialogResult == DialogResult.Cancel && _applied)
        {
            _applied = false;
            _applyCallback(_original.Clone());
        }

        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _previewTimer.Stop();
            _previewTimer.Dispose();
            _previewPictureBox.Image = null;
            ClearPreviewFrames();
            _titleFont.Dispose();
            _sectionFont.Dispose();
            _rowTitleFont.Dispose();
            _rowDescFont.Dispose();
            _brandTitleFont.Dispose();
            _badgeFont.Dispose();
        }

        base.Dispose(disposing);
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
            EnableIdlePauses = _idlePausesToggle.Checked,
            EnableParachutes = _parachutesToggle.Checked,
            NeverSplat = _neverSplatToggle.Checked,
            SplatDistance = _splatSlider.Value,
            ThemeName = GetSelectedThemeName(),
            ScaleMode = ((ComboItem<SpriteScaleMode>)_scaleCombo.SelectedItem!).Value,
            LayerMode = ((ComboItem<OverlayLayerMode>)_layerCombo.SelectedItem!).Value,
            TargetFps = ((ComboItem<int>)_fpsCombo.SelectedItem!).Value,
            InteractiveMouse = _interactiveMouseToggle.Checked,
            CursorAvoidance = _cursorAvoidToggle.Checked,
            CursorPerch = _cursorPerchToggle.Checked,
            EnableSnow = _snowToggle.Checked,
            EnableMusicDancing = _musicDanceToggle.Checked,
            IgnoreMaximizedWindows = _ignoreMaxToggle.Checked,
            HideInFullscreen = _hideFullscreenToggle.Checked,
            PauseOnBatterySaver = _pauseBatterySaverToggle.Checked,
            PauseOnPresentationOrFocus = _pauseFocusToggle.Checked,
            PauseWhenRecording = _pauseRecordingToggle.Checked,
            RecordingProcesses = _recordingProcBox.Text.Trim(),
            PrimaryMonitorOnly = _primaryOnlyToggle.Checked,
            LaunchAtStartup = _startupToggle.Checked,
            SoundEnabled = _soundEnabledToggle.Checked,
            EnableActionSounds = _actionSoundsToggle.Checked,
            SoundVolume = _volumeSlider.Value,
            BuiltInSound = GetSelectedBuiltInSound(),
            CustomSoundPath = _customSoundBox.Text.Trim(),
            EnablePauseHotkey = _enableHotkeyToggle.Checked,
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
        _idlePausesToggle.Checked = copy.EnableIdlePauses;
        _parachutesToggle.Checked = copy.EnableParachutes;
        _neverSplatToggle.Checked = copy.NeverSplat;
        _splatSlider.Value = Math.Clamp(copy.SplatDistance, _splatSlider.Minimum, _splatSlider.Maximum);
        _splatSlider.Enabled = !copy.NeverSplat;

        _interactiveMouseToggle.Checked = copy.InteractiveMouse;
        _cursorPerchToggle.Checked = copy.CursorPerch;
        _cursorAvoidToggle.Checked = copy.CursorAvoidance;
        _snowToggle.Checked = copy.EnableSnow;

        _musicDanceToggle.Checked = copy.EnableMusicDancing;
        _soundEnabledToggle.Checked = copy.SoundEnabled;
        _actionSoundsToggle.Checked = copy.EnableActionSounds;
        _volumeSlider.Value = copy.SoundVolume;
        SelectComboValue(_builtInSoundCombo, copy.BuiltInSound);
        _customSoundBox.Text = copy.CustomSoundPath;
        _hideFullscreenToggle.Checked = copy.HideInFullscreen;
        _pauseFocusToggle.Checked = copy.PauseOnPresentationOrFocus;
        _pauseBatterySaverToggle.Checked = copy.PauseOnBatterySaver;
        _pauseRecordingToggle.Checked = copy.PauseWhenRecording;
        _recordingProcBox.Text = copy.RecordingProcesses;
        _recordingProcBox.Enabled = copy.PauseWhenRecording;

        SelectComboValue(_scaleCombo, copy.ScaleMode);
        SelectComboValue(_layerCombo, copy.LayerMode);
        SelectComboValue(_fpsCombo, copy.TargetFps);
        _primaryOnlyToggle.Checked = copy.PrimaryMonitorOnly;
        _ignoreMaxToggle.Checked = copy.IgnoreMaximizedWindows;
        _startupToggle.Checked = copy.LaunchAtStartup;
        _ignoredProcessesBox.Text = copy.IgnoredProcesses;

        PopulateThemeList(copy.ThemeName);
        _enableHotkeyToggle.Checked = copy.EnablePauseHotkey;
        _hotkeyCombo.Text = copy.PauseHotkey;
        _hotkeyCombo.Enabled = copy.EnablePauseHotkey;

        UpdateHotkeyStatusLabel();
        UpdateAllSliderLabels();
    }

    private void PopulateThemeList(string selectedThemeName)
    {
        _themeCombo.Items.Clear();
        IReadOnlyList<ThemeDescriptor> themes = ThemeDiscovery.ListThemes();
        foreach (ThemeDescriptor theme in themes)
        {
            string label = theme.IsBuiltIn ? $"{theme.Name} (Built-in)" : $"{theme.Name} (Custom Pack)";
            _themeCombo.Items.Add(new ComboItem<string>(theme.Name, label));
        }

        SelectComboValue(_themeCombo, selectedThemeName);
        RefreshThemePreview();
    }

    private string GetSelectedThemeName() =>
        (_themeCombo.SelectedItem as ComboItem<string>)?.Value ?? "Classic";

    private string GetSelectedBuiltInSound() =>
        (_builtInSoundCombo.SelectedItem as ComboItem<string>)?.Value ?? "boom";

    private void UpdateHotkeyStatusLabel()
    {
        if (!_enableHotkeyToggle.Checked)
        {
            _hotkeyStatusLabel.ForeColor = _palette.TextSecondary;
            _hotkeyStatusLabel.Text = "Disabled";
            return;
        }

        if (GlobalHotkeyParser.TryParse(_hotkeyCombo.Text, out _, out _, out string normalized))
        {
            _hotkeyStatusLabel.ForeColor = _palette.SuccessText;
            _hotkeyStatusLabel.Text = $"Valid: {normalized}";
        }
        else
        {
            _hotkeyStatusLabel.ForeColor = _palette.DangerText;
            _hotkeyStatusLabel.Text = "Use Modifier+Key";
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

    private void OnImportGifOrAsepriteTheme(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Import Animated GIF or Aseprite JSON Sprite Sheet",
            Filter = "Animated GIF or Aseprite JSON (*.gif;*.json)|*.gif;*.json|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dlg.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            (string themeName, string folderPath) = SpriteCatalog.ImportThemeFromGifOrAseprite(dlg.FileName);
            PopulateThemeList(themeName);
            MessageBox.Show(
                this,
                $"Imported theme '{themeName}' to:\r\n{folderPath}\r\n\r\nSelect Apply or OK to activate it now!",
                "Theme Imported",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Could not import theme from '{Path.GetFileName(dlg.FileName)}':\r\n{ex.Message}",
                "Import Failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void RefreshThemePreview()
    {
        _previewTimer.Stop();
        _previewPictureBox.Image = null;
        ClearPreviewFrames();

        _previewThemeName = GetSelectedThemeName();
        _previewCaptionLabel.Text = _previewThemeName;

        try
        {
            SpriteCatalog catalog = SpriteCatalog.LoadTheme(_previewThemeName, 1.0);
            if (!catalog.Sheets.TryGetValue(PenguinAnimationId.Walker, out SpriteSheet? walker))
            {
                return;
            }

            bool smoothHd = _previewThemeName.Equals("SmoothHD", StringComparison.OrdinalIgnoreCase);
            for (int i = 0; i < walker.Spec.FrameCount; i++)
            {
                SpriteFrame frame = walker.GetFrame(FacingDirection.Right, i);
                _previewFrames.Add(RenderPreviewCardFrame(frame, smoothHd));
            }

            _previewFrameIndex = 0;
            if (_previewFrames.Count > 0)
            {
                _previewPictureBox.Image = _previewFrames[0];
                if (_previewFrames.Count > 1)
                {
                    _previewTimer.Start();
                }
            }
        }
        catch
        {
            // Keep dialog resilient if a custom pack has incomplete preview assets.
        }
    }

    private void OnPreviewTimerTick(object? sender, EventArgs e)
    {
        if (_previewFrames.Count <= 1 || IsDisposed)
        {
            return;
        }

        _previewFrameIndex = (_previewFrameIndex + 1) % _previewFrames.Count;
        _previewPictureBox.Image = _previewFrames[_previewFrameIndex];
    }

    private void ClearPreviewFrames()
    {
        for (int i = 0; i < _previewFrames.Count; i++)
        {
            _previewFrames[i].Dispose();
        }
        _previewFrames.Clear();
    }

    private static Bitmap RenderPreviewCardFrame(SpriteFrame frame, bool smoothHd)
    {
        const int cardSize = 72;
        var card = new Bitmap(cardSize, cardSize, PixelFormat.Format32bppPArgb);
        using var spriteBmp = new Bitmap(frame.Width, frame.Height, PixelFormat.Format32bppPArgb);
        BitmapData data = spriteBmp.LockBits(
            new Rectangle(0, 0, frame.Width, frame.Height),
            ImageLockMode.WriteOnly,
            PixelFormat.Format32bppPArgb);
        try
        {
            int[] signed = new int[frame.Pixels.Length];
            Buffer.BlockCopy(frame.Pixels, 0, signed, 0, frame.Pixels.Length * 4);
            for (int row = 0; row < frame.Height; row++)
            {
                Marshal.Copy(signed, row * frame.Width, data.Scan0 + (row * data.Stride), frame.Width);
            }
        }
        finally
        {
            spriteBmp.UnlockBits(data);
        }

        using Graphics g = Graphics.FromImage(card);
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = smoothHd ? InterpolationMode.HighQualityBicubic : InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;

        using (var shadowBrush = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
        {
            g.FillEllipse(shadowBrush, 16, 58, 40, 8);
        }

        const int drawSize = 52;
        int offsetX = (cardSize - drawSize) / 2;
        int offsetY = 62 - drawSize;
        g.DrawImage(spriteBmp, new Rectangle(offsetX, offsetY, drawSize, drawSize));
        return card;
    }

    private void AddNavItem(TableLayoutPanel navList, int index, FluentNavIcon icon, string text, Panel pagePanel)
    {
        var btn = new FluentNavButton(_palette, icon, text)
        {
            Dock = DockStyle.Top,
            Margin = new Padding(0, 2, 0, 2)
        };
        btn.Click += (_, _) => SelectPage(index);
        navList.Controls.Add(btn, 0, index);
        _pages.Add((btn, pagePanel));
    }

    private void SelectPage(int activeIndex)
    {
        for (int i = 0; i < _pages.Count; i++)
        {
            bool active = i == activeIndex;
            _pages[i].Button.Selected = active;
            _pages[i].Page.Visible = active;
        }
    }

    private TableLayoutPanel CreatePage(Panel host, string title, string subtitle)
    {
        var scrollHost = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = new Padding(26, 20, 26, 20),
            BackColor = _palette.WindowBg,
            Visible = false
        };

        var stack = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            BackColor = _palette.WindowBg
        };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            Margin = new Padding(0, 0, 0, 12),
            BackColor = _palette.WindowBg
        };
        header.Controls.Add(new Label
        {
            Text = title,
            Font = _titleFont,
            ForeColor = _palette.TextPrimary,
            UseMnemonic = false,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 2)
        }, 0, 0);
        header.Controls.Add(new Label
        {
            Text = subtitle,
            Font = _rowDescFont,
            ForeColor = _palette.TextSecondary,
            UseMnemonic = false,
            AutoSize = true,
            Margin = new Padding(0)
        }, 0, 1);

        stack.Controls.Add(header, 0, 0);
        scrollHost.Controls.Add(stack);
        host.Controls.Add(scrollHost);
        return stack;
    }

    private void AddSectionHeader(TableLayoutPanel page, ref int row, string title)
    {
        var lbl = new Label
        {
            Text = title,
            Font = _sectionFont,
            ForeColor = _palette.TextPrimary,
            UseMnemonic = false,
            AutoSize = true,
            Margin = new Padding(2, 6, 0, 6)
        };
        page.Controls.Add(lbl, 0, row++);
    }

    private TableLayoutPanel AddCard(TableLayoutPanel page, ref int row)
    {
        var card = new FluentCard(_palette)
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Height = 1
        };
        var rows = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Height = 1,
            ColumnCount = 1,
            BackColor = _palette.CardBg
        };
        rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        card.Controls.Add(rows);
        page.Controls.Add(card, 0, row++);
        return rows;
    }

    private void AddDivider(TableLayoutPanel rows, ref int row)
    {
        var line = new Panel
        {
            Height = 1,
            Dock = DockStyle.Top,
            BackColor = _palette.Divider,
            Margin = new Padding(0, 5, 0, 5)
        };
        rows.Controls.Add(line, 0, row++);
    }

    private (FluentSlider Slider, Label ValueLabel) AddSliderRow(
        TableLayoutPanel rows,
        ref int row,
        string title,
        string subtitle,
        int min,
        int max,
        Func<int, string> formatter)
    {
        var rowGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            BackColor = _palette.CardBg,
            Margin = new Padding(0, 2, 0, 2)
        };
        rowGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        rowGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        rowGrid.Controls.Add(CreateTitleStack(title, subtitle), 0, 0);

        var valueLabel = new Label
        {
            Text = formatter(min),
            Font = _badgeFont,
            ForeColor = _palette.Accent,
            AutoSize = true,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(8, 4, 0, 0)
        };
        rowGrid.Controls.Add(valueLabel, 1, 0);

        var slider = new FluentSlider(_palette)
        {
            Minimum = min,
            Maximum = max,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 4, 0, 2)
        };
        slider.ValueChanged += (_, _) => valueLabel.Text = formatter(slider.Value);

        rowGrid.Controls.Add(slider, 0, 1);
        rowGrid.SetColumnSpan(slider, 2);

        rows.Controls.Add(rowGrid, 0, row++);
        return (slider, valueLabel);
    }

    private FluentToggleSwitch AddToggleRow(TableLayoutPanel rows, ref int row, string title, string subtitle)
    {
        var rowGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            BackColor = _palette.CardBg,
            Margin = new Padding(0, 2, 0, 2)
        };
        rowGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        rowGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        TableLayoutPanel titleStack = CreateTitleStack(title, subtitle);
        var toggle = new FluentToggleSwitch(_palette)
        {
            Anchor = AnchorStyles.Right,
            Margin = new Padding(12, 4, 0, 4)
        };

        void ToggleOnLabelClick(object? sender, EventArgs e)
        {
            if (toggle.Enabled)
            {
                toggle.Checked = !toggle.Checked;
            }
        }
        titleStack.Click += ToggleOnLabelClick;
        foreach (Control child in titleStack.Controls)
        {
            child.Click += ToggleOnLabelClick;
        }

        rowGrid.Controls.Add(titleStack, 0, 0);
        rowGrid.Controls.Add(toggle, 1, 0);
        rows.Controls.Add(rowGrid, 0, row++);
        return toggle;
    }

    private void AddControlRow(TableLayoutPanel rows, ref int row, string title, string subtitle, Control rightControl)
    {
        var rowGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            BackColor = _palette.CardBg,
            Margin = new Padding(0, 2, 0, 2)
        };
        rowGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        rowGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        rightControl.Anchor = AnchorStyles.Right;
        rowGrid.Controls.Add(CreateTitleStack(title, subtitle), 0, 0);
        rowGrid.Controls.Add(rightControl, 1, 0);
        rows.Controls.Add(rowGrid, 0, row++);
    }

    private void AddStackedControlRow(TableLayoutPanel rows, ref int row, string title, string subtitle, Control bottomControl)
    {
        var rowGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            BackColor = _palette.CardBg,
            Margin = new Padding(0, 2, 0, 2)
        };
        rowGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        rowGrid.Controls.Add(CreateTitleStack(title, subtitle), 0, 0);
        rowGrid.Controls.Add(bottomControl, 0, 1);
        rows.Controls.Add(rowGrid, 0, row++);
    }

    private TableLayoutPanel CreateTitleStack(string title, string subtitle)
    {
        var stack = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            BackColor = _palette.CardBg,
            Margin = new Padding(0)
        };
        stack.Controls.Add(new Label
        {
            Text = title,
            Font = _rowTitleFont,
            ForeColor = _palette.TextPrimary,
            UseMnemonic = false,
            AutoSize = true,
            Margin = new Padding(0, 1, 0, 1)
        }, 0, 0);
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            stack.Controls.Add(new Label
            {
                Text = subtitle,
                Font = _rowDescFont,
                ForeColor = _palette.TextSecondary,
                UseMnemonic = false,
                AutoSize = true,
                MaximumSize = new Size(360, 0),
                Margin = new Padding(0, 0, 0, 2)
            }, 0, 1);
        }
        return stack;
    }

    private ComboBox CreateStyledComboBox(ComboBoxStyle DropDownStyle, int width)
    {
        var combo = new ComboBox
        {
            DropDownStyle = DropDownStyle,
            Width = width,
            Font = Font
        };
        FluentUi.StyleComboBox(combo, _palette);
        return combo;
    }

    private FluentButton CreateSecondaryButton(string text, int minWidth) =>
        new(_palette, primary: false)
        {
            Text = text,
            AutoSize = true,
            MinimumSize = new Size(minWidth, 30)
        };

    private FluentButton CreateQuickActionButton(string text, PenguinQuickAction action)
    {
        var btn = new FluentButton(_palette, primary: false)
        {
            Text = text,
            AutoSize = true,
            MinimumSize = new Size(94, 30),
            Margin = new Padding(0, 0, 8, 2),
            Enabled = _quickActionCallback is not null
        };
        btn.Click += (_, _) => _quickActionCallback?.Invoke(action);
        return btn;
    }

    private static Control CreateInlinePair(Control first, Control second)
    {
        var panel = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0)
        };
        first.Margin = new Padding(0, 2, 8, 0);
        second.Margin = new Padding(0);
        panel.Controls.Add(first);
        panel.Controls.Add(second);
        return panel;
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

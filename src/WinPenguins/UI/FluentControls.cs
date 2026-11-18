using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using Microsoft.Win32;
using WinPenguins.Platform;

namespace WinPenguins.UI;

internal enum FluentNavIcon
{
    Penguins,
    Toys,
    Audio,
    Display,
    Themes
}

internal sealed class FluentPalette
{
    public bool IsDark { get; private set; }
    public Color WindowBg { get; private set; }
    public Color SidebarBg { get; private set; }
    public Color CardBg { get; private set; }
    public Color CardBorder { get; private set; }
    public Color Divider { get; private set; }
    public Color NavHover { get; private set; }
    public Color NavSelected { get; private set; }
    public Color ControlBg { get; private set; }
    public Color ControlHover { get; private set; }
    public Color ControlBorder { get; private set; }
    public Color Accent { get; private set; }
    public Color AccentHover { get; private set; }
    public Color AccentText { get; private set; }
    public Color TextPrimary { get; private set; }
    public Color TextSecondary { get; private set; }
    public Color SuccessText { get; private set; }
    public Color DangerText { get; private set; }

    private FluentPalette(bool isDark)
    {
        ApplyMode(isDark);
    }

    public static FluentPalette DetectCurrent() => new(IsSystemDarkMode());

    internal static FluentPalette CreateForTheme(bool isDark) => new(isDark);

    public bool RefreshFromSystem()
    {
        bool dark = IsSystemDarkMode();
        if (dark == IsDark)
        {
            return false;
        }

        ApplyMode(dark);
        return true;
    }

    private void ApplyMode(bool isDark)
    {
        IsDark = isDark;
        if (isDark)
        {
            WindowBg = Color.FromArgb(32, 32, 32);
            SidebarBg = Color.FromArgb(26, 26, 26);
            CardBg = Color.FromArgb(43, 43, 43);
            CardBorder = Color.FromArgb(58, 58, 58);
            Divider = Color.FromArgb(56, 56, 56);
            NavHover = Color.FromArgb(46, 46, 46);
            NavSelected = Color.FromArgb(52, 52, 52);
            ControlBg = Color.FromArgb(52, 52, 52);
            ControlHover = Color.FromArgb(64, 64, 64);
            ControlBorder = Color.FromArgb(78, 78, 78);
            Accent = Color.FromArgb(96, 205, 255);
            AccentHover = Color.FromArgb(124, 214, 255);
            AccentText = Color.FromArgb(12, 18, 24);
            TextPrimary = Color.FromArgb(250, 250, 250);
            TextSecondary = Color.FromArgb(178, 182, 190);
            SuccessText = Color.FromArgb(108, 203, 95);
            DangerText = Color.FromArgb(255, 138, 128);
        }
        else
        {
            WindowBg = Color.FromArgb(243, 243, 243);
            SidebarBg = Color.FromArgb(236, 236, 236);
            CardBg = Color.FromArgb(255, 255, 255);
            CardBorder = Color.FromArgb(226, 226, 226);
            Divider = Color.FromArgb(234, 234, 234);
            NavHover = Color.FromArgb(228, 228, 228);
            NavSelected = Color.FromArgb(220, 222, 226);
            ControlBg = Color.FromArgb(255, 255, 255);
            ControlHover = Color.FromArgb(246, 246, 246);
            ControlBorder = Color.FromArgb(204, 204, 204);
            Accent = Color.FromArgb(0, 103, 192);
            AccentHover = Color.FromArgb(22, 118, 204);
            AccentText = Color.White;
            TextPrimary = Color.FromArgb(26, 26, 26);
            TextSecondary = Color.FromArgb(94, 98, 106);
            SuccessText = Color.FromArgb(15, 123, 15);
            DangerText = Color.FromArgb(196, 43, 28);
        }
    }

    private static bool IsSystemDarkMode()
    {
        try
        {
            object? val = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme",
                1);
            if (val is int intVal)
            {
                return intVal == 0;
            }
        }
        catch
        {
            // Fall back to WinForms system colors.
        }

        return SystemColors.Window.GetBrightness() < 0.5f;
    }
}

internal static class FluentUi
{
    public static void ApplyWindowChrome(Form form, FluentPalette palette)
    {
        form.BackColor = palette.WindowBg;
        form.ForeColor = palette.TextPrimary;

        void ApplyDwm()
        {
            if (!form.IsHandleCreated) return;
            try
            {
                int dark = palette.IsDark ? 1 : 0;
                _ = NativeMethods.DwmSetWindowAttribute(form.Handle, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
                int round = NativeMethods.DWMWCP_ROUND;
                _ = NativeMethods.DwmSetWindowAttribute(form.Handle, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
            }
            catch
            {
                // Non-DWM / downlevel OS fallback.
            }
        }

        form.HandleCreated += (_, _) => ApplyDwm();
        if (form.IsHandleCreated)
        {
            ApplyDwm();
        }
    }

    public static void ApplyDwmOnly(Form form, FluentPalette palette)
    {
        form.BackColor = palette.WindowBg;
        form.ForeColor = palette.TextPrimary;
        if (!form.IsHandleCreated) return;
        try
        {
            int dark = palette.IsDark ? 1 : 0;
            _ = NativeMethods.DwmSetWindowAttribute(form.Handle, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        }
        catch
        {
        }
    }

    public static GraphicsPath CreateRoundRect(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Max(1f, radius * 2f);
        if (d > bounds.Width) d = bounds.Width;
        if (d > bounds.Height) d = bounds.Height;

        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static Control WrapTextBox(TextBox box, FluentPalette palette, Padding? margin = null, int height = 0)
    {
        box.BorderStyle = BorderStyle.None;
        box.BackColor = palette.ControlBg;
        box.ForeColor = palette.TextPrimary;

        bool multiline = height > 0;
        var host = new FluentInputHost(palette, box)
        {
            Margin = margin ?? new Padding(0),
            Padding = multiline ? new Padding(8, 6, 8, 6) : new Padding(9, 6, 9, 5),
            Height = multiline ? height : 30,
            Dock = DockStyle.Fill
        };
        if (multiline)
        {
            box.Dock = DockStyle.Fill;
        }
        else
        {
            box.Dock = DockStyle.Top;
        }

        host.Controls.Add(box);
        return host;
    }

    private sealed class FluentInputHost : Panel
    {
        private readonly FluentPalette _palette;
        private readonly TextBox _box;
        private bool _focused;

        public FluentInputHost(FluentPalette palette, TextBox box)
        {
            _palette = palette;
            _box = box;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = palette.WindowBg;
            _box.GotFocus += (_, _) => { _focused = true; Invalidate(); };
            _box.LostFocus += (_, _) => { _focused = false; Invalidate(); };
            _box.EnabledChanged += (_, _) =>
            {
                _box.BackColor = _box.Enabled ? _palette.ControlBg : _palette.CardBg;
                Invalidate();
            };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Color fill = _box.Enabled ? _palette.ControlBg : _palette.CardBg;
            _box.BackColor = fill;
            _box.ForeColor = _box.Enabled ? _palette.TextPrimary : _palette.TextSecondary;
            using GraphicsPath path = CreateRoundRect(rect, 5f);
            using var brush = new SolidBrush(fill);
            using var pen = new Pen(_focused ? _palette.Accent : _palette.ControlBorder, _focused ? 1.5f : 1f);
            g.FillPath(brush, path);
            g.DrawPath(pen, path);
        }
    }
}

internal sealed class FluentComboBox : ComboBox
{
    private readonly FluentPalette _palette;

    public FluentComboBox(FluentPalette palette)
    {
        _palette = palette;
        FlatStyle = FlatStyle.Flat;
        DrawMode = DrawMode.OwnerDrawFixed;
        ItemHeight = 24;
        BackColor = palette.ControlBg;
        ForeColor = palette.TextPrimary;
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        Graphics g = e.Graphics;
        bool editBox = (e.State & DrawItemState.ComboBoxEdit) != DrawItemState.None;
        bool selected = (e.State & DrawItemState.Selected) != DrawItemState.None && !editBox;
        bool disabled = (e.State & DrawItemState.Disabled) != DrawItemState.None || !Enabled;

        Color bg = selected
            ? _palette.NavSelected
            : (disabled ? _palette.CardBg : _palette.ControlBg);
        Color fg = disabled ? _palette.TextSecondary : _palette.TextPrimary;

        using (var bgBrush = new SolidBrush(bg))
        {
            g.FillRectangle(bgBrush, e.Bounds);
        }

        if (selected)
        {
            using var pillBrush = new SolidBrush(_palette.Accent);
            g.FillRectangle(pillBrush, e.Bounds.X + 2, e.Bounds.Y + 4, 3, Math.Max(4, e.Bounds.Height - 8));
        }

        if (e.Index >= 0 && e.Index < Items.Count)
        {
            string text = GetItemText(Items[e.Index]) ?? string.Empty;
            var textRect = new Rectangle(e.Bounds.X + 8, e.Bounds.Y, Math.Max(0, e.Bounds.Width - 10), e.Bounds.Height);
            TextRenderer.DrawText(
                g,
                text,
                Font,
                textRect,
                fg,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    public void SyncColors()
    {
        BackColor = _palette.ControlBg;
        ForeColor = _palette.TextPrimary;
        Invalidate();
    }
}

internal sealed class FluentChipButton : Control
{
    private readonly FluentPalette _palette;
    private bool _checked;
    private bool _hovered;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? CheckedChanged;

    public FluentChipButton(FluentPalette palette, string text)
    {
        _palette = palette;
        Text = text;
        Font = new Font("Segoe UI Semibold", 8.75f);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
        Size = new Size(78, 28);
        MinimumSize = new Size(68, 28);
        Cursor = Cursors.Hand;
        TabStop = true;
    }

    protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hovered = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnClick(EventArgs e)
    {
        if (Enabled)
        {
            Checked = !Checked;
        }
        base.OnClick(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (Enabled && e.KeyCode is Keys.Space or Keys.Enter)
        {
            Checked = !Checked;
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? _palette.CardBg);

        var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using GraphicsPath path = FluentUi.CreateRoundRect(rect, 13f);
        Color bg = Checked
            ? (_hovered ? _palette.AccentHover : _palette.Accent)
            : (_hovered ? _palette.ControlHover : _palette.ControlBg);
        Color fg = Checked ? _palette.AccentText : _palette.TextPrimary;
        Color border = Checked ? bg : _palette.ControlBorder;

        using (var fill = new SolidBrush(bg))
        using (var pen = new Pen(border, 1f))
        {
            g.FillPath(fill, path);
            g.DrawPath(pen, path);
        }

        TextRenderer.DrawText(
            g,
            Text,
            Font,
            ClientRectangle,
            fg,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}

internal sealed class FluentCard : Panel
{
    private readonly FluentPalette _palette;

    protected override Size DefaultSize => new(200, 32);

    public FluentCard(FluentPalette palette, Padding? padding = null, Padding? margin = null)
    {
        _palette = palette;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = palette.WindowBg;
        Padding = padding ?? new Padding(16, 10, 16, 10);
        Margin = margin ?? new Padding(0, 0, 0, 10);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using GraphicsPath path = FluentUi.CreateRoundRect(rect, 8f);
        using var fill = new SolidBrush(_palette.CardBg);
        using var border = new Pen(_palette.CardBorder, 1f);
        g.FillPath(fill, path);
        g.DrawPath(border, path);
    }
}

internal sealed class FluentToggleSwitch : Control
{
    private readonly FluentPalette _palette;
    private bool _checked;
    private bool _hovered;
    private bool _pressed;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? CheckedChanged;

    public FluentToggleSwitch(FluentPalette palette)
    {
        _palette = palette;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);
        Size = new Size(44, 24);
        MinimumSize = Size;
        MaximumSize = Size;
        Cursor = Cursors.Hand;
        TabStop = true;
    }

    protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hovered = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (Enabled && e.Button == MouseButtons.Left)
        {
            Focus();
            _pressed = true;
            Invalidate();
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (Enabled && _pressed && e.Button == MouseButtons.Left)
        {
            _pressed = false;
            if (ClientRectangle.Contains(e.Location))
            {
                Checked = !Checked;
            }
            Invalidate();
        }
        base.OnMouseUp(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (Enabled && e.KeyCode is Keys.Space or Keys.Enter)
        {
            Checked = !Checked;
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? _palette.CardBg);

        var track = new RectangleF(2f, 2f, Width - 4f, Height - 4f);
        using GraphicsPath trackPath = FluentUi.CreateRoundRect(track, track.Height / 2f);

        if (Checked)
        {
            Color fill = !Enabled
                ? _palette.ControlBorder
                : (_hovered ? _palette.AccentHover : _palette.Accent);
            using var brush = new SolidBrush(fill);
            g.FillPath(brush, trackPath);
        }
        else
        {
            Color borderColor = !Enabled ? _palette.ControlBorder : _palette.TextSecondary;
            if (_hovered && Enabled)
            {
                using var hoverBg = new SolidBrush(Color.FromArgb(18, _palette.TextPrimary));
                g.FillPath(hoverBg, trackPath);
            }
            using var pen = new Pen(borderColor, 1.4f);
            g.DrawPath(pen, trackPath);
        }

        float knobSize = _pressed ? 14f : (_hovered && Enabled ? 13.5f : 12f);
        float margin = (track.Height - knobSize) / 2f;
        float knobX = Checked ? track.Right - margin - knobSize : track.Left + margin;
        float knobY = track.Top + margin;

        Color knobColor = Checked
            ? (Enabled ? _palette.AccentText : _palette.CardBg)
            : (Enabled ? _palette.TextSecondary : _palette.ControlBorder);
        using var knobBrush = new SolidBrush(knobColor);
        g.FillEllipse(knobBrush, knobX, knobY, knobSize, knobSize);

        if (Focused)
        {
            using var focusPen = new Pen(_palette.Accent, 1f) { DashStyle = DashStyle.Dot };
            using GraphicsPath focusPath = FluentUi.CreateRoundRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 11f);
            g.DrawPath(focusPen, focusPath);
        }
    }
}

internal sealed class FluentSlider : Control
{
    private readonly FluentPalette _palette;
    private int _minimum;
    private int _maximum = 100;
    private int _value;
    private int _smallChange = 1;
    private bool _hovered;
    private bool _dragging;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Minimum
    {
        get => _minimum;
        set { _minimum = value; if (_maximum < _minimum) _maximum = _minimum; Value = _value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Maximum
    {
        get => _maximum;
        set { _maximum = Math.Max(_minimum, value); Value = _value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SmallChange
    {
        get => _smallChange;
        set => _smallChange = Math.Max(1, value);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Value
    {
        get => _value;
        set
        {
            int clamped = Math.Clamp(value, _minimum, _maximum);
            if (_value == clamped) return;
            _value = clamped;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? ValueChanged;

    public FluentSlider(FluentPalette palette)
    {
        _palette = palette;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable | ControlStyles.ResizeRedraw, true);
        Height = 22;
        MinimumSize = new Size(100, 22);
        Cursor = Cursors.Hand;
        TabStop = true;
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown ||
        base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!Enabled)
        {
            base.OnKeyDown(e);
            return;
        }

        int step = Math.Max(1, (_maximum - _minimum) / 10);
        switch (e.KeyCode)
        {
            case Keys.Left or Keys.Down:
                Value -= _smallChange;
                e.Handled = true;
                break;
            case Keys.Right or Keys.Up:
                Value += _smallChange;
                e.Handled = true;
                break;
            case Keys.PageDown:
                Value -= step;
                e.Handled = true;
                break;
            case Keys.PageUp:
                Value += step;
                e.Handled = true;
                break;
            case Keys.Home:
                Value = _minimum;
                e.Handled = true;
                break;
            case Keys.End:
                Value = _maximum;
                e.Handled = true;
                break;
        }

        base.OnKeyDown(e);
    }

    protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hovered = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (Enabled && e.Button == MouseButtons.Left)
        {
            Focus();
            _dragging = true;
            SetValueFromX(e.X);
            Invalidate();
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (Enabled && _dragging)
        {
            SetValueFromX(e.X);
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_dragging)
        {
            _dragging = false;
            Invalidate();
        }
        base.OnMouseUp(e);
    }

    private void SetValueFromX(int mouseX)
    {
        const float pad = 10f;
        float span = Math.Max(1f, Width - pad * 2f);
        double t = Math.Clamp((mouseX - pad) / span, 0.0, 1.0);
        Value = _minimum + (int)Math.Round(t * (_maximum - _minimum));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? _palette.CardBg);

        const float pad = 10f;
        float trackW = Math.Max(4f, Width - pad * 2f);
        float cy = Height / 2f;
        float frac = _maximum > _minimum ? (float)(_value - _minimum) / (_maximum - _minimum) : 0f;
        float thumbX = pad + frac * trackW;

        var fullTrack = new RectangleF(pad, cy - 2f, trackW, 4f);
        using (GraphicsPath bgPath = FluentUi.CreateRoundRect(fullTrack, 2f))
        using (var bgBrush = new SolidBrush(_palette.ControlBorder))
        {
            g.FillPath(bgBrush, bgPath);
        }

        if (frac > 0.002f)
        {
            var activeTrack = new RectangleF(pad, cy - 2f, Math.Max(4f, thumbX - pad), 4f);
            using GraphicsPath actPath = FluentUi.CreateRoundRect(activeTrack, 2f);
            using var actBrush = new SolidBrush(Enabled ? _palette.Accent : _palette.ControlBorder);
            g.FillPath(actBrush, actPath);
        }

        const float outerDiameter = 18f;
        using (var outerBrush = new SolidBrush(_palette.ControlBg))
        using (var outerPen = new Pen(_palette.ControlBorder, 1.1f))
        {
            g.FillEllipse(outerBrush, thumbX - outerDiameter / 2f, cy - outerDiameter / 2f, outerDiameter, outerDiameter);
            g.DrawEllipse(outerPen, thumbX - outerDiameter / 2f, cy - outerDiameter / 2f, outerDiameter, outerDiameter);
        }

        float innerDiameter = _dragging ? 8f : (_hovered && Enabled ? 11.5f : 10f);
        Color innerColor = Enabled
            ? (_hovered ? _palette.AccentHover : _palette.Accent)
            : _palette.ControlBorder;
        using (var innerBrush = new SolidBrush(innerColor))
        {
            g.FillEllipse(innerBrush, thumbX - innerDiameter / 2f, cy - innerDiameter / 2f, innerDiameter, innerDiameter);
        }

        if (Focused)
        {
            using var focusPen = new Pen(_palette.Accent, 1f) { DashStyle = DashStyle.Dot };
            g.DrawRectangle(focusPen, 1, 1, Width - 3, Height - 3);
        }
    }
}

internal sealed class FluentButton : Button
{
    private readonly FluentPalette _palette;
    private readonly bool _primary;
    private bool _hovered;
    private bool _pressed;

    public FluentButton(FluentPalette palette, bool primary = false)
    {
        _palette = palette;
        _primary = primary;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        Font = new Font("Segoe UI Semibold", 9.25f);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        Size text = TextRenderer.MeasureText(Text ?? string.Empty, Font);
        int w = Math.Max(MinimumSize.Width, text.Width + 24);
        int h = Math.Max(MinimumSize.Height, Math.Max(30, text.Height + 12));
        return new Size(w, h);
    }

    protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hovered = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        if (Enabled && mevent.Button == MouseButtons.Left) { _pressed = true; Invalidate(); }
        base.OnMouseDown(mevent);
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(mevent);
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        Graphics g = pevent.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? _palette.WindowBg);

        var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using GraphicsPath path = FluentUi.CreateRoundRect(rect, 5.5f);

        Color bg;
        Color fg;
        Color border;

        if (_primary)
        {
            bg = !Enabled
                ? _palette.ControlBorder
                : (_pressed ? _palette.Accent : (_hovered ? _palette.AccentHover : _palette.Accent));
            fg = Enabled ? _palette.AccentText : _palette.TextSecondary;
            border = bg;
        }
        else
        {
            bg = !Enabled
                ? _palette.CardBg
                : (_pressed ? _palette.NavSelected : (_hovered ? _palette.ControlHover : _palette.ControlBg));
            fg = Enabled ? _palette.TextPrimary : _palette.TextSecondary;
            border = _palette.ControlBorder;
        }

        using (var brush = new SolidBrush(bg))
        using (var pen = new Pen(border, 1f))
        {
            g.FillPath(brush, path);
            g.DrawPath(pen, path);
        }

        TextRenderer.DrawText(
            g,
            Text,
            Font,
            ClientRectangle,
            fg,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        if (Focused)
        {
            using var focusPen = new Pen(_primary ? _palette.AccentText : _palette.Accent, 1f) { DashStyle = DashStyle.Dot };
            using GraphicsPath focusPath = FluentUi.CreateRoundRect(new RectangleF(3f, 3f, Width - 7f, Height - 7f), 4f);
            g.DrawPath(focusPen, focusPath);
        }
    }
}

internal sealed class FluentNavButton : Control
{
    private readonly FluentPalette _palette;
    private readonly FluentNavIcon _icon;
    private bool _selected;
    private bool _hovered;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            Invalidate();
        }
    }

    public FluentNavButton(FluentPalette palette, FluentNavIcon icon, string text)
    {
        _palette = palette;
        _icon = icon;
        Text = text;
        Font = new Font("Segoe UI Semibold", 9.5f);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
        Height = 40;
        Cursor = Cursors.Hand;
        TabStop = true;
    }

    protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hovered = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Enter or Keys.Space)
        {
            OnClick(EventArgs.Empty);
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? _palette.SidebarBg);

        if (_selected || _hovered)
        {
            var bgRect = new RectangleF(2f, 2f, Width - 4f, Height - 4f);
            using GraphicsPath bgPath = FluentUi.CreateRoundRect(bgRect, 6f);
            using var bgBrush = new SolidBrush(_selected ? _palette.NavSelected : _palette.NavHover);
            g.FillPath(bgBrush, bgPath);
        }

        if (_selected)
        {
            var pill = new RectangleF(3f, (Height - 16f) / 2f, 3.2f, 16f);
            using GraphicsPath pillPath = FluentUi.CreateRoundRect(pill, 1.6f);
            using var accentBrush = new SolidBrush(_palette.Accent);
            g.FillPath(accentBrush, pillPath);
        }

        Color iconColor = _selected ? _palette.Accent : _palette.TextSecondary;
        DrawVectorIcon(g, _icon, iconColor, new RectangleF(15f, (Height - 16f) / 2f, 16f, 16f));

        Color textColor = _selected ? _palette.TextPrimary : (_hovered ? _palette.TextPrimary : _palette.TextSecondary);
        var textBounds = new Rectangle(40, 0, Width - 46, Height);
        TextRenderer.DrawText(
            g,
            Text,
            Font,
            textBounds,
            textColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    private static void DrawVectorIcon(Graphics g, FluentNavIcon icon, Color color, RectangleF box)
    {
        using var pen = new Pen(color, 1.5f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var fill = new SolidBrush(color);

        float x = box.X;
        float y = box.Y;

        switch (icon)
        {
            case FluentNavIcon.Penguins:
                g.DrawEllipse(pen, x + 3f, y + 1f, 10f, 13f);
                g.FillEllipse(fill, x + 6f, y + 4.5f, 1.6f, 1.6f);
                g.FillEllipse(fill, x + 8.6f, y + 4.5f, 1.6f, 1.6f);
                g.DrawLine(pen, x + 6.5f, y + 8f, x + 9.5f, y + 8f);
                g.DrawLine(pen, x + 4.5f, y + 14.5f, x + 11.5f, y + 14.5f);
                break;

            case FluentNavIcon.Toys:
                g.DrawEllipse(pen, x + 1.5f, y + 4.5f, 9f, 9f);
                g.DrawLine(pen, x + 12f, y + 1.5f, x + 12f, y + 6.5f);
                g.DrawLine(pen, x + 9.5f, y + 4f, x + 14.5f, y + 4f);
                break;

            case FluentNavIcon.Audio:
                g.DrawPolygon(pen,
                [
                    new PointF(x + 2f, y + 6f),
                    new PointF(x + 5f, y + 6f),
                    new PointF(x + 8.5f, y + 3f),
                    new PointF(x + 8.5f, y + 13f),
                    new PointF(x + 5f, y + 10f),
                    new PointF(x + 2f, y + 10f)
                ]);
                g.DrawArc(pen, x + 8.5f, y + 4.5f, 4f, 7f, -60, 120);
                g.DrawArc(pen, x + 9.5f, y + 2.5f, 5.5f, 11f, -60, 120);
                break;

            case FluentNavIcon.Display:
                g.DrawRectangle(pen, x + 1.5f, y + 2.5f, 13f, 9f);
                g.DrawLine(pen, x + 5f, y + 14f, x + 11f, y + 14f);
                g.DrawLine(pen, x + 8f, y + 11.5f, x + 8f, y + 14f);
                break;

            case FluentNavIcon.Themes:
                g.DrawEllipse(pen, x + 1.5f, y + 1.5f, 13f, 13f);
                g.FillEllipse(fill, x + 5f, y + 4.5f, 2f, 2f);
                g.FillEllipse(fill, x + 9.5f, y + 4.5f, 2f, 2f);
                g.FillEllipse(fill, x + 4.5f, y + 8.5f, 2f, 2f);
                g.FillEllipse(fill, x + 8.5f, y + 9.5f, 3f, 2.5f);
                break;
        }
    }
}

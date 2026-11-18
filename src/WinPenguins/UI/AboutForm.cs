using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using WinPenguins.Diagnostics;

namespace WinPenguins.UI;

public sealed class AboutForm : Form
{
    private readonly Font _baseFont;
    private readonly Font _titleFont;
    private readonly Font _sectionFont;
    private readonly Font _statsFont;
    private readonly Font _monoFont;
    private readonly Bitmap _iconBitmap;

    public AboutForm(Icon appIcon, string? statsSummary = null)
        : this(appIcon, statsSummary, FluentPalette.DetectCurrent())
    {
    }

    internal static AboutForm CreateWithThemeForTest(bool isDark, Icon appIcon, string? statsSummary = null) =>
        new(appIcon, statsSummary, FluentPalette.CreateForTheme(isDark));

    private AboutForm(Icon appIcon, string? statsSummary, FluentPalette palette)
    {
        ArgumentNullException.ThrowIfNull(appIcon);
        FluentUi.ApplyWindowChrome(this, palette);

        _baseFont = new Font("Segoe UI", 9.5f);
        _titleFont = new Font("Segoe UI Semibold", 12.5f);
        _sectionFont = new Font("Segoe UI Semibold", 9.5f);
        _statsFont = new Font("Segoe UI", 8.25f);
        _monoFont = new Font("Consolas", 8.5f);
        _iconBitmap = appIcon.ToBitmap();

        Text = "About WinPenguins";
        Icon = appIcon;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(560, 440);
        Font = _baseFont;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20),
            ColumnCount = 1,
            RowCount = 4,
            BackColor = palette.WindowBg
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var headerCard = new FluentCard(palette, new Padding(16, 14, 16, 14), new Padding(0, 0, 0, 12))
        {
            Dock = DockStyle.Top,
            AutoSize = true
        };

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            BackColor = palette.CardBg
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var iconBox = new PictureBox
        {
            Image = _iconBitmap,
            Size = new Size(52, 52),
            SizeMode = PictureBoxSizeMode.Zoom,
            Margin = new Padding(0, 2, 16, 0),
            BackColor = palette.CardBg
        };

        var titlePanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            WrapContents = false,
            BackColor = palette.CardBg
        };

        titlePanel.Controls.Add(new Label
        {
            Text = "WinPenguins 2.1.5 (Modern Windows 11 Edition)",
            Font = _titleFont,
            ForeColor = palette.TextPrimary,
            UseMnemonic = false,
            AutoSize = true
        });
        titlePanel.Controls.Add(new Label
        {
            Text = "Originally by Michael Vines (based on XPenguins by Robin Hogan & Pingus artwork by Joel Fauche).",
            ForeColor = palette.TextSecondary,
            UseMnemonic = false,
            AutoSize = true,
            MaximumSize = new Size(420, 0),
            Margin = new Padding(0, 4, 0, 0)
        });

        var repoLink = new LinkLabel
        {
            Text = "https://github.com/FIMARx/winpenguins",
            LinkColor = palette.Accent,
            ActiveLinkColor = palette.AccentHover,
            VisitedLinkColor = palette.Accent,
            AutoSize = true,
            Margin = new Padding(0, 6, 0, 0)
        };
        repoLink.LinkClicked += (_, _) => OpenUrl("https://github.com/FIMARx/winpenguins");
        titlePanel.Controls.Add(repoLink);

        if (!string.IsNullOrWhiteSpace(statsSummary))
        {
            titlePanel.Controls.Add(new Label
            {
                Text = statsSummary,
                Font = _statsFont,
                ForeColor = palette.TextSecondary,
                UseMnemonic = false,
                AutoSize = true,
                MaximumSize = new Size(420, 0),
                Margin = new Padding(0, 6, 0, 0)
            });
        }

        header.Controls.Add(iconBox, 0, 0);
        header.Controls.Add(titlePanel, 1, 0);
        headerCard.Controls.Add(header);

        var licenseLabel = new Label
        {
            Text = "License (GNU General Public License v2)",
            Font = _sectionFont,
            ForeColor = palette.TextPrimary,
            AutoSize = true,
            Margin = new Padding(2, 2, 0, 8)
        };

        var licenseCard = new FluentCard(palette, new Padding(10), new Padding(0, 0, 0, 12))
        {
            Dock = DockStyle.Fill
        };
        var gplBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = palette.CardBg,
            ForeColor = palette.TextSecondary,
            ScrollBars = ScrollBars.Vertical,
            Font = _monoFont,
            AccessibleName = "GNU GPL License Text",
            Text = LoadLicenseText()
        };
        licenseCard.Controls.Add(gplBox);

        var footerBar = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 3,
            BackColor = palette.WindowBg,
            Margin = new Padding(0, 2, 0, 0)
        };
        footerBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footerBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footerBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var copyDiagButton = new FluentButton(palette, primary: false)
        {
            Text = "Copy Diagnostics",
            Size = new Size(134, 32),
            Margin = new Padding(0)
        };
        copyDiagButton.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(DiagnosticsReporter.Default.BuildDiagnosticSnapshot(statsSummary));
                copyDiagButton.Text = "Copied!";
            }
            catch
            {
                copyDiagButton.Text = "Copy Failed";
            }
        };

        var okButton = new FluentButton(palette, primary: true)
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Size = new Size(96, 32),
            Anchor = AnchorStyles.Right,
            Margin = new Padding(0)
        };

        footerBar.Controls.Add(copyDiagButton, 0, 0);
        footerBar.Controls.Add(okButton, 2, 0);

        root.Controls.Add(headerCard, 0, 0);
        root.Controls.Add(licenseLabel, 0, 1);
        root.Controls.Add(licenseCard, 0, 2);
        root.Controls.Add(footerBar, 0, 3);

        AcceptButton = okButton;
        CancelButton = okButton;
        Controls.Add(root);
        Shown += (_, _) =>
        {
            gplBox.SelectionLength = 0;
            okButton.Focus();
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _baseFont.Dispose();
            _titleFont.Dispose();
            _sectionFont.Dispose();
            _statsFont.Dispose();
            _monoFont.Dispose();
            _iconBitmap.Dispose();
        }

        base.Dispose(disposing);
    }

    private static string LoadLicenseText()
    {
        try
        {
            using Stream? stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("WinPenguins.Assets.COPYING.txt");
            if (stream != null)
            {
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
        }
        catch
        {
            // Fallback message below.
        }

        return "Released under the GNU General Public License v2. See COPYING.txt for details.";
    }

    private static void OpenUrl(string url)
    {
        try
        {
            using Process? proc = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // Ignore shell launch failures.
        }
    }
}

using System.Diagnostics;
using System.Drawing;
using System.Reflection;

namespace WinPenguins.UI;

public sealed class AboutForm : Form
{
    public AboutForm(Icon appIcon)
    {
        Text = "About WinPenguins";
        Icon = appIcon;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(520, 400);
        Font = new Font("Segoe UI", 9.5f);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 1,
            RowCount = 4
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var iconBox = new PictureBox
        {
            Image = appIcon.ToBitmap(),
            Size = new Size(48, 48),
            SizeMode = PictureBoxSizeMode.Zoom,
            Margin = new Padding(0, 0, 14, 0)
        };

        var titlePanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            WrapContents = false
        };

        titlePanel.Controls.Add(new Label
        {
            Text = "WinPenguins 2.0 (Modern Windows 11 Edition)",
            Font = new Font("Segoe UI Semibold", 12f),
            AutoSize = true
        });
        titlePanel.Controls.Add(new Label
        {
            Text = "Originally by Michael Vines (based on XPenguins by Robin Hogan & Pingus artwork by Joel Fauche).",
            AutoSize = true,
            MaximumSize = new Size(400, 0),
            Margin = new Padding(0, 4, 0, 0)
        });

        var repoLink = new LinkLabel
        {
            Text = "https://github.com/kasper93/winpenguins",
            AutoSize = true,
            Margin = new Padding(0, 4, 0, 0)
        };
        repoLink.LinkClicked += (_, _) => OpenUrl("https://github.com/kasper93/winpenguins");
        titlePanel.Controls.Add(repoLink);

        header.Controls.Add(iconBox, 0, 0);
        header.Controls.Add(titlePanel, 1, 0);

        var licenseLabel = new Label
        {
            Text = "License (GNU General Public License v2):",
            AutoSize = true,
            Margin = new Padding(0, 12, 0, 6)
        };

        var gplBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 8.5f),
            Text = LoadLicenseText()
        };

        var okButton = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            AutoSize = true,
            MinimumSize = new Size(90, 30),
            Anchor = AnchorStyles.Right,
            Margin = new Padding(0, 12, 0, 0)
        };

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(licenseLabel, 0, 1);
        root.Controls.Add(gplBox, 0, 2);
        root.Controls.Add(okButton, 0, 3);

        AcceptButton = okButton;
        CancelButton = okButton;
        Controls.Add(root);
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
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // Ignore shell launch failures.
        }
    }
}

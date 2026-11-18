using System.Drawing;
using System.Reflection;
using WinPenguins.Assets;
using WinPenguins.Config;
using WinPenguins.Diagnostics;
using WinPenguins.UI;
using Xunit;

namespace WinPenguins.Tests;

[Collection("SequentialUiTests")]
public sealed class UiAuditTests
{
    private static Icon LoadAppIcon()
    {
        using Stream? stream = typeof(AppSettings).Assembly.GetManifestResourceStream("WinPenguins.Assets.winpenguins.ico");
        return stream != null ? new Icon(stream) : (Icon)SystemIcons.Application.Clone();
    }

    [Theory]
    [Trait("Category", "Slow")]
    [InlineData(false, 800, 540)]
    [InlineData(true, 800, 540)]
    [InlineData(false, 920, 700)]
    [InlineData(true, 920, 700)]
    public void OptionsForm_AllPagesAndThemes_PassLayoutContrastAndAccessAudit(bool isDark, int width, int height)
    {
        StaRunner.Run(() =>
        {
            using Icon icon = LoadAppIcon();
            using var sound = new SoundPlayerService();
            using OptionsForm form = OptionsForm.CreateWithThemeForTest(
                isDark,
                icon,
                new AppSettings(),
                sound,
                _ => { },
                _ => { });

            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-32000, -32000);
            form.ClientSize = new Size(width, height);
            form.Show();
            Application.DoEvents();

            var allFindings = new List<string>();
            for (int page = 0; page < form.PageCount; page++)
            {
                form.SelectPage(page);
                Application.DoEvents();
                List<string> pageFindings = UiLayoutAuditor.AuditForm(form, $"Page{page}({(isDark ? "Dark" : "Light")},{width}x{height})");
                allFindings.AddRange(pageFindings);
            }

            form.Close();
            Assert.True(allFindings.Count == 0, "UI audit findings:\n" + string.Join('\n', allFindings));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AboutForm_PassesLayoutContrastAndSnapshotAudit(bool isDark)
    {
        StaRunner.Run(() =>
        {
            using Icon icon = LoadAppIcon();
            using AboutForm form = AboutForm.CreateWithThemeForTest(
                isDark,
                icon,
                "Active penguins: 12 · Fish eaten: 4 · Trampoline bounces: 9 · Portal jumps: 2");
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-32000, -32000);
            form.Show();
            Application.DoEvents();

            List<string> findings = UiLayoutAuditor.AuditForm(form, $"About({(isDark ? "Dark" : "Light")})");
            form.Close();
            Assert.True(findings.Count == 0, "AboutForm audit findings:\n" + string.Join('\n', findings));
        });
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void OptionsForm_Crawler_ExercisesAllTabsPresetsAndCancelRollback()
    {
        StaRunner.Run(() =>
        {
            AppSettings initial = new()
            {
                PenguinCount = 11,
                EnableSnow = false,
                SantaPercent = 15
            };

            AppSettings liveApplied = initial.Clone();
            int quickActionCount = 0;

            using Icon icon = LoadAppIcon();
            using var sound = new SoundPlayerService();
            using OptionsForm form = OptionsForm.CreateWithThemeForTest(
                true,
                icon,
                initial,
                sound,
                s => liveApplied = s.Clone(),
                _ => quickActionCount++);

            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-32000, -32000);
            form.Show();
            Application.DoEvents();

            var exercisedButtons = new HashSet<FluentButton>();
            var crawlerToggles = new List<FluentToggleSwitch>();
            var crawlerCombos = new List<ComboBox>();
            FluentButton? applyBtn = null;

            for (int p = 0; p < form.PageCount; p++)
            {
                form.SelectPage(p);
                Application.DoEvents();

                var pageButtons = new List<FluentButton>();
                CollectControls(form, pageButtons, crawlerToggles, crawlerCombos);

                foreach (FluentButton btn in pageButtons)
                {
                    if (string.Equals(btn.Text, "Apply", StringComparison.Ordinal))
                    {
                        applyBtn = btn;
                    }

                    if (!exercisedButtons.Add(btn) || !btn.Enabled || !btn.Visible)
                    {
                        continue;
                    }

                    if (btn.Text.Contains("Browse", StringComparison.OrdinalIgnoreCase) ||
                        btn.Text.Contains("Open Folder", StringComparison.OrdinalIgnoreCase) ||
                        btn.Text.Contains("Import", StringComparison.OrdinalIgnoreCase) ||
                        btn.Text.Contains("Create Sample", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(btn.Text, "Reset Defaults", StringComparison.Ordinal) ||
                        btn.DialogResult != DialogResult.None)
                    {
                        continue;
                    }

                    btn.PerformClick();
                    Application.DoEvents();
                }
            }

            Assert.True(quickActionCount >= 4, $"Expected crawler to exercise quick-action buttons, triggered {quickActionCount}.");

            foreach (FluentToggleSwitch toggle in crawlerToggles.Distinct())
            {
                toggle.Checked = !toggle.Checked;
                Application.DoEvents();
            }

            foreach (ComboBox combo in crawlerCombos.Distinct())
            {
                if (combo.Items.Count > 1)
                {
                    combo.SelectedIndex = (combo.SelectedIndex + 1) % combo.Items.Count;
                    Application.DoEvents();
                }
            }

            applyBtn?.PerformClick();
            Application.DoEvents();

            form.DialogResult = DialogResult.Cancel;
            form.Close();
            Application.DoEvents();

            Assert.Equal(initial.PenguinCount, liveApplied.PenguinCount);
            Assert.Equal(initial.EnableSnow, liveApplied.EnableSnow);
            Assert.Equal(initial.SantaPercent, liveApplied.SantaPercent);
        }, timeoutSeconds: 75);
    }

    private static void CollectControls(
        Control parent,
        List<FluentButton> buttons,
        List<FluentToggleSwitch> toggles,
        List<ComboBox> combos)
    {
        foreach (Control c in parent.Controls)
        {
            if (!c.Visible)
            {
                continue;
            }

            if (c is FluentButton fb && !buttons.Contains(fb))
            {
                buttons.Add(fb);
            }
            else if (c is FluentToggleSwitch ts && !toggles.Contains(ts))
            {
                toggles.Add(ts);
            }
            else if (c is ComboBox cb && !combos.Contains(cb))
            {
                combos.Add(cb);
            }

            if (c.HasChildren)
            {
                CollectControls(c, buttons, toggles, combos);
            }
        }
    }

    private static class UiLayoutAuditor
    {
        private static readonly string[] BrokenTokens =
        [
            "NaN",
            "Infinity",
            "[object ",
            "undefined",
            "{0}",
            "System.Windows.Forms"
        ];

        public static List<string> AuditForm(Form form, string label)
        {
            var issues = new List<string>();
            WalkContainer(form, label, issues, insideHorizontalScrollContainer: false);
            return issues;
        }

        private static void WalkContainer(
            Control container,
            string scope,
            List<string> issues,
            bool insideHorizontalScrollContainer)
        {
            if (container is ScrollableControl sc && sc.AutoScroll)
            {
                // Scrollable pages may scroll vertically, never horizontally.
                if (sc.HorizontalScroll.Visible && sc.DisplayRectangle.Width > sc.ClientSize.Width + 2)
                {
                    issues.Add($"{scope}: HORIZ_SCROLL on {Describe(sc)} (content={sc.DisplayRectangle.Width} > client={sc.ClientSize.Width})");
                }
            }

            var visibleChildren = new List<Control>();
            foreach (Control child in container.Controls)
            {
                if (!child.Visible)
                {
                    continue;
                }

                visibleChildren.Add(child);
                AuditSingleControl(child, container, scope, issues);
            }

            // OVERLAP: visible siblings in the same parent should not overlap noticeably.
            for (int i = 0; i < visibleChildren.Count; i++)
            {
                for (int j = i + 1; j < visibleChildren.Count; j++)
                {
                    Control a = visibleChildren[i];
                    Control b = visibleChildren[j];
                    Rectangle inter = Rectangle.Intersect(a.Bounds, b.Bounds);
                    if (inter.Width > 3 && inter.Height > 3 && inter.Width * inter.Height > 16)
                    {
                        issues.Add($"{scope}: OVERLAP {Describe(a)} with {Describe(b)} by {inter}");
                    }
                }
            }

            foreach (Control child in visibleChildren)
            {
                if (child.HasChildren)
                {
                    WalkContainer(child, scope, issues, insideHorizontalScrollContainer);
                }
            }
        }

        private static void AuditSingleControl(Control c, Control parent, string scope, List<string> issues)
        {
            // BROKEN_VALUE
            string text = c.Text ?? string.Empty;
            foreach (string token in BrokenTokens)
            {
                if (text.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add($"{scope}: BROKEN_VALUE \"{token}\" in {Describe(c)}");
                }
            }

            // OUTSIDE parent client area (allow vertical scrolling inside vertical AutoScroll panels).
            bool verticalAutoScrollParent = parent is ScrollableControl sc && sc.AutoScroll;
            Rectangle client = parent.ClientRectangle;
            if (c.Width > 0 && c.Height > 0)
            {
                if (c.Left < -2 || c.Right > client.Right + 2 || (!verticalAutoScrollParent && (c.Top < -2 || c.Bottom > client.Bottom + 2)))
                {
                    issues.Add($"{scope}: OUTSIDE {Describe(c)} bounds={c.Bounds} inside {Describe(parent)} client={client}");
                }
            }

            // TOO_SMALL on interactive controls
            bool isInteractive = c is ButtonBase or FluentButton or FluentChipButton or FluentNavButton or FluentToggleSwitch or FluentSlider or ComboBox;
            if (isInteractive && (c.Width < 20 || c.Height < 18))
            {
                issues.Add($"{scope}: TOO_SMALL {Describe(c)} size={c.Size}");
            }

            // CLIPPED text check on single-line Labels & FluentButtons
            if (c is Label lbl && !string.IsNullOrWhiteSpace(lbl.Text) && lbl.AutoSize && lbl.MaximumSize.Width == 0)
            {
                Size measured = TextRenderer.MeasureText(lbl.Text, lbl.Font);
                if (measured.Width > lbl.Width + 10 && lbl.Text.IndexOf('\n') < 0)
                {
                    issues.Add($"{scope}: CLIPPED label {Describe(lbl)} textWidth={measured.Width} > width={lbl.Width}");
                }
            }

            if (c is FluentButton fb && !string.IsNullOrWhiteSpace(fb.Text))
            {
                Size measured = TextRenderer.MeasureText(fb.Text, fb.Font);
                if (measured.Width > fb.Width - 2)
                {
                    issues.Add($"{scope}: CLIPPED button {Describe(fb)} textWidth={measured.Width} > width={fb.Width}");
                }
            }

            // LOW_CONTRAST on visible text controls using actual rendered colors
            if (c is Label textLbl && !string.IsNullOrWhiteSpace(textLbl.Text) && textLbl.Enabled)
            {
                Color bg = ResolveOpaqueBackColor(textLbl);
                Color fg = textLbl.ForeColor;
                double ratio = ContrastRatio(fg, bg);
                bool large = textLbl.Font.SizeInPoints >= 14f || (textLbl.Font.SizeInPoints >= 11f && textLbl.Font.Bold);
                double minRatio = large ? 3.0 : 4.5;
                if (ratio < minRatio)
                {
                    issues.Add($"{scope}: LOW_CONTRAST {Describe(textLbl)} ratio={ratio:0.00}:1 fg={fg} bg={bg}");
                }
            }
        }

        private static Color ResolveOpaqueBackColor(Control c)
        {
            Control? cur = c;
            while (cur != null)
            {
                if (cur.BackColor != Color.Transparent && cur.BackColor.A == 255)
                {
                    return cur.BackColor;
                }

                cur = cur.Parent;
            }

            return SystemColors.Window;
        }

        private static double ContrastRatio(Color fg, Color bg)
        {
            double l1 = RelativeLuminance(fg);
            double l2 = RelativeLuminance(bg);
            return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
        }

        private static double RelativeLuminance(Color c)
        {
            static double Channel(byte v)
            {
                double s = v / 255.0;
                return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
            }

            return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
        }

        private static string Describe(Control c)
        {
            string text = (c.Text ?? string.Empty).ReplaceLineEndings(" ").Trim();
            if (text.Length > 26)
            {
                text = text[..26] + "...";
            }

            return string.IsNullOrEmpty(text)
                ? c.GetType().Name
                : $"{c.GetType().Name}(\"{text}\")";
        }
    }
}

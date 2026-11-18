using System.Drawing;
using System.Text.Json;
using WinPenguins.Assets;
using WinPenguins.Config;
using WinPenguins.Diagnostics;
using WinPenguins.Engine;
using WinPenguins.Platform;
using WinPenguins.UI;
using Xunit;

// QueryProcessHandleCounts() reads process-wide GDI/USER handle counts across testhost.exe;
// disabling intra-assembly parallelization prevents concurrent GDI/WinForms tests from skewing deltas.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace WinPenguins.Tests;

[Collection("SequentialUiTests")]
public sealed class HandleAndLeakTests
{
    private static Icon LoadAppIcon()
    {
        using Stream? stream = typeof(AppSettings).Assembly.GetManifestResourceStream("WinPenguins.Assets.winpenguins.ico");
        return stream != null ? new Icon(stream) : (Icon)SystemIcons.Application.Clone();
    }

    [Fact]
    public void DiagnosticsReporter_DeduplicatesCapsRotatesAndBuildsSnapshot()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"WinPenguinsDiagTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string logPath = Path.Combine(tempDir, "diagnostics.jsonl");
            var reporter = new DiagnosticsReporter(logPath, maxEventsPerSession: 5);

            var ex = new InvalidOperationException("Boom from test");
            Assert.True(reporter.RecordException("TestSrc", ex, "ctx-1"));
            Assert.False(reporter.RecordException("TestSrc", ex, "ctx-dup")); // Deduplicated by source/type/top frame.

            reporter.RecordFrameTiming(12.5);
            reporter.RecordFrameTiming(145.0);  // slow frame, under stall threshold
            reporter.RecordFrameTiming(420.0);  // stall anomaly logged once
            reporter.RecordFrameTiming(430.0);  // stall deduplicated

            Assert.True(reporter.RecordAnomaly("Custom", "Event A"));
            Assert.True(reporter.RecordAnomaly("Custom", "Event B"));
            Assert.True(reporter.RecordAnomaly("Custom", "Event C"));
            Assert.False(reporter.RecordAnomaly("Custom", "Event D")); // Enforced cap of 5 events.

            (long total, long slow, double worst) = reporter.GetFrameStats();
            Assert.Equal(4, total);
            Assert.Equal(3, slow);
            Assert.Equal(430.0, worst, 1);

            string[] lines = File.ReadAllLines(logPath);
            Assert.Equal(5, lines.Length);
            DiagnosticEntry? parsed = JsonSerializer.Deserialize<DiagnosticEntry>(lines[0]);
            Assert.NotNull(parsed);
            Assert.Equal("InvalidOperationException", parsed.Kind);

            string snapshot = reporter.BuildDiagnosticSnapshot("Penguins: 7");
            Assert.Contains("WinPenguins", snapshot);
            Assert.Contains("Handles: GDI=", snapshot);
            Assert.Contains("Penguins: 7", snapshot);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void WindowsGuiHandles_RepeatedOptionsAndAboutFormLifecyclesDoNotLeakHandles()
    {
        StaRunner.Run(() =>
        {
            using Icon icon = LoadAppIcon();
            using var sound = new SoundPlayerService();

            // Warmup JIT & internal WinForms caches before taking baseline handle count.
            for (int i = 0; i < 2; i++)
            {
                using var warmupOpt = new OptionsForm(icon, new AppSettings(), sound, _ => { });
                warmupOpt.StartPosition = FormStartPosition.Manual;
                warmupOpt.Location = new Point(-32000, -32000);
                warmupOpt.Show();
                for (int p = 0; p < warmupOpt.PageCount; p++)
                {
                    warmupOpt.SelectPage(p);
                    Application.DoEvents();
                }
                warmupOpt.Close();

                using var warmupAbout = new AboutForm(icon, "warmup");
                warmupAbout.StartPosition = FormStartPosition.Manual;
                warmupAbout.Location = new Point(-32000, -32000);
                warmupAbout.Show();
                Application.DoEvents();
                warmupAbout.Close();
            }

            ForceFullGc();
            (uint startGdi, uint startUser) = DiagnosticsReporter.QueryProcessHandleCounts();

            const int cycles = 6;
            for (int i = 0; i < cycles; i++)
            {
                using var opt = new OptionsForm(icon, new AppSettings(), sound, _ => { });
                opt.StartPosition = FormStartPosition.Manual;
                opt.Location = new Point(-32000, -32000);
                opt.Show();
                for (int p = 0; p < opt.PageCount; p++)
                {
                    opt.SelectPage(p);
                    Application.DoEvents();
                }
                opt.Close();

                using var about = new AboutForm(icon, "cycle");
                about.StartPosition = FormStartPosition.Manual;
                about.Location = new Point(-32000, -32000);
                about.Show();
                Application.DoEvents();
                about.Close();
            }

            ForceFullGc();
            (uint endGdi, uint endUser) = DiagnosticsReporter.QueryProcessHandleCounts();

            long gdiDelta = (long)endGdi - startGdi;
            long userDelta = (long)endUser - startUser;

            Assert.True(gdiDelta <= 12, $"Potential GDI handle leak across {cycles} dialog cycles: delta={gdiDelta} (start={startGdi}, end={endGdi}).");
            Assert.True(userDelta <= 12, $"Potential USER handle leak across {cycles} dialog cycles: delta={userDelta} (start={startUser}, end={endUser}).");
        }, timeoutSeconds: 240);
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void OverlayHost_SustainedRenderingAndThemeReloadsKeepHandlesStable()
    {
        var settings = new AppSettings { PenguinCount = 12, EnableSnow = true, PrimaryMonitorOnly = true };
        SpriteCatalog catalog = SpriteCatalog.LoadTheme("Classic", 1.0);
        using var host = new OverlayHost(settings, catalog);
        host.SetVisible(false);

        var world = new CollisionWorld([new Rectangle(0, 0, 1920, 1080)]);
        var sim = new PenguinSimulation(world, 1.0, specs: catalog.GetSpecsMap());
        sim.EnsureCount(12);

        // Warmup render & reload
        host.RenderAll(sim.Penguins, 255, forceRedraw: true, sim.Toys.Toys, sim.Snow);
        host.ReloadCatalog(SpriteCatalog.LoadTheme("SmoothHD", 1.0));
        ForceFullGc();

        (uint startGdi, uint startUser) = DiagnosticsReporter.QueryProcessHandleCounts();

        for (int i = 0; i < 120; i++)
        {
            sim.Step(settings, 16.67, null, i % 10 == 0 ? 0.8f : 0f);
            host.RenderAll(sim.Penguins, 255, forceRedraw: i % 20 == 0, sim.Toys.Toys, sim.Snow);
            if (i % 40 == 0)
            {
                host.ReloadCatalog(SpriteCatalog.LoadTheme(i % 80 == 0 ? "Classic" : "SmoothHD", 1.0));
            }
        }

        ForceFullGc();
        (uint endGdi, uint endUser) = DiagnosticsReporter.QueryProcessHandleCounts();
        long gdiDelta = (long)endGdi - startGdi;
        long userDelta = (long)endUser - startUser;

        Assert.True(gdiDelta <= 10, $"OverlayHost GDI handle delta={gdiDelta} exceeded tolerance.");
        Assert.True(userDelta <= 10, $"OverlayHost USER handle delta={userDelta} exceeded tolerance.");
    }

    private static void ForceFullGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}

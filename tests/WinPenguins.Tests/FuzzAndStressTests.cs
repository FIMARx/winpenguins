using System.Drawing;
using CsCheck;
using WinPenguins.Config;
using WinPenguins.Engine;
using WinPenguins.Platform;
using Xunit;

namespace WinPenguins.Tests;

public sealed class FuzzAndStressTests
{
    [Fact]
    public void Fuzz_SimulationNeverProducesNaNOrStrandsEntitiesAcrossMonitorTopologiesAndStalls()
    {
        Gen<Rectangle[]> monitorGen = Gen.Select(
            Gen.Int[1, 3],
            Gen.Int[-1920, 400],
            Gen.Int[-400, 400],
            Gen.Int[800, 2560],
            Gen.Int[600, 1440],
            (count, originX, originY, w, h) =>
            {
                var screens = new Rectangle[count];
                int curX = originX;
                for (int i = 0; i < count; i++)
                {
                    int mw = i == 0 ? w : Math.Max(800, w - i * 160);
                    int mh = i == 0 ? h : Math.Max(600, h - i * 120);
                    screens[i] = new Rectangle(curX, i == 0 ? 0 : originY, mw, mh);
                    curX += mw;
                }

                return screens;
            });

        Gen<double[]> scaleGen = Gen.Select(
            Gen.OneOf(Gen.Const(1.0), Gen.Const(1.25), Gen.Const(1.5), Gen.Const(2.0)),
            Gen.OneOf(Gen.Const(1.0), Gen.Const(1.5), Gen.Const(2.0)),
            Gen.OneOf(Gen.Const(1.0), Gen.Const(1.25), Gen.Const(1.75)),
            (s0, s1, s2) => new[] { s0, s1, s2 });

        Gen<(Rectangle[] Monitors, double[] Scales, int Count, int WindowCount, int DtPattern, int Seed)> scenarioGen =
            Gen.Select(
                monitorGen,
                scaleGen,
                Gen.Int[1, 18],
                Gen.Int[0, 5],
                Gen.Int[0, 3],
                Gen.Int[1, 100000],
                (m, sc, cnt, wc, dtPat, seed) => (m, sc, cnt, wc, dtPat, seed));

        scenarioGen.Sample(s =>
        {
            double[] scales = [.. s.Scales.Take(s.Monitors.Length)];
            var world = new CollisionWorld(s.Monitors, monitorScales: scales);
            var rng = new Random(s.Seed);
            var sim = new PenguinSimulation(world, scales[0], rng, AnimationCatalogSpecs.All);

            var settings = new AppSettings
            {
                PenguinCount = s.Count,
                SantaPercent = s.Seed % 101,
                SpecialCrewPercent = (s.Seed * 7) % 101,
                EnableSnow = (s.Seed & 1) == 0,
                EnableMusicDancing = (s.Seed & 2) != 0,
                CursorAvoidance = (s.Seed & 4) != 0,
                CursorPerch = (s.Seed & 8) != 0,
                NeverSplat = (s.Seed & 16) != 0
            };
            settings.Normalize();
            sim.EnsureCount(s.Count);

            // Spawn all toy kinds randomly
            Rectangle primary = s.Monitors[0];
            sim.Toys.SpawnToy(ToyKind.Fish, primary.Left + primary.Width / 2, primary.Top + 40, scales[0]);
            sim.Toys.SpawnToy(ToyKind.Trampoline, primary.Left + primary.Width / 3, primary.Top + 60, scales[0]);
            sim.Toys.SpawnTwinPortals(world, rng);

            var windows = new List<Rectangle>();
            for (int w = 0; w < s.WindowCount; w++)
            {
                Rectangle host = s.Monitors[w % s.Monitors.Length];
                int wx = host.Left + rng.Next(40, Math.Max(41, host.Width - 260));
                int wy = host.Top + rng.Next(40, Math.Max(41, host.Height - 220));
                windows.Add(new Rectangle(wx, wy, 220, 140));
            }

            double[] dtSchedule = s.DtPattern switch
            {
                0 => [4.167, 4.167, 4.167],            // 240 Hz rapid
                1 => [16.667, 16.667, 16.667],         // 60 Hz normal
                2 => [250.0, 4.167, 180.0, 16.667],    // Severe stall spikes (--slow=4 equivalent)
                _ => [8.333, 66.0, 4.167, 33.333]
            };

            for (int step = 0; step < 90; step++)
            {
                // Mid-simulation monitor unplug / window shift
                if (step == 45 && s.Monitors.Length > 1)
                {
                    world.Update([s.Monitors[0]], [], [scales[0]]);
                }
                else
                {
                    world.Update(
                        step > 45 && s.Monitors.Length > 1 ? [s.Monitors[0]] : s.Monitors,
                        windows,
                        step > 45 && s.Monitors.Length > 1 ? [scales[0]] : scales);
                }

                double dt = dtSchedule[step % dtSchedule.Length];
                var cursor = new Point(primary.Left + (step * 17) % primary.Width, primary.Top + primary.Height - 20);
                float audioPeak = (step % 5 == 0) ? 0.85f : 0.1f;

                sim.Step(settings, dt, cursor, audioPeak);

                foreach (Penguin p in sim.Penguins)
                {
                    if (!double.IsFinite(p.VisualX) || !double.IsFinite(p.VisualY) || !double.IsFinite(p.VelocityY))
                    {
                        return false;
                    }
                }

                foreach (DesktopToy t in sim.Toys.Toys)
                {
                    if (!t.IsGrabbed && !world.IsInsideDesktop(t.Bounds))
                    {
                        return false;
                    }
                }
            }

            return sim.Snow.Particles.Length <= SnowSystem.Capacity;
        }, iter: 80, seed: "23456789ABCDEF");
    }

    [Fact]
    public void Fuzz_SettingsNormalizeAndParsersNeverThrowOnArbitraryInputs()
    {
        Gen.Select(
            Gen.Int[-500, 10000],
            Gen.Int[-100, 500],
            Gen.Int[-50, 200],
            Gen.String[0, 64],
            Gen.String[0, 64],
            (count, delay, pct, procList, hotkey) => (count, delay, pct, procList, hotkey))
        .Sample(x =>
        {
            var s = new AppSettings
            {
                PenguinCount = x.count,
                FrameDelayMs = x.delay,
                SantaPercent = x.pct,
                SpecialCrewPercent = x.pct,
                TargetFps = x.count,
                SoundVolume = x.pct,
                IgnoredProcesses = x.procList,
                PauseHotkey = x.hotkey
            };
            s.Normalize();

            bool okCounts = s.PenguinCount is >= AppSettings.MinPenguins and <= AppSettings.MaxPenguins &&
                            s.FrameDelayMs is >= AppSettings.MinFrameDelayMs and <= AppSettings.MaxFrameDelayMs &&
                            s.SantaPercent is >= 0 and <= 100 &&
                            s.SpecialCrewPercent is >= 0 and <= 100 &&
                            s.SoundVolume is >= 0 and <= 100 &&
                            s.TargetFps is >= 30 and <= 240;

            IReadOnlyList<string> procs = s.ParseIgnoredProcessNames();
            _ = GlobalHotkeyParser.TryParse(x.hotkey, out _, out _, out _);

            return okCounts && procs != null;
        }, iter: 200, seed: "23456789ABCDEF");
    }
}

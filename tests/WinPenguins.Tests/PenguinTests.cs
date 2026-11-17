using System.Buffers.Binary;
using System.Drawing;
using System.Drawing.Imaging;
using WinPenguins.Assets;
using WinPenguins.Config;
using WinPenguins.Engine;
using WinPenguins.Platform;
using WinPenguins.UI;
using Xunit;

namespace WinPenguins.Tests;

public sealed class CollisionWorldTests
{
    [Fact]
    public void MultiMonitor_AllowsCrossingAdjacentMonitorSeam()
    {
        var world = new CollisionWorld(
        [
            new Rectangle(0, 0, 1920, 1080),
            new Rectangle(1920, 0, 1920, 1080)
        ]);

        var straddling = new Rectangle(1904, 500, 32, 32);
        Assert.True(world.IsInsideDesktop(straddling));
        Assert.False(world.IsBlocked(straddling, CollisionDirection.Right));
        Assert.False(world.IsBlocked(straddling, CollisionDirection.Left));
    }

    [Fact]
    public void MultiMonitor_BlocksCrossingIntoDeadVoidAboveShorterMonitor()
    {
        var world = new CollisionWorld(
        [
            new Rectangle(0, 0, 1920, 1080),
            new Rectangle(1920, 200, 1920, 880)
        ]);

        var nearSeamAboveMonitor2 = new Rectangle(1888, 100, 32, 32);
        Assert.True(world.IsInsideDesktop(nearSeamAboveMonitor2));
        Assert.True(world.IsBlocked(nearSeamAboveMonitor2, CollisionDirection.Right));
    }

    [Fact]
    public void WindowCollision_DetectsSurfaceStandingAndWalls()
    {
        var window = new Rectangle(400, 300, 500, 300);
        var world = new CollisionWorld(
            [new Rectangle(0, 0, 1920, 1080)],
            [window]);

        var standingOnTop = new Rectangle(450, 268, 32, 32);
        Assert.True(world.IsInsideDesktop(standingOnTop));
        Assert.False(world.IsBlocked(standingOnTop, CollisionDirection.Here));
        Assert.True(world.IsBlocked(standingOnTop, CollisionDirection.Down));
        Assert.False(world.IsBlocked(standingOnTop, CollisionDirection.Right));

        var besideLeftWall = new Rectangle(368, 350, 32, 32);
        Assert.True(world.IsBlocked(besideLeftWall, CollisionDirection.Right));
        Assert.False(world.IsBlocked(besideLeftWall, CollisionDirection.Left));
    }

    [Fact]
    public void DualMonitor_MixedDpiAndTallerTaskbarSeam_CrossesSmoothlyWithoutExploding()
    {
        var monitors = new[]
        {
            new Rectangle(0, 0, 1920, 1080),
            new Rectangle(1920, 0, 1920, 1080)
        };
        var taskbars = new[]
        {
            new Rectangle(0, 1032, 1920, 48),
            new Rectangle(1920, 1008, 1920, 72)
        };
        double[] scales = [1.0, 1.5];
        var world = new CollisionWorld(monitors, taskbars, scales);
        var sim = new PenguinSimulation(world, scale: 1.0, rng: new Random(7));
        var settings = new AppSettings
        {
            PenguinCount = 1,
            ScaleMode = SpriteScaleMode.Auto,
            EnableIdlePauses = false,
            EnableParachutes = false
        };

        int explosions = 0;
        sim.SoundRequested += kind =>
        {
            if (kind == SoundEffectKind.Explosion) explosions++;
        };

        sim.EnsureCount(1);
        Penguin p = sim.Penguins[0];
        p.StartingUp = false;
        p.SetScale(1.0);
        p.SetAnimation(PenguinAnimationId.Walker, FacingDirection.Right);
        p.X = 1860;
        p.Y = 1032 - p.Height;
        p.SetVelocity(3.2, 0);

        for (int i = 0; i < 200; i++)
        {
            sim.Step(settings, deltaMs: 20);
        }

        Assert.Equal(0, explosions);
        Assert.True(p.X > 1960, $"Penguin should climb/step cleanly across taller mixed-DPI taskbar seam onto monitor 2 (X={p.X}, Y={p.Y}).");
        Assert.Equal(1.5, p.Scale, 2);
    }

    [Fact]
    public void DualMonitor_SecondaryWithoutTaskbar_WalksBackOntoPrimaryTaskbar()
    {
        var monitors = new[]
        {
            new Rectangle(0, 0, 1920, 1080),
            new Rectangle(1920, 0, 1920, 1080)
        };
        var primaryTaskbar = new[] { new Rectangle(0, 1032, 1920, 48) };
        var world = new CollisionWorld(monitors, primaryTaskbar);
        var sim = new PenguinSimulation(world, scale: 1.0, rng: new Random(4));
        var settings = new AppSettings
        {
            PenguinCount = 1,
            EnableIdlePauses = false,
            EnableParachutes = false
        };

        sim.EnsureCount(1);
        Penguin p = sim.Penguins[0];
        p.StartingUp = false;
        p.SetAnimation(PenguinAnimationId.Walker, FacingDirection.Left);
        p.X = 1940;
        p.Y = 1080 - p.Height;
        p.SetVelocity(-3.2, 0);

        for (int i = 0; i < 250; i++)
        {
            sim.Step(settings, deltaMs: 20);
        }

        Assert.True(p.X < 1860, $"Penguin on monitor 2 floor should climb primary 48px taskbar step onto monitor 1 (X={p.X}, Y={p.Y}).");
        Assert.Equal(1032 - p.Height, p.Y);
    }

    [Fact]
    public void DualMonitor_Staggered1440pAnd1080p_ClimbsUpSeamOntoSecondMonitor()
    {
        var monitors = new[]
        {
            new Rectangle(0, 0, 2560, 1440),
            new Rectangle(2560, 0, 1920, 1080)
        };
        var taskbars = new[]
        {
            new Rectangle(0, 1392, 2560, 48),
            new Rectangle(2560, 1032, 1920, 48)
        };
        var world = new CollisionWorld(monitors, taskbars);
        var settings = new AppSettings
        {
            PenguinCount = 1,
            EnableIdlePauses = false,
            EnableParachutes = false
        };

        bool reachedMonitor2 = false;
        for (int seed = 0; seed < 12 && !reachedMonitor2; seed++)
        {
            var sim = new PenguinSimulation(world, scale: 1.0, rng: new Random(seed));
            sim.EnsureCount(1);
            Penguin p = sim.Penguins[0];
            p.StartingUp = false;
            p.SetAnimation(PenguinAnimationId.Walker, FacingDirection.Right);
            p.X = 2500;
            p.Y = 1392 - p.Height;
            p.SetVelocity(3.2, 0);

            for (int i = 0; i < 600; i++)
            {
                sim.Step(settings, deltaMs: 20);
                if (p.X >= 2580)
                {
                    reachedMonitor2 = true;
                    break;
                }
            }
        }

        Assert.True(reachedMonitor2, "Penguins hitting a staggered 1440p/1080p monitor edge should climb up and cross onto the second monitor.");
    }

    [Fact]
    public void DualMonitor_NegativeLeftAndVerticalStackedMonitors_CrossCleanly()
    {
        // Horizontal with negative-X left monitor
        var hWorld = new CollisionWorld(
        [
            new Rectangle(-1920, 0, 1920, 1080),
            new Rectangle(0, 0, 1920, 1080)
        ]);
        var hSim = new PenguinSimulation(hWorld, scale: 1.0, rng: new Random(2));
        var settings = new AppSettings { PenguinCount = 1, EnableIdlePauses = false, EnableParachutes = false };
        hSim.EnsureCount(1);
        Penguin hp = hSim.Penguins[0];
        hp.StartingUp = false;
        hp.SetAnimation(PenguinAnimationId.Walker, FacingDirection.Right);
        hp.X = -40;
        hp.Y = 1080 - hp.Height;
        hp.SetVelocity(3.2, 0);

        for (int i = 0; i < 80; i++)
        {
            hSim.Step(settings, deltaMs: 20);
        }

        Assert.True(hp.X > 10, $"Penguin from negative-X secondary monitor should walk onto primary monitor (X={hp.X}).");

        // Vertical stacked monitors: falling past bottom of top monitor reaches bottom monitor instead of exploding
        var vWorld = new CollisionWorld(
        [
            new Rectangle(0, 0, 1920, 1080),
            new Rectangle(0, 1080, 1920, 1080)
        ]);
        var vSim = new PenguinSimulation(vWorld, scale: 1.0, rng: new Random(3));
        vSim.EnsureCount(1);
        Penguin vp = vSim.Penguins[0];
        vp.StartingUp = false;
        vp.SetAnimation(PenguinAnimationId.Faller, FacingDirection.Left);
        vp.X = 500;
        vp.Y = 1060;
        vp.SetVelocity(0, 3);

        for (int i = 0; i < 950; i++)
        {
            vSim.Step(settings, deltaMs: 20);
        }

        Assert.False(vp.IsTerminalAnimation);
        Assert.True(vp.Y > 2000, $"Penguin falling across vertically stacked monitors should land on bottom monitor (Y={vp.Y}).");
    }
}

public sealed class PenguinSimulationTests
{
    [Fact]
    public void Faller_LandsOnWindowAndBecomesWalker()
    {
        var window = new Rectangle(100, 200, 400, 200);
        var world = new CollisionWorld([new Rectangle(0, 0, 1000, 800)], [window]);
        var sim = new PenguinSimulation(world, scale: 1.0, rng: new Random(42));
        var settings = new AppSettings { PenguinCount = 1, SantaPercent = 0, NeverSplat = true };

        sim.EnsureCount(1);
        Penguin p = sim.Penguins[0];
        p.X = 200;
        p.Y = 160;
        p.SetVelocity(0, 3);
        p.StartingUp = false;

        for (int i = 0; i < 10; i++)
        {
            sim.Step(settings);
        }

        Assert.Equal(PenguinAnimationId.Walker, p.Animation);
        Assert.Equal(168, p.Y);
    }

    [Fact]
    public void SmoothDeltaTimeStep_MovesWalkersAt60FpsSubsteps()
    {
        var window = new Rectangle(100, 200, 600, 200);
        var world = new CollisionWorld([new Rectangle(0, 0, 1000, 800)], [window]);
        var sim = new PenguinSimulation(world, scale: 2.0, rng: new Random(42));
        var settings = new AppSettings { PenguinCount = 1, FrameDelayMs = 45, NeverSplat = true };

        sim.EnsureCount(1);
        Penguin p = sim.Penguins[0];
        p.SetAnimation(PenguinAnimationId.Walker, FacingDirection.Right);
        p.X = 150;
        p.Y = 200 - p.Height;
        p.SetVelocity(8, 0);
        p.StartingUp = false;

        sim.Step(settings, deltaMs: 16.0);
        int xAfter1 = p.X;
        sim.Step(settings, deltaMs: 16.0);
        int xAfter2 = p.X;
        sim.Step(settings, deltaMs: 16.0);
        int xAfter3 = p.X;

        Assert.True(xAfter1 > 150);
        Assert.True(xAfter2 > xAfter1);
        Assert.True(xAfter3 > xAfter2);
        Assert.Equal(158, xAfter3); // ~45ms total = ~8px walk at 2x scale
    }

    [Fact]
    public void Penguin_VisualXAdvancesUniformlyWithoutIntegerJitter()
    {
        var window = new Rectangle(100, 200, 600, 200);
        var world = new CollisionWorld([new Rectangle(0, 0, 1000, 800)], [window]);
        var sim = new PenguinSimulation(world, scale: 2.0, rng: new Random(42));
        var settings = new AppSettings { PenguinCount = 1, FrameDelayMs = 45, NeverSplat = true };

        sim.EnsureCount(1);
        Penguin p = sim.Penguins[0];
        p.SetAnimation(PenguinAnimationId.Walker, FacingDirection.Right);
        p.X = 150;
        p.Y = 200 - p.Height;
        p.SetVelocity(6.4, 0);
        p.StartingUp = false;

        double prev = p.VisualX;
        var deltas = new List<double>();
        for (int i = 0; i < 6; i++)
        {
            sim.Step(settings, deltaMs: 8.333);
            deltas.Add(p.VisualX - prev);
            prev = p.VisualX;
        }

        double expectedDelta = 6.4 * (8.333 / 45.0);
        Assert.All(deltas, d => Assert.InRange(d, expectedDelta - 0.01, expectedDelta + 0.01));
    }

    [Fact]
    public void Penguin_DoesNotDropAnimationFramesWhenStepXTruncatesToZero()
    {
        var window = new Rectangle(100, 200, 600, 200);
        var world = new CollisionWorld([new Rectangle(0, 0, 1000, 800)], [window]);
        var sim = new PenguinSimulation(world, scale: 1.0, rng: new Random(42));
        var settings = new AppSettings
        {
            PenguinCount = 1,
            FrameDelayMs = 45,
            NeverSplat = true,
            EnableIdlePauses = false,
            EnableParachutes = false
        };

        sim.EnsureCount(1);
        Penguin p = sim.Penguins[0];
        p.SpeedMultiplier = 1.0;
        p.SetAnimation(PenguinAnimationId.Walker, FacingDirection.Left);
        p.X = 400;
        p.Y = 200 - p.Height;
        p.SetVelocity(-3.2, 0);
        p.StartingUp = false;

        // Step 8 full 45ms intervals split into 5 sub-steps of 9ms each (where some sub-steps have stepX == 0).
        var observedFrames = new List<int> { p.FrameIndex };
        for (int frame = 0; frame < 7; frame++)
        {
            for (int sub = 0; sub < 5; sub++)
            {
                sim.Step(settings, deltaMs: 9.0);
            }
            observedFrames.Add(p.FrameIndex);
        }

        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6, 7 }, observedFrames);
    }

    [Fact]
    public void HighRefresh240Hz_SlowSpeedDoesNotSpeedUpFromMinClamp()
    {
        var window = new Rectangle(100, 200, 600, 200);
        var world = new CollisionWorld([new Rectangle(0, 0, 1000, 800)], [window]);
        var sim = new PenguinSimulation(world, scale: 1.0, rng: new Random(42));
        var slowSettings = new AppSettings { PenguinCount = 1, FrameDelayMs = 150, NeverSplat = true };

        sim.EnsureCount(1);
        Penguin p = sim.Penguins[0];
        p.SetAnimation(PenguinAnimationId.Walker, FacingDirection.Right);
        p.X = 150;
        p.Y = 200 - p.Height;
        p.SetVelocity(4, 0);
        p.StartingUp = false;

        // 36 frames at 240Hz = 150ms total => should walk ~4px, NOT ~8px.
        for (int i = 0; i < 36; i++)
        {
            sim.Step(slowSettings, deltaMs: 4.1667);
        }

        int walked = p.X - 150;
        Assert.InRange(walked, 3, 5);
    }

    [Fact]
    public void Faller_HittingRightWall_AlwaysClimbsRightWallNotEmptyLeftAir()
    {
        var rightWall = new Rectangle(400, 100, 200, 500);
        var world = new CollisionWorld([new Rectangle(0, 0, 1000, 800)], [rightWall]);
        var settings = new AppSettings
        {
            PenguinCount = 1,
            NeverSplat = true,
            EnableParachutes = false
        };

        // Run with multiple seeds to cover what previously failed 40% of the time.
        for (int seed = 0; seed < 20; seed++)
        {
            var sim = new PenguinSimulation(world, scale: 1.0, rng: new Random(seed));
            sim.EnsureCount(1);
            Penguin p = sim.Penguins[0];
            p.SetAnimation(PenguinAnimationId.Faller, FacingDirection.Left);
            p.IsParachuting = false;
            p.X = 368; // right edge = 400 (touching rightWall.Left)
            p.Y = 200;
            p.SetVelocity(1, 3);
            p.StartingUp = false;

            sim.Step(settings);

            Assert.Equal(PenguinAnimationId.Climber, p.Animation);
            Assert.Equal(FacingDirection.Right, p.Direction);
        }
    }

    [Fact]
    public void Walker_WalksOffEdgeTumblesAndSplatsWhenExceedingSplatDistance()
    {
        var platform = new Rectangle(100, 100, 100, 40);
        var world = new CollisionWorld([new Rectangle(0, 0, 800, 700)], [platform]);
        var sim = new PenguinSimulation(world, scale: 1.0, rng: new Random(7));
        var settings = new AppSettings
        {
            PenguinCount = 1,
            NeverSplat = false,
            EnableParachutes = false,
            SplatDistance = 200
        };

        int explosions = 0;
        sim.SoundRequested += kind =>
        {
            if (kind == SoundEffectKind.Explosion)
            {
                explosions++;
            }
        };

        sim.EnsureCount(1);
        Penguin p = sim.Penguins[0];
        p.SetAnimation(PenguinAnimationId.Walker, FacingDirection.Right);
        p.X = 198;
        p.Y = 68;
        p.SetVelocity(4, 0);
        p.StartingUp = false;

        for (int i = 0; i < 150; i++)
        {
            sim.Step(settings);
            if (explosions > 0)
            {
                break;
            }
        }

        Assert.Equal(1, explosions);
    }

    [Fact]
    public void Climber_ReachingTopOfWindow_TransitionsToWalkerOnWindowTop()
    {
        var window = new Rectangle(300, 200, 200, 300);
        var world = new CollisionWorld([new Rectangle(0, 0, 1000, 800)], [window]);
        var sim = new PenguinSimulation(world, scale: 1.0, rng: new Random(11));
        var settings = new AppSettings { PenguinCount = 1, NeverSplat = true };

        sim.EnsureCount(1);
        Penguin p = sim.Penguins[0];
        p.SetAnimation(PenguinAnimationId.Climber, FacingDirection.Right);
        p.X = 268;
        p.Y = 175;
        p.SetVelocity(0, -4);
        p.StartingUp = false;

        for (int i = 0; i < 10; i++)
        {
            sim.Step(settings);
            if (p.Animation == PenguinAnimationId.Walker)
            {
                break;
            }
        }

        Assert.Equal(PenguinAnimationId.Walker, p.Animation);
        Assert.Equal(FacingDirection.Right, p.Direction);
    }

    [Fact]
    public void SantaPercent_RespectsZeroAndHundredPercent()
    {
        var world = new CollisionWorld([new Rectangle(0, 0, 800, 600)]);
        var sim0 = new PenguinSimulation(world, 1.0, new Random(1));
        sim0.EnsureCount(5);
        var s0 = new AppSettings { PenguinCount = 5, SantaPercent = 0 };
        foreach (Penguin p in sim0.Penguins)
        {
            p.Y = 568;
            p.StartingUp = false;
        }
        sim0.Step(s0);
        Assert.All(sim0.Penguins, p => Assert.NotEqual(PenguinSubType.Santa, p.SubType));

        var sim100 = new PenguinSimulation(world, 1.0, new Random(1));
        sim100.EnsureCount(5);
        var s100 = new AppSettings { PenguinCount = 5, SantaPercent = 100 };
        foreach (Penguin p in sim100.Penguins)
        {
            p.Y = 568;
            p.StartingUp = false;
        }
        sim100.Step(s100);
        Assert.All(sim100.Penguins, p => Assert.Equal(PenguinSubType.Santa, p.SubType));
    }

    [Fact]
    public void ExplodeAllToBombers_FinishesAndRemovesPenguinsWhenTargetIsZero()
    {
        var world = new CollisionWorld([new Rectangle(0, 0, 800, 600)]);
        var sim = new PenguinSimulation(world, 1.0, new Random(1));
        sim.EnsureCount(3);
        sim.ExplodeAllToBombers();

        var stopSettings = new AppSettings { PenguinCount = 0 };
        for (int i = 0; i < 30; i++)
        {
            sim.Step(stopSettings);
        }

        Assert.Empty(sim.Penguins);
    }

    [Fact]
    public void ZeroVelocityBlocked_DoesNotInfiniteLoop()
    {
        var block = new Rectangle(0, 0, 800, 600);
        var world = new CollisionWorld([new Rectangle(0, 0, 800, 600)], [block]);
        var p = new Penguin(100, 100, 0, 0, 1.0);
        MoveOutcome outcome = p.Advance(world, force: false, out _);
        Assert.Equal(MoveOutcome.Moved, outcome);
    }

    [Fact]
    public void CursorAvoidance_TurnsWalkerAwayFromNearbyCursor()
    {
        var world = new CollisionWorld([new Rectangle(0, 0, 800, 600)]);
        var sim = new PenguinSimulation(world, scale: 1.0, rng: new Random(3));
        sim.EnsureCount(1);

        Penguin p = sim.Penguins[0];
        p.SetAnimation(PenguinAnimationId.Walker, FacingDirection.Right);
        p.X = 200;
        p.Y = 568;
        p.SetVelocity(4, 0);
        p.StartingUp = false;

        var settings = new AppSettings { PenguinCount = 1, CursorAvoidance = true };
        sim.Step(settings, cursorScreenPos: new Point(225, 584));

        Assert.Equal(FacingDirection.Left, p.Direction);
        Assert.True(p.VelocityX < 0);
    }
}

public sealed class SpriteAndOverlayTests
{
    [Theory]
    [InlineData(1.0, 32, 64)]
    [InlineData(1.5, 48, 96)]
    [InlineData(2.0, 64, 128)]
    public void SpriteCatalog_LoadsAllAnimationsWithExact1To1Alignment(
        double scale, int expectedPenguinSize, int expectedExplosionSize)
    {
        SpriteCatalog catalog = SpriteCatalog.LoadDefault(scale);

        foreach (PenguinAnimationId anim in Enum.GetValues<PenguinAnimationId>())
        {
            var p = new Penguin(0, 0, 0, 0, scale);
            p.SetAnimation(anim, FacingDirection.Left);
            SpriteFrame frame = catalog.GetFrame(p);

            int expected = anim == PenguinAnimationId.Explosion ? expectedExplosionSize : expectedPenguinSize;
            Assert.Equal(expected, frame.Width);
            Assert.Equal(expected, frame.Height);

            bool hasOpaquePixel = frame.Pixels.Any(px => (px >> 24) == 0xFF);
            bool hasTransparentPixel = frame.Pixels.Any(px => px == 0);
            Assert.True(hasOpaquePixel, $"{anim} should have opaque pixels");
            Assert.True(hasTransparentPixel, $"{anim} should have transparent pixels");
        }
    }

    [Fact]
    public void SpriteCatalog_WalkerTopRowIsTransparentNotWrappedFromRow1()
    {
        SpriteCatalog catalog = SpriteCatalog.LoadDefault(1.0);
        var p = new Penguin(0, 0, 0, 0, 1.0);
        p.SetAnimation(PenguinAnimationId.Walker, FacingDirection.Left);
        SpriteFrame frame0 = catalog.GetFrame(p);

        // In Walker.bmp row 0 frame 0, y=0..2 are transparent and y=31 is transparent.
        for (int x = 0; x < 32; x++)
        {
            Assert.Equal(0u, frame0.Pixels[x]);           // y = 0 must be transparent
            Assert.Equal(0u, frame0.Pixels[31 * 32 + x]); // y = 31 must be transparent
        }

        // Verify feet near y = 30 are present and not clipped off by 72-DPI scaling.
        bool hasFeetAtY30 = Enumerable.Range(0, 32).Any(x => (frame0.Pixels[30 * 32 + x] >> 24) == 0xFF);
        Assert.True(hasFeetAtY30);
    }

    [Theory]
    [InlineData(PenguinAnimationId.Faller)]
    [InlineData(PenguinAnimationId.Floater)]
    [InlineData(PenguinAnimationId.Walker)]
    [InlineData(PenguinAnimationId.Climber)]
    [InlineData(PenguinAnimationId.Tumbler)]
    public void SpriteCatalog_ClassicSprites_RemoveWhiteMatteHaloWithoutHurtingWhiteBelly(PenguinAnimationId anim)
    {
        SpriteCatalog catalog = SpriteCatalog.LoadDefault(1.0);
        SpriteSheet sheet = catalog.Sheets[anim];
        int w = sheet.FrameWidth;
        int h = sheet.FrameHeight;

        int opaqueBrightPixels = 0;
        for (int f = 0; f < sheet.Spec.FrameCount; f++)
        {
            uint[] px = sheet.GetFrame(FacingDirection.Left, f).Pixels;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    uint p = px[y * w + x];
                    byte a = (byte)(p >> 24);
                    if (a == 0) continue;

                    byte r = (byte)((p >> 16) & 0xFF);
                    byte g = (byte)((p >> 8) & 0xFF);
                    byte b = (byte)(p & 0xFF);
                    if (a == 255 && r > 200 && g > 200 && b > 200)
                    {
                        opaqueBrightPixels++;
                    }

                    // Ensure no outer-boundary opaque bright-gray/white halo pixels remain next to transparent + dark pixels.
                    int lum = (r + g + b) / 3;
                    int sat = Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b));
                    if (a == 255 && sat <= 28 && lum >= 130)
                    {
                        bool adjTrans = x == 0 || x == w - 1 || y == 0 || y == h - 1
                            || (px[y * w + x - 1] >> 24) == 0 || (px[y * w + x + 1] >> 24) == 0
                            || (px[(y - 1) * w + x] >> 24) == 0 || (px[(y + 1) * w + x] >> 24) == 0;
                        bool adjDark = false;
                        bool adjWhite = false;
                        foreach ((int nx, int ny) in new[] { (x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1) })
                        {
                            if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                            uint np = px[ny * w + nx];
                            byte na = (byte)(np >> 24);
                            if (na == 0) continue;
                            int nr = Math.Min(255, ((int)((np >> 16) & 0xFF) * 255) / na);
                            int ng = Math.Min(255, ((int)((np >> 8) & 0xFF) * 255) / na);
                            int nb = Math.Min(255, ((int)(np & 0xFF) * 255) / na);
                            if ((nr + ng + nb) / 3 <= 115) adjDark = true;
                            if (nr >= 205 && ng >= 205 && nb >= 205) adjWhite = true;
                        }

                        Assert.False(adjTrans && adjDark && !adjWhite,
                            $"{anim} frame {f} ({x},{y}) retained white-halo border pixel rgb=({r},{g},{b}).");
                    }
                }
            }
        }

        Assert.True(opaqueBrightPixels > 20, $"{anim} should preserve interior white belly pixels.");
    }

    [Fact]
    public void GpuSpriteAtlas_PadsFramesWithTransparentGutter()
    {
        SpriteCatalog catalog = SpriteCatalog.LoadDefault(1.0);
        SpriteSheet walker = catalog.GetSheet(PenguinAnimationId.Walker);
        uint[] padded = GpuSpriteAtlas.BuildPaddedAtlasPixels(walker, out int paddedW, out int paddedH);

        int cellW = walker.Spec.BaseWidth + GpuSpriteAtlas.FramePadding * 2;
        int cellH = walker.Spec.BaseHeight + GpuSpriteAtlas.FramePadding * 2;
        Assert.Equal(walker.Spec.FrameCount * cellW, paddedW);
        Assert.Equal(walker.Spec.DirectionCount * cellH, paddedH);

        // Verify 1px transparent gutter around frame (1, 1)
        int f1x = 1 * cellW;
        int d1y = 1 * cellH;
        for (int x = f1x; x < f1x + cellW; x++)
        {
            Assert.Equal(0u, padded[d1y * paddedW + x]);
            Assert.Equal(0u, padded[(d1y + cellH - 1) * paddedW + x]);
        }
        for (int y = d1y; y < d1y + cellH; y++)
        {
            Assert.Equal(0u, padded[y * paddedW + f1x]);
            Assert.Equal(0u, padded[y * paddedW + f1x + cellW - 1]);
        }
    }

    [Theory]
    [InlineData(PenguinAnimationId.Walker)]
    [InlineData(PenguinAnimationId.SantaWalker)]
    [InlineData(PenguinAnimationId.Climber)]
    [InlineData(PenguinAnimationId.SantaClimber)]
    public void SpriteCatalog_RightDirectionFramesMatchLeftForwardOrder(PenguinAnimationId anim)
    {
        SpriteCatalog catalog = SpriteCatalog.LoadDefault(1.0);
        SpriteSheet sheet = catalog.Sheets[anim];
        int w = sheet.Spec.BaseWidth;
        int h = sheet.Spec.BaseHeight;

        double[] leftAvgY = new double[sheet.Spec.FrameCount];
        double[] rightAvgY = new double[sheet.Spec.FrameCount];

        for (int f = 0; f < sheet.Spec.FrameCount; f++)
        {
            leftAvgY[f] = ComputeOpaqueAvgY(sheet.GetFrame(FacingDirection.Left, f).Pixels, w, h);
            rightAvgY[f] = ComputeOpaqueAvgY(sheet.GetFrame(FacingDirection.Right, f).Pixels, w, h);

            // Also verify SheetPixels used by GPU atlas matches GetFrame row-for-row.
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    uint gpuPx = sheet.SheetPixels[((1 * h) + y) * sheet.SheetWidth + (f * w) + x];
                    uint cpuPx = sheet.GetFrame(FacingDirection.Right, f).Pixels[y * w + x];
                    Assert.Equal(cpuPx, gpuPx);
                }
            }
        }

        for (int f = 0; f < sheet.Spec.FrameCount; f++)
        {
            double tolerance = anim == PenguinAnimationId.SantaWalker ? 0.12 : 0.02;
            Assert.True(
                Math.Abs(leftAvgY[f] - rightAvgY[f]) <= tolerance,
                $"{anim} frame {f}: left avgY={leftAvgY[f]:F3}, right avgY={rightAvgY[f]:F3}");
        }
    }

    private static double ComputeOpaqueAvgY(uint[] pixels, int width, int height)
    {
        long sumY = 0;
        long count = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if ((pixels[y * width + x] >> 24) >= 128)
                {
                    sumY += y;
                    count++;
                }
            }
        }
        return count > 0 ? (double)sumY / count : 0;
    }

    [Fact]
    public void OverlayHost_DirectCompositionRendersAndTogglesCleanly()
    {
        SpriteCatalog catalog = SpriteCatalog.LoadDefault(1.0);
        var settings = new AppSettings { PenguinCount = 4, LayerMode = OverlayLayerMode.AboveWindows };
        using var host = new OverlayHost(settings, catalog);

        var world = new CollisionWorld([new Rectangle(0, 0, 800, 600)]);
        var sim = new PenguinSimulation(world, 2.0, new Random(9));
        sim.EnsureCount(4);

        host.RenderAll(sim.Penguins, opacity: 255, forceRedraw: true);
        for (int i = 0; i < 10; i++)
        {
            sim.Step(settings, deltaMs: 8.333);
            host.RenderAll(sim.Penguins, opacity: 255);
        }

        host.ApplyLayerMode(OverlayLayerMode.DesktopOnly);
        host.SetVisible(false);
        host.SetVisible(true);
        host.RenderAll(sim.Penguins, opacity: 200, forceRedraw: true);
    }

    [Fact]
    public void OverlayHost_120FramesWith20PenguinsCompletesRapidly()
    {
        SpriteCatalog catalog = SpriteCatalog.LoadDefault(1.0);
        var settings = new AppSettings { PenguinCount = 20, TargetFps = 120, LayerMode = OverlayLayerMode.AboveWindows };
        using var host = new OverlayHost(settings, catalog);

        var world = new CollisionWorld([new Rectangle(0, 0, 1920, 1080)]);
        var sim = new PenguinSimulation(world, 2.0, new Random(17));
        sim.EnsureCount(20);

        // Warmup first frame
        host.RenderAll(sim.Penguins, opacity: 255, forceRedraw: true);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 120; i++)
        {
            sim.Step(settings, deltaMs: 8.333);
            host.RenderAll(sim.Penguins, opacity: 255);
        }
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 2000, $"120 frames should render rapidly, took {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void FramePacer_TicksPausesAndDisposesCleanly()
    {
        int ticks = 0;
        using var pacer = new FramePacer(120, _ => ticks++);

        var deadline = DateTime.UtcNow.AddMilliseconds(300);
        while (ticks < 2 && DateTime.UtcNow < deadline)
        {
            Application.DoEvents();
            Thread.Sleep(2);
        }

        Assert.True(ticks >= 1, "FramePacer should deliver at least one tick message.");

        pacer.SetPaused(true);
        Application.DoEvents();
        int pausedCount = ticks;
        Thread.Sleep(25);
        Application.DoEvents();
        Assert.Equal(pausedCount, ticks);
    }

    [Fact]
    public void Forms_ConstructWithoutLayoutOrResourceErrors()
    {
        using var sound = new SoundPlayerService();
        using var options = new OptionsForm(SystemIcons.Application, new AppSettings(), sound, _ => { });
        using var about = new AboutForm(SystemIcons.Application);

        Assert.Equal("WinPenguins Options", options.Text);
        Assert.Equal("About WinPenguins", about.Text);
    }

    [Fact]
    public void OverlayHost_ReloadCatalogUpdatesSpecsAndRendersCleanly()
    {
        SpriteCatalog catalog = SpriteCatalog.LoadDefault(1.0);
        var settings = new AppSettings { PenguinCount = 2, LayerMode = OverlayLayerMode.AboveWindows };
        using var host = new OverlayHost(settings, catalog);
        var world = new CollisionWorld([new Rectangle(0, 0, 800, 600)]);
        var sim = new PenguinSimulation(world, 1.0, new Random(3));
        sim.EnsureCount(2);

        host.RenderAll(sim.Penguins, 255, forceRedraw: true);
        host.ReloadCatalog(SpriteCatalog.LoadDefault(1.0));
        host.UpdateInteractiveHover(true, false, (_, _) => false);
        host.RenderAll(sim.Penguins, 255, forceRedraw: true);
    }

    [Fact]
    public void MixedDpi_CollisionWorldUpdatesPenguinScalePerMonitor()
    {
        var monitors = new[]
        {
            new Rectangle(0, 0, 1920, 1080),
            new Rectangle(1920, 0, 1920, 1080)
        };
        double[] scales = [1.0, 2.0];
        var world = new CollisionWorld(monitors, monitorScales: scales);
        var sim = new PenguinSimulation(world, scale: 1.0, rng: new Random(11));
        var settings = new AppSettings { PenguinCount = 1, ScaleMode = SpriteScaleMode.Auto, EnableIdlePauses = false };

        sim.EnsureCount(1);
        Penguin p = sim.Penguins[0];
        p.StartingUp = false;
        p.SetAnimation(PenguinAnimationId.Walker, FacingDirection.Right);
        p.SetScale(1.0);
        p.X = 100;
        p.Y = 1080 - p.Height;
        p.SetVelocity(3.2, 0);

        sim.Step(settings, deltaMs: 45);
        Assert.Equal(1.0, p.Scale, 2);

        p.X = 2100;
        p.Y = 1080 - p.Height;
        sim.Step(settings, deltaMs: 45);
        Assert.Equal(2.0, p.Scale, 2);
        Assert.Equal(64, p.Width);
    }

    [Fact]
    public void MouseInteraction_ClickHopsAndChirps_DragAndThrowLaunchesWithVelocity()
    {
        var world = new CollisionWorld([new Rectangle(0, 0, 1280, 720)]);
        var sim = new PenguinSimulation(world, scale: 1.0, rng: new Random(5));
        var sounds = new List<SoundEffectKind>();
        sim.SoundRequested += sounds.Add;
        sim.EnsureCount(1);

        Penguin p = sim.Penguins[0];
        p.StartingUp = false;
        p.SetAnimation(PenguinAnimationId.Walker, FacingDirection.Left);
        p.X = 400;
        p.Y = 720 - p.Height;

        Assert.Same(p, sim.HitTest(410, 720 - p.Height / 2));
        Assert.Null(sim.HitTest(10, 10));

        // Click -> Eep on grab, Chirp + upward hop on release
        sim.BeginGrab(p);
        Assert.True(p.IsGrabbed);
        Assert.Equal(PenguinAnimationId.Tumbler, p.Animation);
        sim.ReleaseGrab(p, 0, 0, wasClick: true);
        Assert.False(p.IsGrabbed);
        Assert.True(p.VelocityY < 0);
        Assert.Contains(SoundEffectKind.Eep, sounds);
        Assert.Contains(SoundEffectKind.Chirp, sounds);

        // Drag & Throw -> moves center under cursor and throws with tumbler momentum
        sim.BeginGrab(p);
        sim.MoveGrab(p, 600, 300);
        Assert.Equal(600 - p.Width / 2, p.X);
        Assert.Equal(300 - p.Height / 2, p.Y);
        sim.ReleaseGrab(p, velocityPxPerSecX: 350, velocityPxPerSecY: -200, wasClick: false);
        Assert.False(p.IsGrabbed);
        Assert.True(p.VelocityX > 6.5);
        Assert.Equal(PenguinAnimationId.Tumbler, p.Animation);
    }

    [Fact]
    public void TopRightCorner_ReleaseGrabAtCorner_DoesNotFreezeOrSpinOutsideBounds()
    {
        var world = new CollisionWorld([new Rectangle(0, 0, 1920, 1080)]);
        var sim = new PenguinSimulation(world, scale: 1.0, rng: new Random(7));
        var settings = new AppSettings { PenguinCount = 1, NeverSplat = true, EnableParachutes = false };
        sim.EnsureCount(1);

        Penguin p = sim.Penguins[0];
        sim.BeginGrab(p);
        sim.MoveGrab(p, 1919, 1);
        Assert.True(world.IsInsideDesktop(p.Bounds));

        sim.ReleaseGrab(p, velocityPxPerSecX: 500, velocityPxPerSecY: -400, wasClick: false);

        for (int i = 0; i < 300; i++)
        {
            sim.Step(settings, deltaMs: 4.1667);
        }

        Assert.True(p.Y > 80, $"Penguin released at top-right corner should fall down cleanly instead of spinning in place (Y={p.Y}).");
    }

    [Fact]
    public void TopRightCorner_ChuteBetweenWindowAndMonitorEdge_EscapesInsteadOfPingPonging()
    {
        var window = new Rectangle(0, 0, 1888, 600);
        var world = new CollisionWorld([new Rectangle(0, 0, 1920, 1080)], [window]);
        var sim = new PenguinSimulation(world, scale: 1.0, rng: new Random(13));
        var settings = new AppSettings { PenguinCount = 1, NeverSplat = true, EnableParachutes = false };
        sim.EnsureCount(1);

        Penguin p = sim.Penguins[0];
        p.StartingUp = false;
        p.X = 1888;
        p.Y = 12;
        p.SetAnimation(PenguinAnimationId.Climber, FacingDirection.Left);
        p.SetVelocity(0, -3.5);

        for (int i = 0; i < 500; i++)
        {
            sim.Step(settings, deltaMs: 8.333);
        }

        Assert.True(p.Y > 200, $"Climber in top-right chute should fall out instead of bouncing at top-right (Y={p.Y}).");
    }

    [Fact]
    public void TopRightCorner_SurfaceNearCeiling_WalksAwayInsteadOfSpinningInCorner()
    {
        var window = new Rectangle(0, 34, 1920, 1000);
        var world = new CollisionWorld([new Rectangle(0, 0, 1920, 1080)], [window]);
        var settings = new AppSettings
        {
            PenguinCount = 1,
            NeverSplat = true,
            EnableIdlePauses = false,
            EnableParachutes = true
        };

        for (int seed = 0; seed < 10; seed++)
        {
            var sim = new PenguinSimulation(world, scale: 1.0, rng: new Random(seed));
            sim.EnsureCount(1);

            Penguin p = sim.Penguins[0];
            p.StartingUp = false;
            p.X = 1888;
            p.Y = 2;
            p.SetAnimation(PenguinAnimationId.Walker, FacingDirection.Right);
            p.SetVelocity(3.2, 0);

            for (int i = 0; i < 250; i++)
            {
                sim.Step(settings, deltaMs: 8.333);
            }

            Assert.True(p.X < 1850, $"Seed {seed}: penguin stayed trapped near top-right corner at X={p.X}, Anim={p.Animation}");
        }
    }

    [Fact]
    public void IdlePause_PausesWalkerOnStandingFrameAndResumes()
    {
        var world = new CollisionWorld([new Rectangle(0, 0, 1280, 720)]);
        var sim = new PenguinSimulation(world, scale: 1.0, rng: new Random(99));
        var settings = new AppSettings
        {
            PenguinCount = 1,
            EnableIdlePauses = true,
            EnableParachutes = false
        };
        sim.EnsureCount(1);

        Penguin p = sim.Penguins[0];
        p.StartingUp = false;
        p.SetAnimation(PenguinAnimationId.Walker, FacingDirection.Right);
        p.X = 200;
        p.Y = 720 - p.Height;
        p.SetVelocity(3.2, 0);

        bool observedIdle = false;
        for (int i = 0; i < 800; i++)
        {
            sim.Step(settings, deltaMs: 45);
            if (p.IsIdle)
            {
                observedIdle = true;
                int xAtIdleStart = p.X;
                Assert.Equal(0, p.FrameIndex);

                sim.Step(settings, deltaMs: 45);
                Assert.Equal(xAtIdleStart, p.X);

                // Exhaust idle duration (~1.4s max) and verify walking resumes.
                for (int j = 0; j < 40; j++)
                {
                    sim.Step(settings, deltaMs: 45);
                }

                Assert.False(p.IsIdle);
                break;
            }
        }

        Assert.True(observedIdle, "Walker should occasionally enter idle pause when EnableIdlePauses is enabled.");
    }

    [Fact]
    public void Parachute_SavesFastFallingTumblerFromSplatting()
    {
        var world = new CollisionWorld([new Rectangle(0, 0, 800, 800)]);
        var sim = new PenguinSimulation(world, scale: 1.0, rng: new Random(1));
        var settings = new AppSettings
        {
            PenguinCount = 1,
            NeverSplat = false,
            SplatDistance = 200,
            EnableParachutes = true
        };

        int explosions = 0;
        sim.SoundRequested += kind =>
        {
            if (kind == SoundEffectKind.Explosion) explosions++;
        };

        sim.EnsureCount(1);
        Penguin p = sim.Penguins[0];
        p.StartingUp = false;
        p.SetAnimation(PenguinAnimationId.Tumbler, FacingDirection.Left);
        p.TumbleStartY = 0;
        p.X = 300;
        p.Y = 260;
        p.SetVelocity(0, 4);

        bool deployedParachute = false;
        for (int i = 0; i < 400; i++)
        {
            sim.Step(settings, deltaMs: 20);
            if (p.Animation == PenguinAnimationId.Floater)
            {
                deployedParachute = true;
                break;
            }
        }

        Assert.True(deployedParachute, "Fast tumbler should open parachute before splatting when EnableParachutes is enabled.");
        Assert.Equal(0, explosions);
    }

    [Fact]
    public void SpriteCatalog_SampleExportAndPngThemeLoading_SupportsCustomResolutionAndSantaFallback()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"WinPenguinsThemeTest_{Guid.NewGuid():N}");
        try
        {
            // 1. Export sample PNG theme & roundtrip load it
            string sampleDir = SpriteCatalog.CreateSampleTheme(tempRoot, "SamplePack");
            Assert.True(File.Exists(Path.Combine(sampleDir, "theme.json")));
            Assert.True(File.Exists(Path.Combine(sampleDir, "walker.png")));

            SpriteCatalog loadedSample = SpriteCatalog.LoadTheme("SamplePack", 1.0, tempRoot);
            Assert.Equal("SamplePack", loadedSample.ThemeName);
            Assert.Equal(32, loadedSample.GetSheet(PenguinAnimationId.Walker).FrameWidth);

            // 2. Custom partial theme with 48x48 PNG Walker and omitted SantaWalker falls back cleanly
            string customDir = Path.Combine(tempRoot, "HdPack");
            Directory.CreateDirectory(customDir);
            string walkerPng = Path.Combine(customDir, "hd_walker.png");
            using (var bmp = new Bitmap(48 * 4, 48 * 2, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.FromArgb(200, 30, 144, 255));
                }
                bmp.Save(walkerPng, ImageFormat.Png);
            }

            File.WriteAllText(Path.Combine(customDir, "theme.json"), """
                {
                  "Name": "HdPack",
                  "Author": "Tests",
                  "Animations": {
                    "Walker": {
                      "Image": "hd_walker.png",
                      "FrameCount": 4,
                      "DirectionCount": 2,
                      "FrameWidth": 48,
                      "FrameHeight": 48,
                      "Cycles": true,
                      "ReverseRightRow": false
                    }
                  }
                }
                """);

            SpriteCatalog hdCatalog = SpriteCatalog.LoadTheme("HdPack", 1.0, tempRoot);
            Assert.Equal("HdPack", hdCatalog.ThemeName);
            Assert.Equal(48, hdCatalog.GetSpecsMap()[PenguinAnimationId.Walker].BaseWidth);
            Assert.Equal(4, hdCatalog.GetSpecsMap()[PenguinAnimationId.Walker].FrameCount);
            Assert.Same(hdCatalog.GetSheet(PenguinAnimationId.Walker), hdCatalog.GetSheet(PenguinAnimationId.SantaWalker));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void SoundPlayerService_ScaleWavPcmVolume_Scales8BitAnd16BitSamples()
    {
        byte[] wav8 = CreateSyntheticPcmWav(bitsPerSample: 8, sampleValue: 228); // +100 above silence 128
        byte[] scaled8 = SoundPlayerService.ScaleWavPcmVolume(wav8, volumePercent: 50);
        Assert.Equal(128 + 50, scaled8[44]);

        byte[] wav16 = CreateSyntheticPcmWav(bitsPerSample: 16, sampleValue: 20000);
        byte[] scaled16 = SoundPlayerService.ScaleWavPcmVolume(wav16, volumePercent: 25);
        short actual16 = BinaryPrimitives.ReadInt16LittleEndian(scaled16.AsSpan(44, 2));
        Assert.Equal(5000, actual16);
    }

    private static byte[] CreateSyntheticPcmWav(ushort bitsPerSample, int sampleValue)
    {
        int bytesPerSample = bitsPerSample / 8;
        int dataSize = bytesPerSample;
        var wav = new byte[44 + dataSize];
        "RIFF"u8.CopyTo(wav.AsSpan(0, 4));
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(4, 4), 36 + dataSize);
        "WAVEfmt "u8.CopyTo(wav.AsSpan(8, 8));
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(16, 4), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(wav.AsSpan(20, 2), 1); // PCM
        BinaryPrimitives.WriteUInt16LittleEndian(wav.AsSpan(22, 2), 1); // Mono
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(24, 4), 11025);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(28, 4), 11025 * bytesPerSample);
        BinaryPrimitives.WriteUInt16LittleEndian(wav.AsSpan(32, 2), (ushort)bytesPerSample);
        BinaryPrimitives.WriteUInt16LittleEndian(wav.AsSpan(34, 2), bitsPerSample);
        "data"u8.CopyTo(wav.AsSpan(36, 4));
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(40, 4), dataSize);

        if (bitsPerSample == 8)
        {
            wav[44] = (byte)sampleValue;
        }
        else
        {
            BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(44, 2), (short)sampleValue);
        }

        return wav;
    }
}

public sealed class SettingsTests
{
    [Fact]
    public void AppSettings_NormalizeClampsOutOfRangeValues()
    {
        var s = new AppSettings
        {
            PenguinCount = 999,
            FrameDelayMs = 1,
            Opacity = 0,
            SantaPercent = 250,
            SoundVolume = -25,
            TargetFps = 999,
            ThemeName = "  ",
            PauseHotkey = "",
            BuiltInSound = "invalid"
        };

        s.Normalize();

        Assert.Equal(AppSettings.MaxPenguins, s.PenguinCount);
        Assert.Equal(AppSettings.MinFrameDelayMs, s.FrameDelayMs);
        Assert.Equal((byte)15, s.Opacity);
        Assert.Equal(100, s.SantaPercent);
        Assert.Equal(0, s.SoundVolume);
        Assert.Equal(240, s.TargetFps);
        Assert.Equal("Classic", s.ThemeName);
        Assert.Equal("Ctrl+Alt+P", s.PauseHotkey);
        Assert.Equal("boom", s.BuiltInSound);
    }

    [Fact]
    public void AppSettings_ParseIgnoredProcessNames_NormalizesExeAndDeduplicates()
    {
        var s = new AppSettings
        {
            IgnoredProcesses = " spotify.exe, Discord ; obs64.EXE\nSPOTIFY\n  "
        };

        IReadOnlyList<string> parsed = s.ParseIgnoredProcessNames();
        Assert.Equal(["spotify", "Discord", "obs64"], parsed);
    }

    [Fact]
    public void GlobalHotkeyParser_ValidatesAndNormalizesShortcuts()
    {
        Assert.True(GlobalHotkeyParser.TryParse("control + shift + p", out uint mods, out uint vk, out string norm));
        Assert.Equal("Ctrl+Shift+P", norm);
        Assert.Equal((uint)'P', vk);
        Assert.NotEqual(0u, mods);

        Assert.True(GlobalHotkeyParser.TryParse("Alt+f9", out _, out uint vkF9, out string normF9));
        Assert.Equal("Alt+F9", normF9);
        Assert.Equal((uint)Keys.F9, vkF9);

        Assert.False(GlobalHotkeyParser.TryParse("P", out _, out _, out _)); // Requires modifier
        Assert.False(GlobalHotkeyParser.TryParse("Ctrl+Alt+", out _, out _, out _));
        Assert.False(GlobalHotkeyParser.TryParse("", out _, out _, out _));
    }
}

public sealed class NextLevelFeatureTests
{
    [Fact]
    public void CollisionWorld_FindFloorDistanceBelow_DetectsWindowAndMonitorFloor()
    {
        var world = new CollisionWorld(
            [new Rectangle(0, 0, 1920, 1080)],
            [new Rectangle(400, 500, 300, 200)]);

        Assert.Equal(28, world.FindFloorDistanceBelow(new Rectangle(450, 440, 32, 32), maxDistance: 120));
        Assert.Equal(28, world.FindFloorDistanceBelow(new Rectangle(100, 1020, 32, 32), maxDistance: 120));
        Assert.True(world.FindFloorDistanceBelow(new Rectangle(100, 100, 32, 32), maxDistance: 120) > 120);
    }

    [Fact]
    public void PenguinVisualTransforms_TumblerSpinsAndSquashRelaxes()
    {
        var p = new Penguin(100, 100, 6.0, 2.0, 1.0);
        p.SetAnimation(PenguinAnimationId.Tumbler, FacingDirection.Right);
        p.TriggerSquash(1.26f, 0.76f);

        for (int i = 0; i < 40; i++)
        {
            p.RelaxVisualTransforms(16.0);
        }

        Assert.NotEqual(0f, p.VisualRotationDeg);
        Assert.InRange(p.VisualScaleX, 0.98f, 1.02f);
        Assert.InRange(p.VisualScaleY, 0.98f, 1.02f);
    }

    [Fact]
    public void DesktopToys_TrampolineBounce_TwinPortalsAcrossMonitors_AndFishSnack()
    {
        var world = new CollisionWorld(
            [new Rectangle(0, 0, 1920, 1080), new Rectangle(1920, 0, 1920, 1080)],
            [new Rectangle(200, 800, 500, 120)],
            [1.0, 1.5]);
        var sim = new PenguinSimulation(world, scale: 1.0, rng: new Random(42));
        var settings = new AppSettings
        {
            PenguinCount = 1,
            ScaleMode = SpriteScaleMode.Auto,
            FrameDelayMs = 45,
            InteractiveMouse = false,
            EnableIdlePauses = false,
            EnableParachutes = false
        };

        // 1. Twin Portals across dual monitors
        sim.Toys.SpawnTwinPortals(world, new Random(1));
        DesktopToy portalA = Assert.Single(sim.Toys.Toys, t => t.Kind == ToyKind.PortalA);
        DesktopToy portalB = Assert.Single(sim.Toys.Toys, t => t.Kind == ToyKind.PortalB);
        Assert.True(portalA.X < 1920);
        Assert.True(portalB.X >= 1920);

        sim.EnsureCount(1);
        Penguin jumper = sim.Penguins[0];
        jumper.StartingUp = false;
        jumper.X = (int)portalA.X;
        jumper.Y = (int)portalA.Y;
        jumper.SetAnimation(PenguinAnimationId.Walker, FacingDirection.Right);
        jumper.SetVelocity(3.2, 0);

        sim.Step(settings, 45.0);
        Assert.True(jumper.X >= 1920);
        Assert.True(jumper.PortalCooldown > 0);
        Assert.Equal(1.5, jumper.Scale, precision: 3);

        // 2. Trampoline launch
        sim.Toys.Clear();
        DesktopToy tramp = sim.Toys.SpawnToy(ToyKind.Trampoline, 400, 786, 1.0);
        Penguin bouncer = sim.Penguins[0];
        bouncer.SetScale(1.0);
        bouncer.StartingUp = false;
        bouncer.PortalCooldown = 0;
        bouncer.BounceCooldown = 0;
        bouncer.X = 405;
        bouncer.Y = 760;
        bouncer.SetAnimation(PenguinAnimationId.Faller, FacingDirection.Left);
        bouncer.SetVelocity(2.0, 3.0);
        sim.Step(settings, 45.0);
        Assert.True(bouncer.VelocityY < 0);
        Assert.True(bouncer.BounceCooldown > 0);
        Assert.True(tramp.Pulse > 0);

        // 3. Fish snack eaten on contact
        sim.Toys.Clear();
        DesktopToy fish = sim.Toys.SpawnToy(ToyKind.Fish, 420, 786, 1.0);
        Penguin eater = sim.Penguins[0];
        eater.SetScale(1.0);
        eater.StartingUp = false;
        eater.X = 410;
        eater.Y = 768;
        eater.SetAnimation(PenguinAnimationId.Walker, FacingDirection.Right);
        eater.SetVelocity(3.2, 0);
        sim.Step(settings, 45.0);
        Assert.False(fish.Active);
    }

    [Fact]
    public void SnowSystem_FallsSettlesAndScattersUnderPenguin()
    {
        var world = new CollisionWorld(
            [new Rectangle(0, 0, 800, 600)],
            [new Rectangle(100, 250, 600, 100)]);
        var snow = new SnowSystem(new Random(7));
        var penguins = new List<Penguin>();

        bool hadSettled = false;
        float settledX = 0;
        float settledY = 0;
        for (int step = 0; step < 300; step++)
        {
            snow.Step(world, penguins, 40.0, enabled: true);
            ReadOnlySpan<SnowParticle> span = snow.Particles;
            for (int i = 0; i < span.Length; i++)
            {
                if (span[i].Active && span[i].Settled && span[i].Y >= 245 && span[i].Y <= 255)
                {
                    hadSettled = true;
                    settledX = span[i].X;
                    settledY = span[i].Y;
                    break;
                }
            }
            if (hadSettled) break;
        }

        Assert.True(hadSettled);

        var walker = new Penguin((int)settledX - 16, (int)settledY - 32, 3.0, 0, 1.0) { StartingUp = false };
        penguins.Add(walker);
        snow.Step(world, penguins, 16.0, enabled: true);

        bool stillSettledAtSameSpot = false;
        foreach (SnowParticle f in snow.Particles)
        {
            if (f.Active && f.Settled && Math.Abs(f.X - settledX) < 4 && Math.Abs(f.Y - settledY) < 4)
            {
                stillSettledAtSameSpot = true;
                break;
            }
        }
        Assert.False(stillSettledAtSameSpot);

        snow.Step(world, penguins, 16.0, enabled: false);
        foreach (SnowParticle f in snow.Particles)
        {
            Assert.False(f.Active);
        }
    }

    [Fact]
    public void PenguinToPenguin_BounceAndWalkerGreeting_ApplyCooldowns()
    {
        var world = new CollisionWorld([new Rectangle(0, 0, 1920, 1080)]);
        var sim = new PenguinSimulation(world, scale: 1.0, rng: new Random(123));
        var settings = new AppSettings
        {
            PenguinCount = 2,
            FrameDelayMs = 45,
            EnableIdlePauses = false,
            EnableParachutes = false
        };

        sim.EnsureCount(2);
        Penguin bottom = sim.Penguins[0];
        bottom.StartingUp = false;
        bottom.X = 500;
        bottom.Y = 1048;
        bottom.SetAnimation(PenguinAnimationId.Walker, FacingDirection.Right);
        bottom.SetVelocity(3.2, 0);

        Penguin top = sim.Penguins[1];
        top.StartingUp = false;
        top.X = 502;
        top.Y = 1024;
        top.SetAnimation(PenguinAnimationId.Faller, FacingDirection.Left);
        top.SetVelocity(0, 3.0);

        sim.Step(settings, 45.0);
        Assert.True(top.VelocityY < 0);
        Assert.True(top.BounceCooldown > 0);
        Assert.True(bottom.BounceCooldown > 0);

        Penguin leftWalker = sim.Penguins[0];
        leftWalker.StartingUp = false;
        leftWalker.BounceCooldown = 0;
        leftWalker.GreetingCooldown = 0;
        leftWalker.X = 600;
        leftWalker.Y = 1048;
        leftWalker.SetAnimation(PenguinAnimationId.Walker, FacingDirection.Right);
        leftWalker.SetVelocity(3.2, 0);

        Penguin rightWalker = sim.Penguins[1];
        rightWalker.StartingUp = false;
        rightWalker.BounceCooldown = 0;
        rightWalker.GreetingCooldown = 0;
        rightWalker.X = 608;
        rightWalker.Y = 1048;
        rightWalker.SetAnimation(PenguinAnimationId.Walker, FacingDirection.Left);
        rightWalker.SetVelocity(-3.2, 0);

        sim.Step(settings, 45.0);
        Assert.True(leftWalker.GreetingCooldown > 0);
        Assert.True(rightWalker.GreetingCooldown > 0);
    }

    [Fact]
    public void CursorPerch_DwellsOnStationaryCursorThenFlingsOnFastShake()
    {
        var world = new CollisionWorld([new Rectangle(0, 0, 1920, 1080)]);
        var sim = new PenguinSimulation(world, scale: 1.0, rng: new Random(9));
        var settings = new AppSettings
        {
            PenguinCount = 1,
            FrameDelayMs = 45,
            InteractiveMouse = true,
            CursorPerch = true,
            CursorAvoidance = false,
            EnableIdlePauses = false,
            EnableParachutes = false
        };

        sim.EnsureCount(1);
        Penguin p = sim.Penguins[0];
        p.StartingUp = false;
        p.IsPerching = true;
        p.X = 400;
        p.Y = 400;
        p.SetAnimation(PenguinAnimationId.Walker, FacingDirection.Right);
        p.SetVelocity(3.2, 0);

        sim.Step(settings, 45.0, new Point(420, 440));
        Assert.True(p.IsPerching);
        Assert.Equal(420 - p.Width / 2, p.X);
        Assert.Equal(440 - p.Height, p.Y);

        // Move cursor rapidly across > 780 px/s threshold within 20 ms -> flings penguin off!
        sim.Step(settings, 20.0, new Point(520, 440));
        Assert.False(p.IsPerching);
        Assert.True(p.VelocityY < 0);
    }

    [Fact]
    public void SpriteCatalog_SmoothHdTheme_PreservesLogicalSizeWithDoubleTextureResolution()
    {
        SpriteCatalog classic = SpriteCatalog.LoadDefault(1.0);
        SpriteCatalog smooth = SpriteCatalog.LoadSmoothHd(1.0);

        Assert.Equal("SmoothHD", smooth.ThemeName);
        SpriteSheet classicWalker = classic.Sheets[PenguinAnimationId.Walker];
        SpriteSheet smoothWalker = smooth.Sheets[PenguinAnimationId.Walker];

        Assert.Equal(classicWalker.FrameWidth, smoothWalker.FrameWidth);
        Assert.Equal(classicWalker.FrameHeight, smoothWalker.FrameHeight);
        Assert.Equal(classicWalker.TextureFrameWidth * 2, smoothWalker.TextureFrameWidth);
        Assert.Equal(classicWalker.TextureFrameHeight * 2, smoothWalker.TextureFrameHeight);
        Assert.Contains("SmoothHD", ThemeDiscovery.ListThemes().Select(t => t.Name));
    }

    [Fact]
    public void SpriteCatalog_ImportGifAndAseprite_CreatesLoadableThemes()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"WinPenguinsImportTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            // 1. GIF import
            string gifPath = Path.Combine(tempRoot, "mygifpet.gif");
            using (var gif = new Bitmap(24, 24, PixelFormat.Format32bppArgb))
            {
                using Graphics g = Graphics.FromImage(gif);
                g.Clear(Color.DeepSkyBlue);
                gif.Save(gifPath, ImageFormat.Gif);
            }

            (string gifName, string gifFolder) = SpriteCatalog.ImportThemeFromGifOrAseprite(gifPath, tempRoot);
            Assert.Equal("mygifpet", gifName);
            Assert.True(File.Exists(Path.Combine(gifFolder, "theme.json")));
            SpriteCatalog loadedGif = SpriteCatalog.LoadTheme(gifName, 1.0, tempRoot);
            Assert.Equal(24, loadedGif.Sheets[PenguinAnimationId.Walker].FrameWidth);

            // 2. Aseprite JSON + PNG import
            string asePng = Path.Combine(tempRoot, "hero_sheet.png");
            using (var sheet = new Bitmap(64, 32, PixelFormat.Format32bppArgb))
            {
                using Graphics g = Graphics.FromImage(sheet);
                g.Clear(Color.OrangeRed);
                sheet.Save(asePng, ImageFormat.Png);
            }

            string aseJson = Path.Combine(tempRoot, "HeroPet.json");
            File.WriteAllText(aseJson, """
            {
              "frames": [
                { "frame": { "x": 0, "y": 0, "w": 32, "h": 32 } },
                { "frame": { "x": 32, "y": 0, "w": 32, "h": 32 } }
              ],
              "meta": {
                "image": "hero_sheet.png"
              }
            }
            """);

            (string aseName, string aseFolder) = SpriteCatalog.ImportThemeFromGifOrAseprite(aseJson, tempRoot);
            Assert.Equal("HeroPet", aseName);
            Assert.True(File.Exists(Path.Combine(aseFolder, "theme.json")));
            SpriteCatalog loadedAse = SpriteCatalog.LoadTheme(aseName, 1.0, tempRoot);
            Assert.Equal(2, loadedAse.Sheets[PenguinAnimationId.Walker].Spec.FrameCount);
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public void AmbientMonitors_AudioAndFocusAndRecording_SafeAndMatchProcesses()
    {
        using var audio = new AudioMeterMonitor();
        Assert.Equal(0f, audio.SamplePeak(enabled: false));
        float peak = audio.SamplePeak(enabled: true);
        Assert.InRange(peak, 0f, 1f);

        _ = FocusAndRecordingMonitor.IsPresentationOrFocusBusy();

        var settings = new AppSettings
        {
            RecordingProcesses = "obs64.exe, streamlabs ; NonExistentApp"
        };
        IReadOnlyList<string> parsed = settings.ParseRecordingProcessNames();
        Assert.Equal(["obs64", "streamlabs", "NonExistentApp"], parsed);
        Assert.False(FocusAndRecordingMonitor.IsAnyProcessRunning(
            new HashSet<string>(["definitely_not_running_process_xyz"], StringComparer.OrdinalIgnoreCase)));
    }

    [Fact]
    public void DesktopToys_WalkerAt240HzSprintsWithoutFreezingAndEatsFish()
    {
        var world = new CollisionWorld([new Rectangle(0, 0, 1920, 1080)]);
        var sim = new PenguinSimulation(world, scale: 1.0, rng: new Random(55));
        var settings = new AppSettings
        {
            PenguinCount = 1,
            FrameDelayMs = 45,
            TargetFps = 240,
            EnableIdlePauses = false,
            EnableParachutes = false
        };

        sim.EnsureCount(1);
        Penguin walker = sim.Penguins[0];
        walker.StartingUp = false;
        walker.SpeedMultiplier = 1.0;
        walker.X = 400;
        walker.Y = 1080 - walker.Height;
        walker.SetAnimation(PenguinAnimationId.Walker, FacingDirection.Left);
        walker.SetVelocity(-3.2, 0);

        // Drop a fish 180px to the right on the floor and step at 240Hz (~4.16ms per frame).
        DesktopToy fish = sim.Toys.SpawnToy(ToyKind.Fish, 580, 1080 - 14, 1.0);
        for (int i = 0; i < 450 && fish.Active; i++)
        {
            sim.Step(settings, 4.167);
        }

        Assert.False(fish.Active, "240Hz walker should turn toward fish, keep moving continuously, and eat it without freezing.");
        Assert.Equal(1, sim.FishEatenCount);
        Assert.Equal(PenguinEmote.Heart, walker.Emote);
        Assert.InRange(Math.Abs(walker.VelocityX), 2.8, 3.6);
    }

    [Fact]
    public void DesktopToys_HitTestDragMovesToyAcrossMonitorsAndRemoveWorks()
    {
        var world = new CollisionWorld(
            [new Rectangle(0, 0, 1920, 1080), new Rectangle(1920, 0, 1920, 1080)],
            monitorScales: [1.0, 1.5]);
        var sim = new PenguinSimulation(world, scale: 1.0, rng: new Random(5));

        DesktopToy tramp = sim.Toys.SpawnToy(ToyKind.Trampoline, 400, 700, 1.0);
        Assert.Same(tramp, sim.HitTestToy(410, 705));

        sim.BeginToyGrab(tramp);
        Assert.True(tramp.IsGrabbed);
        sim.MoveToyGrab(tramp, 2200, 500);
        Assert.True(tramp.X >= 2100);
        Assert.Equal(1.5, tramp.Scale, precision: 3);

        sim.ReleaseToyGrab(tramp);
        Assert.False(tramp.IsGrabbed);

        sim.RemoveToy(tramp);
        Assert.Null(sim.HitTestToy(2200, 500));
        Assert.Empty(sim.Toys.Toys);
    }

    [Fact]
    public void PenguinEmotes_TriggerAndRelaxOverTime()
    {
        var p = new Penguin(100, 100, 3.2, 0, 1.0);
        p.TriggerEmote(PenguinEmote.Music, 0.5);
        Assert.Equal(PenguinEmote.Music, p.Emote);

        p.RelaxVisualTransforms(300.0);
        Assert.Equal(PenguinEmote.Music, p.Emote);

        p.RelaxVisualTransforms(250.0);
        Assert.Equal(PenguinEmote.None, p.Emote);
    }

    [Fact]
    public void DCompOverlayWindow_DetectsDxgiDeviceLostHResults()
    {
        Assert.True(DCompOverlayWindow.IsDeviceLostHResult(unchecked((int)0x887A0005)));
        Assert.True(DCompOverlayWindow.IsDeviceLostHResult(unchecked((int)0x887A0007)));
        Assert.True(DCompOverlayWindow.IsDeviceLostHResult(unchecked((int)0x8899000C)));
        Assert.False(DCompOverlayWindow.IsDeviceLostHResult(0));
    }
}

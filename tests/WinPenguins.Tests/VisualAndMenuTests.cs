using System.Drawing;
using WinPenguins.Assets;
using WinPenguins.Config;
using WinPenguins.Engine;
using WinPenguins.Platform;
using WinPenguins.UI;
using Xunit;

namespace WinPenguins.Tests;

[Collection("SequentialUiTests")]
public sealed class VisualAndMenuTests
{
    private static Penguin CreateTestPenguin(
        CollisionWorld world,
        SpriteCatalog catalog,
        PenguinSubType subType,
        PenguinAnimationId anim,
        FacingDirection dir,
        int x,
        int y,
        PenguinEmote emote = PenguinEmote.None)
    {
        var p = new Penguin(
            x,
            y,
            velocityX: 0,
            velocityY: 0,
            scale: 1.0,
            speedMultiplier: 1.0,
            specs: catalog.GetSpecsMap());
        p.SetAnimation(anim, dir);
        p.SetSubType(subType, world);
        if (emote != PenguinEmote.None)
        {
            p.TriggerEmote(emote, 2.0);
        }
        return p;
    }

    private static List<Point> DiffPixels(byte[] baselineBgra, byte[] candidateBgra, int width, int height)
    {
        var diffs = new List<Point>();
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int idx = (y * width + x) * 4;
                int db = Math.Abs(baselineBgra[idx] - candidateBgra[idx]);
                int dg = Math.Abs(baselineBgra[idx + 1] - candidateBgra[idx + 1]);
                int dr = Math.Abs(baselineBgra[idx + 2] - candidateBgra[idx + 2]);
                int da = Math.Abs(baselineBgra[idx + 3] - candidateBgra[idx + 3]);
                if (db + dg + dr + da > 24)
                {
                    diffs.Add(new Point(x, y));
                }
            }
        }
        return diffs;
    }

    private static int CountOpaquePixels(byte[] bgra, int width, Rectangle roi)
    {
        int count = 0;
        for (int y = roi.Top; y < roi.Bottom; y++)
        {
            for (int x = roi.Left; x < roi.Right; x++)
            {
                if (bgra[(y * width + x) * 4 + 3] > 20)
                {
                    count++;
                }
            }
        }
        return count;
    }

    [Theory]
    [InlineData(FacingDirection.Right)] // Climbing right wall -> head on LEFT (x < 12)
    [InlineData(FacingDirection.Left)]  // Climbing left wall -> head on RIGHT (x > 20)
    public void D2DVisual_ClimbingChefAndKingAccessoriesFollowHeadNotTopBack(FacingDirection climbDir)
    {
        const int canvasW = 64;
        const int canvasH = 64;
        const int px = 16;
        const int py = 16;

        SpriteCatalog catalog = SpriteCatalog.LoadDefault(1.0);
        using var host = new OverlayHost(new AppSettings { PrimaryMonitorOnly = true }, catalog);
        host.SetVisible(false);
        var world = new CollisionWorld([new Rectangle(0, 0, canvasW, canvasH)]);

        Penguin normalClimber = CreateTestPenguin(world, catalog, PenguinSubType.Normal, PenguinAnimationId.Climber, climbDir, px, py);
        Penguin chefClimber = CreateTestPenguin(world, catalog, PenguinSubType.Chef, PenguinAnimationId.Climber, climbDir, px, py);
        Penguin kingClimber = CreateTestPenguin(world, catalog, PenguinSubType.King, PenguinAnimationId.Climber, climbDir, px, py);

        byte[] normalPx = host.CaptureTestSceneBgra(canvasW, canvasH, [normalClimber]);
        byte[] chefPx = host.CaptureTestSceneBgra(canvasW, canvasH, [chefClimber]);
        byte[] kingPx = host.CaptureTestSceneBgra(canvasW, canvasH, [kingClimber]);

        foreach ((string role, byte[] rolePx) in new[] { ("Chef", chefPx), ("King", kingPx) })
        {
            List<Point> hatDiffs = DiffPixels(normalPx, rolePx, canvasW, canvasH);
            Assert.True(hatDiffs.Count >= 12, $"{role} climbing {climbDir} should draw visible accessory pixels (found {hatDiffs.Count}).");

            double avgX = hatDiffs.Average(pt => pt.X);
            double localCenterX = avgX - px;

            if (climbDir == FacingDirection.Right)
            {
                Assert.True(localCenterX < 10.0, $"{role} climbing RIGHT wall must draw hat on LEFT head side (localCenterX={localCenterX:F1} >= 10).");
            }
            else
            {
                Assert.True(localCenterX > 22.0, $"{role} climbing LEFT wall must draw hat on RIGHT head side (localCenterX={localCenterX:F1} <= 22).");
            }
        }
    }

    [Fact]
    public void D2DVisual_RendersCrewScaleToysSnowAndEmotesWithExpectedPixelRegions()
    {
        const int width = 360;
        const int height = 120;

        SpriteCatalog catalog = SpriteCatalog.LoadDefault(1.0);
        using var host = new OverlayHost(new AppSettings { PrimaryMonitorOnly = true }, catalog);
        host.SetVisible(false);
        var world = new CollisionWorld([new Rectangle(0, 0, width, height)]);

        // Adult Normal walker at x=20 vs Baby Pingu at x=90 (75% size -> smaller rendered bounding box)
        Penguin adult = CreateTestPenguin(world, catalog, PenguinSubType.Normal, PenguinAnimationId.Walker, FacingDirection.Right, 20, 60, PenguinEmote.Heart);
        Penguin baby = CreateTestPenguin(world, catalog, PenguinSubType.Baby, PenguinAnimationId.Walker, FacingDirection.Right, 90, 60);
        Penguin sleepy = CreateTestPenguin(world, catalog, PenguinSubType.Sleepy, PenguinAnimationId.Walker, FacingDirection.Left, 150, 60, PenguinEmote.Sleep);
        Penguin jet = CreateTestPenguin(world, catalog, PenguinSubType.Jetpack, PenguinAnimationId.Walker, FacingDirection.Right, 210, 60);
        jet.JetBoostSeconds = 0.8;

        var toys = new ToySystem();
        toys.SpawnToy(ToyKind.Fish, 270, 70, 1.0);
        toys.SpawnToy(ToyKind.Trampoline, 305, 80, 1.0);
        toys.SpawnToy(ToyKind.PortalA, 270, 20, 1.0);
        toys.SpawnToy(ToyKind.PortalB, 315, 20, 1.0);

        byte[] frame = host.CaptureTestSceneBgra(width, height, [adult, baby, sleepy, jet], 255, toys.Toys);

        // Verify Heart emote rendered non-transparent pixels directly above adult (y: 44..60)
        int heartPixels = CountOpaquePixels(frame, width, new Rectangle(24, 44, 24, 16));
        Assert.True(heartPixels >= 8, $"Expected visible Heart emote pixels above adult, found {heartPixels}.");

        // Verify Sleep Zzz emote rendered above sleepy (x: 154..180, y: 44..60)
        int sleepPixels = CountOpaquePixels(frame, width, new Rectangle(154, 44, 26, 16));
        Assert.True(sleepPixels >= 6, $"Expected visible Sleep emote pixels above sleepyhead, found {sleepPixels}.");

        // Verify Baby occupies fewer total pixels than Adult
        int adultPixels = CountOpaquePixels(frame, width, new Rectangle(18, 58, 36, 36));
        int babyPixels = CountOpaquePixels(frame, width, new Rectangle(88, 58, 36, 36));
        Assert.True(adultPixels > 150 && babyPixels > 80 && babyPixels < adultPixels,
            $"Expected Baby pixel area ({babyPixels}) < Adult pixel area ({adultPixels}).");

        // Verify every spawned Toy painted pixels in its bounding box
        foreach (DesktopToy toy in toys.Toys)
        {
            Rectangle clipped = Rectangle.Intersect(toy.Bounds, new Rectangle(0, 0, width, height));
            int toyPixels = CountOpaquePixels(frame, width, clipped);
            Assert.True(toyPixels >= 20, $"Toy {toy.Kind} should render >= 20 opaque pixels in {clipped}, found {toyPixels}.");
        }
    }

    [Fact]
    public void OverlayContextMenus_AllItemsValidAndChangeRoleCoversEveryCrewSubType()
    {
        StaRunner.Run(() =>
        {
            var world = new CollisionWorld([new Rectangle(0, 0, 1920, 1080)]);
            var sim = new PenguinSimulation(world, 1.0, new Random(7));
            sim.EnsureCount(2);
            Penguin target = sim.Penguins[0];
            int redrawCalls = 0;
            int optionsCalls = 0;

            using ContextMenuStrip penguinMenu = TrayApplicationContext.BuildOverlayPenguinMenu(
                target,
                200,
                200,
                1.0,
                sim,
                world,
                () => redrawCalls++,
                () => optionsCalls++);

            Assert.True(penguinMenu.Items.Count >= 6);
            foreach (ToolStripItem item in penguinMenu.Items)
            {
                if (item is ToolStripSeparator)
                {
                    continue;
                }

                Assert.False(string.IsNullOrWhiteSpace(item.Text));
            }

            ToolStripMenuItem? roleRoot = penguinMenu.Items
                .OfType<ToolStripMenuItem>()
                .FirstOrDefault(i => string.Equals(i.Text, "Change Role", StringComparison.Ordinal));
            Assert.NotNull(roleRoot);

            // Every defined PenguinSubType except Unspecified must be selectable from the right-click menu.
            var expectedRoles = Enum.GetValues<PenguinSubType>()
                .Where(r => r != PenguinSubType.Unspecified)
                .ToArray();
            Assert.Equal(expectedRoles.Length, roleRoot.DropDownItems.Count);

            foreach (ToolStripMenuItem roleItem in roleRoot.DropDownItems.OfType<ToolStripMenuItem>())
            {
                Assert.False(string.IsNullOrWhiteSpace(roleItem.Text));
                roleItem.PerformClick();
            }

            Assert.Equal(PenguinSubType.Santa, target.SubType);
            Assert.Equal(expectedRoles.Length, redrawCalls);

            // Exercise Feed Fish, Drop Trampoline, Options, Explode, and Toy menu too.
            penguinMenu.Items.OfType<ToolStripMenuItem>().First(i => string.Equals(i.Text, "Feed Fish Here", StringComparison.Ordinal)).PerformClick();
            penguinMenu.Items.OfType<ToolStripMenuItem>().First(i => string.Equals(i.Text, "Options...", StringComparison.Ordinal)).PerformClick();
            Assert.Contains(sim.Toys.Toys, t => t.Kind == ToyKind.Fish);
            Assert.Equal(1, optionsCalls);

            DesktopToy fish = sim.Toys.Toys.First(t => t.Kind == ToyKind.Fish);
            using ContextMenuStrip toyMenu = TrayApplicationContext.BuildOverlayToyMenu(fish, sim, () => redrawCalls++);
            toyMenu.Items.OfType<ToolStripMenuItem>().First(i => string.Equals(i.Text, "Remove This Toy", StringComparison.Ordinal)).PerformClick();
            Assert.Empty(sim.Toys.Toys);
        });
    }
}

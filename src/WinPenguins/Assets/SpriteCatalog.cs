using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using WinPenguins.Engine;

namespace WinPenguins.Assets;

public sealed class SpriteCatalog
{
    private readonly Dictionary<PenguinAnimationId, SpriteSheet> _sheets;

    public string ThemeName { get; }
    public double Scale { get; }
    public IReadOnlyDictionary<PenguinAnimationId, SpriteSheet> Sheets => _sheets;

    private SpriteCatalog(string themeName, Dictionary<PenguinAnimationId, SpriteSheet> sheets, double scale)
    {
        ThemeName = themeName;
        _sheets = sheets;
        Scale = scale;
    }

    public static SpriteCatalog LoadDefault(double scale)
    {
        var sheets = new Dictionary<PenguinAnimationId, SpriteSheet>(AnimationCatalogSpecs.All.Count);
        foreach ((PenguinAnimationId id, AnimationSpec spec) in AnimationCatalogSpecs.All)
        {
            sheets[id] = SpriteSheet.LoadFromEmbeddedMaskedBmp(spec, scale);
        }

        return new SpriteCatalog("Classic", sheets, scale);
    }

    public static SpriteCatalog LoadTheme(string themeName, double scale, string? customRootDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(themeName) ||
            themeName.Equals("Classic", StringComparison.OrdinalIgnoreCase))
        {
            return LoadDefault(scale);
        }

        ThemeDescriptor? descriptor = ThemeDiscovery.ListThemes(customRootDirectory)
            .FirstOrDefault(t => !t.IsBuiltIn && t.Name.Equals(themeName, StringComparison.OrdinalIgnoreCase));

        if (descriptor?.FolderPath == null)
        {
            return LoadDefault(scale);
        }

        string manifestPath = Path.Combine(descriptor.FolderPath, "theme.json");
        ThemeManifest? manifest = ThemeDiscovery.TryLoadManifest(manifestPath);
        if (manifest == null)
        {
            return LoadDefault(scale);
        }

        var sheets = new Dictionary<PenguinAnimationId, SpriteSheet>(AnimationCatalogSpecs.All.Count);
        foreach ((PenguinAnimationId id, AnimationSpec defaultSpec) in AnimationCatalogSpecs.All)
        {
            if (manifest.Animations.TryGetValue(id, out ThemeAnimationEntry? entry) &&
                !string.IsNullOrWhiteSpace(entry.Image))
            {
                string fullImagePath = Path.GetFullPath(Path.Combine(descriptor.FolderPath, entry.Image));
                if (File.Exists(fullImagePath))
                {
                    try
                    {
                        var customSpec = new AnimationSpec(
                            id,
                            defaultSpec.ResourcePrefix,
                            Math.Max(1, entry.FrameCount),
                            Math.Clamp(entry.DirectionCount, 1, 2),
                            Math.Max(4, entry.FrameWidth),
                            Math.Max(4, entry.FrameHeight),
                            entry.Cycles);
                        sheets[id] = SpriteSheet.LoadFromPng(customSpec, fullImagePath, entry.ReverseRightRow, scale);
                        continue;
                    }
                    catch
                    {
                        // Fall back to default sheet below.
                    }
                }
            }

            sheets[id] = SpriteSheet.LoadFromEmbeddedMaskedBmp(defaultSpec, scale);
        }

        // If custom theme provided Walker/Climber without Santa variants, reuse custom base sprites.
        if (!manifest.Animations.ContainsKey(PenguinAnimationId.SantaWalker) &&
            manifest.Animations.ContainsKey(PenguinAnimationId.Walker))
        {
            sheets[PenguinAnimationId.SantaWalker] = sheets[PenguinAnimationId.Walker];
        }

        if (!manifest.Animations.ContainsKey(PenguinAnimationId.SantaClimber) &&
            manifest.Animations.ContainsKey(PenguinAnimationId.Climber))
        {
            sheets[PenguinAnimationId.SantaClimber] = sheets[PenguinAnimationId.Climber];
        }

        return new SpriteCatalog(descriptor.Name, sheets, scale);
    }

    public SpriteSheet GetSheet(PenguinAnimationId id) => _sheets[id];

    public SpriteFrame GetFrame(Penguin penguin) =>
        _sheets[penguin.Animation].GetFrame(penguin.Direction, penguin.FrameIndex);

    public IReadOnlyDictionary<PenguinAnimationId, AnimationSpec> GetSpecsMap()
    {
        var map = new Dictionary<PenguinAnimationId, AnimationSpec>(_sheets.Count);
        foreach ((PenguinAnimationId id, SpriteSheet sheet) in _sheets)
        {
            map[id] = sheet.Spec;
        }
        return map;
    }

    /// <summary>
    /// Exports built-in classic sprites as transparent PNG sprite sheets plus theme.json
    /// so users can customize animations easily.
    /// </summary>
    public static string CreateSampleTheme(string? themesRootDirectory = null, string themeName = "SampleCustom")
    {
        string root = string.IsNullOrWhiteSpace(themesRootDirectory)
            ? ThemeDiscovery.UserThemesDirectory
            : themesRootDirectory;
        string targetDir = Path.Combine(root, themeName);
        Directory.CreateDirectory(targetDir);

        SpriteCatalog classic = LoadDefault(1.0);
        var manifest = new ThemeManifest
        {
            Name = themeName,
            Author = "WinPenguins Sample",
            Animations = []
        };

        foreach ((PenguinAnimationId id, SpriteSheet sheet) in classic.Sheets)
        {
            string fileName = $"{id.ToString().ToLowerInvariant()}.png";
            string pngPath = Path.Combine(targetDir, fileName);
            SavePremultipliedSheetAsPng(sheet.SheetPixels, sheet.SheetWidth, sheet.SheetHeight, pngPath);

            manifest.Animations[id] = new ThemeAnimationEntry
            {
                Image = fileName,
                FrameCount = sheet.Spec.FrameCount,
                DirectionCount = sheet.Spec.DirectionCount,
                FrameWidth = sheet.FrameWidth,
                FrameHeight = sheet.FrameHeight,
                Cycles = sheet.Spec.Cycles,
                ReverseRightRow = false
            };
        }

        var jsonOpts = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };
        File.WriteAllText(Path.Combine(targetDir, "theme.json"), JsonSerializer.Serialize(manifest, jsonOpts));
        return targetDir;
    }

    private static void SavePremultipliedSheetAsPng(uint[] pixels, int width, int height, string path)
    {
        var argb = new int[pixels.Length];
        for (int i = 0; i < pixels.Length; i++)
        {
            uint px = pixels[i];
            byte a = (byte)(px >> 24);
            if (a == 0)
            {
                argb[i] = 0;
                continue;
            }

            if (a == 255)
            {
                argb[i] = unchecked((int)px);
                continue;
            }

            int r = Math.Clamp((int)((((px >> 16) & 0xFF) * 255 + a / 2) / a), 0, 255);
            int g = Math.Clamp((int)((((px >> 8) & 0xFF) * 255 + a / 2) / a), 0, 255);
            int b = Math.Clamp((int)(((px & 0xFF) * 255 + a / 2) / a), 0, 255);
            argb[i] = (a << 24) | (r << 16) | (g << 8) | b;
        }

        using var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        BitmapData data = bmp.LockBits(
            new Rectangle(0, 0, width, height),
            ImageLockMode.WriteOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            Marshal.Copy(argb, 0, data.Scan0, argb.Length);
        }
        finally
        {
            bmp.UnlockBits(data);
        }

        bmp.Save(path, ImageFormat.Png);
    }
}

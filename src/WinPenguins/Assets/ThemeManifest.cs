using System.Text.Json;
using System.Text.Json.Serialization;
using WinPenguins.Engine;

namespace WinPenguins.Assets;

public sealed class ThemeAnimationEntry
{
    public string Image { get; set; } = string.Empty;
    public int FrameCount { get; set; } = 8;
    public int DirectionCount { get; set; } = 1;
    public int FrameWidth { get; set; } = 32;
    public int FrameHeight { get; set; } = 32;
    public bool Cycles { get; set; } = true;
    public bool ReverseRightRow { get; set; } = false;
}

public sealed class ThemeManifest
{
    public string Name { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public Dictionary<PenguinAnimationId, ThemeAnimationEntry> Animations { get; set; } = [];
}

public sealed record ThemeDescriptor(string Name, string? FolderPath, bool IsBuiltIn);

public static class ThemeDiscovery
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string UserThemesDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinPenguins", "themes");

    public static string AppThemesDirectory =>
        Path.Combine(AppContext.BaseDirectory, "themes");

    public static IReadOnlyList<ThemeDescriptor> ListThemes(string? extraRootDirectory = null)
    {
        var result = new List<ThemeDescriptor>
        {
            new("Classic", null, IsBuiltIn: true),
            new("SmoothHD", null, IsBuiltIn: true)
        };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Classic", "SmoothHD" };

        IEnumerable<string> roots = string.IsNullOrWhiteSpace(extraRootDirectory)
            ? [UserThemesDirectory, AppThemesDirectory]
            : [extraRootDirectory, UserThemesDirectory, AppThemesDirectory];

        foreach (string root in roots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (string dir in Directory.GetDirectories(root))
            {
                string manifestPath = Path.Combine(dir, "theme.json");
                if (!File.Exists(manifestPath))
                {
                    continue;
                }

                ThemeManifest? manifest = TryLoadManifest(manifestPath);
                string themeName = !string.IsNullOrWhiteSpace(manifest?.Name)
                    ? manifest.Name.Trim()
                    : Path.GetFileName(dir);

                if (seen.Add(themeName))
                {
                    result.Add(new ThemeDescriptor(themeName, dir, IsBuiltIn: false));
                }
            }
        }

        return result;
    }

    public static ThemeManifest? TryLoadManifest(string manifestPath)
    {
        try
        {
            string json = File.ReadAllText(manifestPath);
            return JsonSerializer.Deserialize<ThemeManifest>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }
}

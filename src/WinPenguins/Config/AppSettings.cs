using System.Text.Json.Serialization;

namespace WinPenguins.Config;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SpriteScaleMode
{
    Auto,
    X1,
    X1_5,
    X2,
    X3
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OverlayLayerMode
{
    AboveWindows,
    DesktopOnly
}

public sealed class AppSettings
{
    public const int MinPenguins = 1;
    public const int MaxPenguins = 100;
    public const int MinFrameDelayMs = 15;
    public const int MaxFrameDelayMs = 150;

    public int PenguinCount { get; set; } = 8;
    public int FrameDelayMs { get; set; } = 45;
    public bool NeverSplat { get; set; } = true;
    public int SplatDistance { get; set; } = 500;
    public byte Opacity { get; set; } = 255;
    public int SantaPercent { get; set; } = 0;

    public bool EnableIdlePauses { get; set; } = true;
    public bool EnableParachutes { get; set; } = true;

    public bool SoundEnabled { get; set; } = false;
    public int SoundVolume { get; set; } = 75;
    public bool EnableActionSounds { get; set; } = true;
    public string BuiltInSound { get; set; } = "boom";
    public string CustomSoundPath { get; set; } = string.Empty;

    public string ThemeName { get; set; } = "Classic";
    public SpriteScaleMode ScaleMode { get; set; } = SpriteScaleMode.Auto;
    public OverlayLayerMode LayerMode { get; set; } = OverlayLayerMode.AboveWindows;
    public int TargetFps { get; set; } = 120;
    public bool IgnoreMaximizedWindows { get; set; } = true;
    public bool HideInFullscreen { get; set; } = true;
    public bool PauseOnBatterySaver { get; set; } = false;
    public bool PrimaryMonitorOnly { get; set; } = false;
    public bool InteractiveMouse { get; set; } = true;
    public bool CursorAvoidance { get; set; } = false;
    public bool CursorPerch { get; set; } = true;
    public bool EnableSnow { get; set; } = false;
    public bool EnableMusicDancing { get; set; } = true;
    public bool PauseOnPresentationOrFocus { get; set; } = false;
    public bool PauseWhenRecording { get; set; } = false;
    public string RecordingProcesses { get; set; } = "obs64,streamlabs,bandicam,xsplit";
    public bool LaunchAtStartup { get; set; } = false;

    public bool EnablePauseHotkey { get; set; } = true;
    public string PauseHotkey { get; set; } = "Ctrl+Alt+P";
    public string IgnoredProcesses { get; set; } = string.Empty;

    public AppSettings Clone() => (AppSettings)MemberwiseClone();

    public void Normalize()
    {
        PenguinCount = Math.Clamp(PenguinCount, MinPenguins, MaxPenguins);
        FrameDelayMs = Math.Clamp(FrameDelayMs, MinFrameDelayMs, MaxFrameDelayMs);
        SplatDistance = Math.Clamp(SplatDistance, 50, 4000);
        Opacity = Math.Clamp(Opacity, (byte)15, (byte)255);
        SantaPercent = Math.Clamp(SantaPercent, 0, 100);
        SoundVolume = Math.Clamp(SoundVolume, 0, 100);
        TargetFps = TargetFps switch
        {
            60 or 120 or 240 => TargetFps,
            _ => Math.Clamp(TargetFps, 30, 240)
        };
        BuiltInSound = BuiltInSound switch
        {
            "boom" or "eep" or "quack" => BuiltInSound,
            _ => "boom"
        };
        ThemeName = string.IsNullOrWhiteSpace(ThemeName) ? "Classic" : ThemeName.Trim();
        PauseHotkey = string.IsNullOrWhiteSpace(PauseHotkey) ? "Ctrl+Alt+P" : PauseHotkey.Trim();
        IgnoredProcesses ??= string.Empty;
        RecordingProcesses ??= string.Empty;
        CustomSoundPath ??= string.Empty;
    }

    public IReadOnlyList<string> ParseIgnoredProcessNames() =>
        ParseProcessList(IgnoredProcesses);

    public IReadOnlyList<string> ParseRecordingProcessNames() =>
        ParseProcessList(RecordingProcesses);

    private static IReadOnlyList<string> ParseProcessList(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<string>();
        }

        return raw
            .Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? p[..^4] : p)
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public double ResolveScaleFactor(double monitorDpiScale)
    {
        return ScaleMode switch
        {
            SpriteScaleMode.X1 => 1.0,
            SpriteScaleMode.X1_5 => 1.5,
            SpriteScaleMode.X2 => 2.0,
            SpriteScaleMode.X3 => 3.0,
            _ => Math.Clamp(Math.Round(monitorDpiScale * 2.0) / 2.0, 1.0, 3.0)
        };
    }
}

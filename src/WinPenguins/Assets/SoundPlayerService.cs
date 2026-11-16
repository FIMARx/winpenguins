using System.Buffers.Binary;
using System.Media;
using System.Reflection;
using WinPenguins.Config;
using WinPenguins.Engine;

namespace WinPenguins.Assets;

public sealed class SoundPlayerService : IDisposable
{
    private readonly object _sync = new();
    private SoundPlayer? _player;
    private MemoryStream? _memoryStream;
    private string _loadedKey = string.Empty;
    private readonly Dictionary<SoundEffectKind, long> _lastPlayedTicks = [];

    public void PlayEvent(SoundEffectKind kind, AppSettings settings)
    {
        if (!settings.SoundEnabled || settings.SoundVolume <= 0)
        {
            return;
        }

        if (kind != SoundEffectKind.Explosion && !settings.EnableActionSounds)
        {
            return;
        }

        long cooldownMs = kind == SoundEffectKind.Explosion ? 75 : 320;
        long now = Environment.TickCount64;
        if (_lastPlayedTicks.TryGetValue(kind, out long lastTick) && (now - lastTick) < cooldownMs)
        {
            return;
        }

        _lastPlayedTicks[kind] = now;

        string soundId = kind switch
        {
            SoundEffectKind.Chirp => "quack",
            SoundEffectKind.Eep => "eep",
            _ => settings.BuiltInSound
        };
        string customPath = kind == SoundEffectKind.Explosion ? settings.CustomSoundPath : string.Empty;
        PlayCore(soundId, customPath, settings.SoundVolume);
    }

    public void PlayTest(string builtInSound, string customSoundPath, int volumePercent) =>
        PlayCore(builtInSound, customSoundPath, volumePercent);

    private void PlayCore(string builtInSound, string customSoundPath, int volumePercent)
    {
        if (volumePercent <= 0)
        {
            return;
        }

        lock (_sync)
        {
            try
            {
                bool useFile = !string.IsNullOrWhiteSpace(customSoundPath) && File.Exists(customSoundPath);
                string targetKey = useFile
                    ? $"file:{customSoundPath}@{volumePercent}"
                    : $"res:{builtInSound}@{volumePercent}";

                if (_player == null || !string.Equals(_loadedKey, targetKey, StringComparison.OrdinalIgnoreCase))
                {
                    DisposePlayerUnlocked();

                    byte[]? rawBytes = LoadWavBytes(useFile ? customSoundPath : null, builtInSound);
                    if (rawBytes == null)
                    {
                        return;
                    }

                    byte[] scaledBytes = ScaleWavPcmVolume(rawBytes, volumePercent);
                    _memoryStream = new MemoryStream(scaledBytes, writable: false);
                    _player = new SoundPlayer(_memoryStream);
                    _player.Load();
                    _loadedKey = targetKey;
                }

                _player?.Play();
            }
            catch
            {
                // Never crash the animation loop if audio device or custom WAV fails.
            }
        }
    }

    public static byte[] ScaleWavPcmVolume(byte[] wavBytes, int volumePercent)
    {
        volumePercent = Math.Clamp(volumePercent, 0, 100);
        if (volumePercent == 100 || wavBytes.Length < 44)
        {
            return wavBytes;
        }

        // Verify RIFF/WAVE signature.
        if (wavBytes[0] != (byte)'R' || wavBytes[1] != (byte)'I' || wavBytes[2] != (byte)'F' || wavBytes[3] != (byte)'F' ||
            wavBytes[8] != (byte)'W' || wavBytes[9] != (byte)'A' || wavBytes[10] != (byte)'V' || wavBytes[11] != (byte)'E')
        {
            return wavBytes;
        }

        byte[] copy = (byte[])wavBytes.Clone();
        double gain = volumePercent / 100.0;
        ushort audioFormat = 0;
        ushort bitsPerSample = 0;

        int pos = 12;
        while (pos + 8 <= copy.Length)
        {
            uint chunkId = BinaryPrimitives.ReadUInt32LittleEndian(copy.AsSpan(pos, 4));
            int chunkSize = BinaryPrimitives.ReadInt32LittleEndian(copy.AsSpan(pos + 4, 4));
            int dataStart = pos + 8;
            if (chunkSize < 0 || dataStart + chunkSize > copy.Length)
            {
                break;
            }

            if (chunkId == 0x20746D66 && chunkSize >= 16) // "fmt "
            {
                audioFormat = BinaryPrimitives.ReadUInt16LittleEndian(copy.AsSpan(dataStart, 2));
                bitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian(copy.AsSpan(dataStart + 14, 2));
            }
            else if (chunkId == 0x61746164 && audioFormat == 1) // "data" & PCM
            {
                if (bitsPerSample == 8)
                {
                    for (int i = dataStart; i < dataStart + chunkSize; i++)
                    {
                        int centered = copy[i] - 128;
                        int scaled = (int)Math.Round(centered * gain) + 128;
                        copy[i] = (byte)Math.Clamp(scaled, 0, 255);
                    }
                }
                else if (bitsPerSample == 16)
                {
                    for (int i = dataStart; i + 1 < dataStart + chunkSize; i += 2)
                    {
                        short sample = BinaryPrimitives.ReadInt16LittleEndian(copy.AsSpan(i, 2));
                        int scaled = (int)Math.Round(sample * gain);
                        BinaryPrimitives.WriteInt16LittleEndian(copy.AsSpan(i, 2), (short)Math.Clamp(scaled, short.MinValue, short.MaxValue));
                    }
                }
                break;
            }

            pos = dataStart + chunkSize + (chunkSize & 1);
        }

        return copy;
    }

    private static byte[]? LoadWavBytes(string? filePath, string builtInSound)
    {
        if (filePath != null)
        {
            return File.ReadAllBytes(filePath);
        }

        using Stream? resStream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream($"WinPenguins.Assets.{builtInSound}.wav");
        if (resStream == null)
        {
            return null;
        }

        using var ms = new MemoryStream();
        resStream.CopyTo(ms);
        return ms.ToArray();
    }

    private void DisposePlayerUnlocked()
    {
        _player?.Dispose();
        _player = null;
        _memoryStream?.Dispose();
        _memoryStream = null;
        _loadedKey = string.Empty;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            DisposePlayerUnlocked();
        }
    }
}

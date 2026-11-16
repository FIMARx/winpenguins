using System.Runtime.InteropServices;

namespace WinPenguins.Platform;

public static class GlobalHotkeyParser
{
    public static readonly string[] Presets =
    [
        "Ctrl+Alt+P",
        "Ctrl+Shift+P",
        "Ctrl+Alt+H",
        "Ctrl+Shift+H",
        "Win+Alt+P",
        "Alt+F9"
    ];

    public static bool TryParse(
        string? hotkeySpec,
        out uint modifiers,
        out uint virtualKey,
        out string normalized)
    {
        modifiers = 0;
        virtualKey = 0;
        normalized = string.Empty;

        if (string.IsNullOrWhiteSpace(hotkeySpec))
        {
            return false;
        }

        string[] parts = hotkeySpec.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            return false;
        }

        bool ctrl = false;
        bool alt = false;
        bool shift = false;
        bool win = false;
        string? keyName = null;

        foreach (string raw in parts)
        {
            switch (raw.ToUpperInvariant())
            {
                case "CTRL":
                case "CONTROL":
                    ctrl = true;
                    modifiers |= NativeMethods.MOD_CONTROL;
                    break;
                case "ALT":
                case "MENU":
                    alt = true;
                    modifiers |= NativeMethods.MOD_ALT;
                    break;
                case "SHIFT":
                    shift = true;
                    modifiers |= NativeMethods.MOD_SHIFT;
                    break;
                case "WIN":
                case "WINDOWS":
                    win = true;
                    modifiers |= NativeMethods.MOD_WIN;
                    break;
                default:
                    if (!TryParseKeyToken(raw, out uint parsedVk, out string canonicalKey))
                    {
                        return false;
                    }

                    if (virtualKey != 0)
                    {
                        return false;
                    }

                    virtualKey = parsedVk;
                    keyName = canonicalKey;
                    break;
            }
        }

        if (modifiers == 0 || virtualKey == 0 || keyName is null)
        {
            return false;
        }

        var ordered = new List<string>(4);
        if (ctrl) ordered.Add("Ctrl");
        if (alt) ordered.Add("Alt");
        if (shift) ordered.Add("Shift");
        if (win) ordered.Add("Win");
        ordered.Add(keyName);
        normalized = string.Join('+', ordered);
        return true;
    }

    private static bool TryParseKeyToken(string token, out uint virtualKey, out string canonicalName)
    {
        virtualKey = 0;
        canonicalName = string.Empty;

        if (token.Length == 1)
        {
            char c = char.ToUpperInvariant(token[0]);
            if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))
            {
                virtualKey = c;
                canonicalName = c.ToString();
                return true;
            }
        }

        if (token.Length >= 2 && (token[0] == 'F' || token[0] == 'f') &&
            int.TryParse(token.AsSpan(1), out int fn) && fn >= 1 && fn <= 24)
        {
            virtualKey = (uint)(0x70 + fn - 1);
            canonicalName = $"F{fn}";
            return true;
        }

        if (string.Equals(token, "SPACE", StringComparison.OrdinalIgnoreCase))
        {
            virtualKey = (uint)Keys.Space;
            canonicalName = "Space";
            return true;
        }

        if (string.Equals(token, "PAUSE", StringComparison.OrdinalIgnoreCase))
        {
            virtualKey = (uint)Keys.Pause;
            canonicalName = "Pause";
            return true;
        }

        if (Enum.TryParse<Keys>(token, ignoreCase: true, out Keys parsed) &&
            parsed != Keys.None &&
            parsed < Keys.Modifiers)
        {
            virtualKey = (uint)parsed;
            canonicalName = parsed.ToString();
            return true;
        }

        return false;
    }
}

public sealed class GlobalHotkeyWindow : NativeWindow, IDisposable
{
    private const int HotkeyId = 0x5750; // 'WP'
    private bool _registered;

    public event Action? HotkeyPressed;

    public GlobalHotkeyWindow()
    {
        CreateHandle(new CreateParams
        {
            Caption = "WinPenguinsHotkey",
            Parent = NativeMethods.HWND_MESSAGE
        });
    }

    public bool UpdateBinding(bool enabled, string? hotkeySpec, out string? errorMessage)
    {
        UnregisterCurrent();
        errorMessage = null;

        if (!enabled || Handle == IntPtr.Zero)
        {
            return true;
        }

        if (!GlobalHotkeyParser.TryParse(hotkeySpec, out uint modifiers, out uint vk, out _))
        {
            errorMessage = $"Unsupported hotkey format \"{hotkeySpec}\". Use combinations like Ctrl+Alt+P.";
            return false;
        }

        if (!NativeMethods.RegisterHotKey(Handle, HotkeyId, modifiers | NativeMethods.MOD_NOREPEAT, vk))
        {
            int err = Marshal.GetLastWin32Error();
            errorMessage = $"Could not register hotkey {hotkeySpec} (Win32 error {err}). It may be in use by another app.";
            return false;
        }

        _registered = true;
        return true;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_HOTKEY && (int)m.WParam == HotkeyId)
        {
            HotkeyPressed?.Invoke();
            m.Result = IntPtr.Zero;
            return;
        }

        base.WndProc(ref m);
    }

    private void UnregisterCurrent()
    {
        if (_registered && Handle != IntPtr.Zero)
        {
            NativeMethods.UnregisterHotKey(Handle, HotkeyId);
            _registered = false;
        }
    }

    public void Dispose()
    {
        UnregisterCurrent();
        if (Handle != IntPtr.Zero)
        {
            DestroyHandle();
        }
    }
}

using System.Windows.Interop;
using DisplayDimmer.Interop;

namespace DisplayDimmer.Services;

/// <summary>Atalhos globais registrados com RegisterHotKey.</summary>
public sealed class HotkeyService : IDisposable
{
    private readonly Dictionary<int, Action> _actions = new();

    /// <summary>Combinacoes que nao puderam ser registradas (invalidas ou ja usadas por outro programa).</summary>
    public List<string> Failures { get; } = new();
    private HwndSource? _source;
    private int _nextId = 0xA000;

    public void Start()
    {
        if (_source is not null)
        {
            return;
        }

        var parameters = new HwndSourceParameters("DisplayDimmerHotkeyWindow")
        {
            WindowStyle = 0,
            ExtendedWindowStyle = NativeMethods.WS_EX_TOOLWINDOW,
            Width = 0,
            Height = 0,
        };

        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    /// <summary>Registra um atalho. Retorna falso quando a combinacao e invalida ou ja esta em uso.</summary>
    public bool Register(string? gesture, Action action)
    {
        if (string.IsNullOrWhiteSpace(gesture) || _source is null)
        {
            return false;
        }

        if (Parse(gesture) is null)
        {
            Failures.Add(gesture);
            return false;
        }

        var parsed = Parse(gesture);
        if (parsed is null)
        {
            return false;
        }

        var id = _nextId++;
        if (!NativeMethods.RegisterHotKey(_source.Handle, id, parsed.Value.Modifiers | NativeMethods.MOD_NOREPEAT, parsed.Value.Key))
        {
            Failures.Add(gesture);
            return false;
        }

        _actions[id] = action;
        return true;
    }

    public void UnregisterAll()
    {
        if (_source is null)
        {
            return;
        }

        foreach (var id in _actions.Keys)
        {
            NativeMethods.UnregisterHotKey(_source.Handle, id);
        }

        _actions.Clear();
        Failures.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && _actions.TryGetValue((int)wParam, out var action))
        {
            action();
            handled = true;
        }

        return IntPtr.Zero;
    }

    public static (uint Modifiers, uint Key)? Parse(string gesture)
    {
        var parts = gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return null;
        }

        uint modifiers = 0;
        uint key = 0;

        foreach (var part in parts)
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= NativeMethods.MOD_CONTROL;
                    break;
                case "alt":
                    modifiers |= NativeMethods.MOD_ALT;
                    break;
                case "shift":
                    modifiers |= NativeMethods.MOD_SHIFT;
                    break;
                case "win":
                case "windows":
                    modifiers |= NativeMethods.MOD_WIN;
                    break;
                default:
                    key = ToVirtualKey(part);
                    break;
            }
        }

        return key == 0 ? null : (modifiers, key);
    }

    private static uint ToVirtualKey(string token)
    {
        if (token.Length == 1)
        {
            var c = char.ToUpperInvariant(token[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                return c;
            }
        }

        if (token.StartsWith('F') && int.TryParse(token[1..], out var fn) && fn is >= 1 and <= 24)
        {
            return (uint)(0x70 + fn - 1);
        }

        return token.ToLowerInvariant() switch
        {
            "up" => 0x26,
            "down" => 0x28,
            "left" => 0x25,
            "right" => 0x27,
            "home" => 0x24,
            "end" => 0x23,
            "pgup" or "pageup" => 0x21,
            "pgdn" or "pagedown" => 0x22,
            "insert" => 0x2D,
            "delete" => 0x2E,
            "space" => 0x20,
            "tab" => 0x09,
            "esc" or "escape" => 0x1B,
            "minus" or "-" => 0xBD,
            "plus" or "=" => 0xBB,
            "pause" => 0x13,
            _ => 0,
        };
    }

    public void Dispose()
    {
        UnregisterAll();

        if (_source is not null)
        {
            _source.RemoveHook(WndProc);
            _source.Dispose();
            _source = null;
        }
    }
}

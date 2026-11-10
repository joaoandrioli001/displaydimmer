using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using DisplayDimmer.Interop;

namespace DisplayDimmer.Services;

/// <summary>Icone da bandeja com menu, dica e ajuste pela roda do mouse em passos de 5%.</summary>
public sealed class TrayService : IDisposable
{
    private const uint IconId = 1;
    private const int CallbackMessage = 0x8000 + 0x21;

    private readonly NativeMethods.HookProc _mouseHookProc;
    private HwndSource? _source;
    private IntPtr _mouseHook;
    private IntPtr _icon;
    private NativeMethods.NOTIFYICONDATA _data;
    private bool _disposed;
    private DateTime _lastWheel = DateTime.MinValue;

    public TrayService()
    {
        _mouseHookProc = MouseHookCallback;
    }

    private static void Log(string message)
    {
        if (Environment.GetEnvironmentVariable("DISPLAYDIMMER_LOG") is null)
        {
            return;
        }

        try
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "displaydimmer-ipc.log"),
                $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    public event Action<int>? Wheel;

    public event Action? ShowRequested;

    /// <summary>Fornece o menu montado pela aplicacao.</summary>
    public Func<ContextMenu>? MenuFactory { get; set; }

    public void Start()
    {
        var parameters = new HwndSourceParameters("DisplayDimmerTrayWindow")
        {
            WindowStyle = 0,
            ExtendedWindowStyle = NativeMethods.WS_EX_TOOLWINDOW,
            Width = 0,
            Height = 0,
            PositionX = 0,
            PositionY = 0,
        };

        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);

        _icon = IconFactory.Create(24);

        _data = new NativeMethods.NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>(),
            hWnd = _source.Handle,
            uID = IconId,
            uFlags = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP | NativeMethods.NIF_SHOWTIP,
            uCallbackMessage = CallbackMessage,
            hIcon = _icon,
            szTip = "Display Dimmer",
            szInfo = string.Empty,
            szInfoTitle = string.Empty,
        };

        var added = NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, ref _data);
        Log($"bandeja: icone adicionado={added} hIcon={_icon}");

        _data.uVersion = 4;
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_SETVERSION, ref _data);

        _mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _mouseHookProc, IntPtr.Zero, 0);
    }

    public void SetTooltip(string text)
    {
        if (_disposed || _source is null)
        {
            return;
        }

        _data.szTip = text.Length > 120 ? text[..120] : text;
        _data.uFlags = NativeMethods.NIF_TIP | NativeMethods.NIF_SHOWTIP;
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, ref _data);
    }

    public void ShowBalloon(string title, string message)
    {
        if (_disposed || _source is null)
        {
            return;
        }

        _data.szInfoTitle = title;
        _data.szInfo = message;
        _data.dwInfoFlags = NativeMethods.NIIF_INFO | NativeMethods.NIIF_NOSOUND;
        _data.uFlags = NativeMethods.NIF_INFO;
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, ref _data);
    }

    public void ShowMenu()
    {
        var factory = MenuFactory;
        if (factory is null)
        {
            return;
        }

        var menu = factory();
        menu.Placement = PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != CallbackMessage)
        {
            return IntPtr.Zero;
        }

        var mouseMessage = (int)((long)lParam & 0xFFFF);

        switch (mouseMessage)
        {
            case NativeMethods.WM_LBUTTONUP:
            case NativeMethods.WM_LBUTTONDBLCLK:
                ShowRequested?.Invoke();
                handled = true;
                break;

            case NativeMethods.WM_RBUTTONUP:
            case NativeMethods.WM_CONTEXTMENU:
                ShowMenu();
                handled = true;
                break;
        }

        return IntPtr.Zero;
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (int)wParam == NativeMethods.WM_MOUSEWHEEL)
        {
            var info = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
            if (IsOverTrayIcon())
            {
                var delta = (short)((info.mouseData >> 16) & 0xFFFF);
                if (delta != 0)
                {
                    // Evita disparos duplicados do mesmo entalhe.
                    if ((DateTime.UtcNow - _lastWheel).TotalMilliseconds > 25)
                    {
                        _lastWheel = DateTime.UtcNow;
                        Wheel?.Invoke(delta);
                    }

                    return (IntPtr)1;
                }
            }
        }

        return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    /// <summary>Indica se o cursor esta dentro do retangulo real do icone deste aplicativo na bandeja.</summary>
    private bool IsOverTrayIcon()
    {
        try
        {
            var hwnd = _source?.Handle ?? IntPtr.Zero;
            if (hwnd == IntPtr.Zero)
            {
                Log("bandeja: janela da bandeja indisponivel, roda ignorada");
                return false;
            }

            if (!NativeMethods.GetCursorPos(out var cursor))
            {
                Log("bandeja: GetCursorPos falhou, roda ignorada");
                return false;
            }

            var identifier = new NativeMethods.NOTIFYICONIDENTIFIER
            {
                cbSize = Marshal.SizeOf<NativeMethods.NOTIFYICONIDENTIFIER>(),
                hwnd = hwnd,
                uID = IconId,
                guidItem = Guid.Empty,
            };

            var result = NativeMethods.Shell_NotifyIconGetRect(ref identifier, out var rect);
            if (result != NativeMethods.S_OK)
            {
                // Falha segura: sem retangulo confiavel nao ha ajuste de brilho.
                Log($"bandeja: Shell_NotifyIconGetRect falhou hr=0x{result:X8}, roda ignorada");
                return false;
            }

            if (rect.Right <= rect.Left || rect.Bottom <= rect.Top)
            {
                // Icone oculto no flyout de overflow ou retangulo ainda nao calculado.
                Log($"bandeja: retangulo invalido ({rect.Left},{rect.Top},{rect.Right},{rect.Bottom}), roda ignorada");
                return false;
            }

            return cursor.X >= rect.Left
                && cursor.X < rect.Right
                && cursor.Y >= rect.Top
                && cursor.Y < rect.Bottom;
        }
        catch (Exception ex)
        {
            Log($"bandeja: falha ao consultar o retangulo do icone: {ex.Message}");
        }

        return false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_mouseHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }

        if (_source is not null)
        {
            _data.uFlags = 0;
            NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, ref _data);
            _source.RemoveHook(WndProc);
            _source.Dispose();
            _source = null;
        }

        if (_icon != IntPtr.Zero)
        {
            NativeMethods.DestroyIcon(_icon);
            _icon = IntPtr.Zero;
        }
    }
}

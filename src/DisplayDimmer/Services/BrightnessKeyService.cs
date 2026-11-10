using System.Runtime.InteropServices;
using System.Windows.Interop;
using DisplayDimmer.Interop;

namespace DisplayDimmer.Services;

/// <summary>
/// Escuta as teclas nativas de brilho do teclado (HID Consumer 0x006F e 0x0070).
/// Depende do driver do fabricante: quando o teclado nao expoe essas teclas como HID,
/// use os atalhos globais configurados em Configuracoes.
/// </summary>
public sealed class BrightnessKeyService : IDisposable
{
    private HwndSource? _source;
    private bool _registered;

    public event Action? BrightnessUp;

    public event Action? BrightnessDown;

    public bool Start()
    {
        if (_source is not null)
        {
            return _registered;
        }

        var parameters = new HwndSourceParameters("DisplayDimmerRawInputWindow")
        {
            WindowStyle = 0,
            ExtendedWindowStyle = NativeMethods.WS_EX_TOOLWINDOW,
            Width = 0,
            Height = 0,
        };

        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);

        var devices = new[]
        {
            new NativeMethods.RAWINPUTDEVICE
            {
                usUsagePage = NativeMethods.HID_USAGE_PAGE_CONSUMER,
                usUsage = NativeMethods.HID_USAGE_CONSUMER_CONTROL,
                dwFlags = NativeMethods.RIDEV_INPUTSINK,
                hwndTarget = _source.Handle,
            },
        };

        _registered = NativeMethods.RegisterRawInputDevices(
            devices,
            (uint)devices.Length,
            (uint)Marshal.SizeOf<NativeMethods.RAWINPUTDEVICE>());

        return _registered;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != NativeMethods.WM_INPUT)
        {
            return IntPtr.Zero;
        }

        try
        {
            var headerSize = (uint)Marshal.SizeOf<NativeMethods.RAWINPUTDEVICE>();
            uint size = 0;
            NativeMethods.GetRawInputData(lParam, NativeMethods.RID_INPUT, null, ref size, headerSize);

            if (size == 0 || size > 4096)
            {
                return IntPtr.Zero;
            }

            var buffer = new byte[size];
            var read = NativeMethods.GetRawInputData(lParam, NativeMethods.RID_INPUT, buffer, ref size, headerSize);
            if (read == uint.MaxValue || buffer.Length < 32)
            {
                return IntPtr.Zero;
            }

            var type = BitConverter.ToUInt32(buffer, 0);
            if (type != NativeMethods.RIM_TYPEHID)
            {
                return IntPtr.Zero;
            }

            // RAWINPUTHEADER tem 24 bytes em x64; depois vem RAWHID (dwSizeHid, dwCount, dados).
            var sizeHid = BitConverter.ToUInt32(buffer, 24);
            var count = BitConverter.ToUInt32(buffer, 28);
            var dataLength = (int)Math.Min(buffer.Length - 32, sizeHid * Math.Max(1u, count));

            for (var i = 0; i + 1 < dataLength; i++)
            {
                var usage = buffer[32 + i] | (buffer[33 + i] << 8);
                if (usage == 0x006F)
                {
                    BrightnessUp?.Invoke();
                    break;
                }

                if (usage == 0x0070)
                {
                    BrightnessDown?.Invoke();
                    break;
                }
            }
        }
        catch
        {
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_source is not null)
        {
            _source.RemoveHook(WndProc);
            _source.Dispose();
            _source = null;
        }

        _registered = false;
    }
}

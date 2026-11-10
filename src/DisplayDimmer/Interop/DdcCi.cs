using DisplayDimmer.Interop;

namespace DisplayDimmer.Interop;

/// <summary>Controle DDC/CI de brilho e contraste em monitores externos.</summary>
internal static class DdcCi
{
    private const byte VcpBrightness = 0x10;
    private const byte VcpContrast = 0x12;

    public static bool TryReadBrightness(IntPtr handle, out int percent)
    {
        percent = 100;
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            if (NativeMethods.GetMonitorBrightness(handle, out var min, out var current, out var max))
            {
                percent = ScaleToPercent(current, min, max);
                return true;
            }

            if (NativeMethods.GetVCPFeatureAndVCPFeatureReply(handle, VcpBrightness, out _, out var value, out var maxValue) && maxValue > 0)
            {
                percent = (int)Math.Round(value * 100.0 / maxValue);
                return true;
            }
        }
        catch
        {
            // Driver recusou a leitura; o chamador usa o valor em cache.
        }

        return false;
    }

    public static bool TrySetBrightness(IntPtr handle, int percent, int min, int max)
    {
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            if (NativeMethods.SetMonitorBrightness(handle, (uint)ScaleFromPercent(percent, min, max)))
            {
                return true;
            }

            return NativeMethods.SetVCPFeature(handle, VcpBrightness, (uint)ScaleFromPercent(percent, 0, 100));
        }
        catch
        {
            return false;
        }
    }

    public static bool TryReadContrast(IntPtr handle, out int percent)
    {
        percent = 50;
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            if (NativeMethods.GetMonitorContrast(handle, out var min, out var current, out var max))
            {
                percent = ScaleToPercent(current, min, max);
                return true;
            }

            if (NativeMethods.GetVCPFeatureAndVCPFeatureReply(handle, VcpContrast, out _, out var value, out var maxValue) && maxValue > 0)
            {
                percent = (int)Math.Round(value * 100.0 / maxValue);
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    public static bool TrySetContrast(IntPtr handle, int percent, int min, int max)
    {
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            if (NativeMethods.SetMonitorContrast(handle, (uint)ScaleFromPercent(percent, min, max)))
            {
                return true;
            }

            return NativeMethods.SetVCPFeature(handle, VcpContrast, (uint)ScaleFromPercent(percent, 0, 100));
        }
        catch
        {
            return false;
        }
    }

    private static int ScaleToPercent(uint value, uint min, uint max)
    {
        if (max <= min)
        {
            return 100;
        }

        var percent = (value - min) * 100.0 / (max - min);
        return (int)Math.Clamp(Math.Round(percent), 0, 100);
    }

    private static int ScaleFromPercent(int percent, int min, int max)
    {
        if (max <= min)
        {
            return Math.Clamp(percent, 0, 100);
        }

        return (int)Math.Clamp(Math.Round(min + (max - min) * Math.Clamp(percent, 0, 100) / 100.0), min, max);
    }
}

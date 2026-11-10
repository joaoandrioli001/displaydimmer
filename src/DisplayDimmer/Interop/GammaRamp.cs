namespace DisplayDimmer.Interop;

/// <summary>Aplica rampas de gamma por dispositivo de exibicao.</summary>
internal static class GammaRamp
{
    public const int RampSize = 256;

    public static bool Apply(string deviceName, ushort[] ramp)
    {
        if (string.IsNullOrWhiteSpace(deviceName) || ramp.Length != RampSize * 3)
        {
            return false;
        }

        var hdc = NativeMethods.CreateDC("DISPLAY", deviceName, null, IntPtr.Zero);
        if (hdc == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            return NativeMethods.SetDeviceGammaRamp(hdc, ramp);
        }
        catch
        {
            return false;
        }
        finally
        {
            NativeMethods.DeleteDC(hdc);
        }
    }

    public static ushort[] CreateNeutralRamp()
    {
        var ramp = new ushort[RampSize * 3];
        for (var i = 0; i < RampSize; i++)
        {
            var value = (ushort)(i * 257);
            ramp[i] = value;
            ramp[RampSize + i] = value;
            ramp[RampSize * 2 + i] = value;
        }

        return ramp;
    }

    public static bool Reset(string deviceName) => Apply(deviceName, CreateNeutralRamp());

    /// <summary>Le a rampa atual do dispositivo (diagnostico).</summary>
    public static ushort[]? Read(string deviceName)
    {
        var hdc = NativeMethods.CreateDC("DISPLAY", deviceName, null, IntPtr.Zero);
        if (hdc == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var ramp = new ushort[RampSize * 3];
            return NativeMethods.GetDeviceGammaRamp(hdc, ramp) ? ramp : null;
        }
        catch
        {
            return null;
        }
        finally
        {
            NativeMethods.DeleteDC(hdc);
        }
    }
}

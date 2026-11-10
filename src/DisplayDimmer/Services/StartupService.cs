using Microsoft.Win32;

namespace DisplayDimmer.Services;

/// <summary>Entrada em HKCU\...\Run para iniciar com o Windows.</summary>
public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DisplayDimmer";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string value && value.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key is null)
            {
                return false;
            }

            if (enabled)
            {
                var path = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(path))
                {
                    return false;
                }

                key.SetValue(ValueName, $"\"{path}\" --minimized");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}

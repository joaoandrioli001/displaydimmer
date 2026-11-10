using System.Windows;
using Microsoft.Win32;

namespace DisplayDimmer.Services;

/// <summary>Aplica o tema claro, escuro ou o tema do sistema.</summary>
public sealed class ThemeService
{
    private string _applied = string.Empty;

    public string CurrentTheme { get; private set; } = "dark";

    public event EventHandler? ThemeChanged;

    public void Apply(string theme)
    {
        CurrentTheme = Normalize(theme);
        var effective = CurrentTheme == "system" ? (IsSystemLight() ? "light" : "dark") : CurrentTheme;

        if (effective == _applied)
        {
            return;
        }

        var source = new Uri($"/DisplayDimmer;component/Themes/{(effective == "light" ? "Light" : "Dark")}.xaml", UriKind.Relative);
        var dictionary = new ResourceDictionary { Source = source };

        var merged = Application.Current.Resources.MergedDictionaries;
        if (merged.Count == 0)
        {
            merged.Add(dictionary);
        }
        else
        {
            merged[0] = dictionary;
        }

        _applied = effective;
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Reavalia o tema do sistema (chamado quando o Windows muda de tema).</summary>
    public void RefreshSystemTheme()
    {
        if (CurrentTheme == "system")
        {
            var effective = IsSystemLight() ? "light" : "dark";
            if (effective != _applied)
            {
                Apply("system");
            }
        }
    }

    public static string Normalize(string? theme)
        => theme?.Trim().ToLowerInvariant() switch
        {
            "light" or "claro" => "light",
            "system" or "sistema" or "auto" => "system",
            _ => "dark",
        };

    public static bool IsSystemLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");

            return key?.GetValue("AppsUseLightTheme") is int value && value == 1;
        }
        catch
        {
            return false;
        }
    }
}

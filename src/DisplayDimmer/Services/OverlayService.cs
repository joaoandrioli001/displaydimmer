using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using DisplayDimmer.Interop;
using DisplayDimmer.Models;

namespace DisplayDimmer.Services;

/// <summary>
/// Escurecimento por software usando uma janela preta clicavel-transparente por monitor.
/// Serve para telas de notebook e monitores sem DDC/CI, inclusive abaixo do limite do gamma.
/// </summary>
public sealed class OverlayService : IDisposable
{
    private const double MaxOpacity = 0.82;

    private readonly Dictionary<string, Window> _windows = new();

    /// <summary>Desligado quando o aplicativo roda sem interface (linha de comando).</summary>
    public bool Enabled { get; set; }

    public void SetDim(DisplayInfo display, int brightnessPercent)
    {
        if (!Enabled || brightnessPercent >= 100)
        {
            Hide(display.Id);
            return;
        }

        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        if (!app.Dispatcher.CheckAccess())
        {
            app.Dispatcher.BeginInvoke(new Action(() => SetDim(display, brightnessPercent)));
            return;
        }

        var window = GetOrCreate(display);
        var alpha = Math.Clamp((1 - brightnessPercent / 100.0) * MaxOpacity, 0, MaxOpacity);
        window.Opacity = alpha;
        Place(window, display);

        if (!window.IsVisible)
        {
            window.Show();
        }
    }

    public void Hide(string displayId)
    {
        var app = Application.Current;
        if (app is not null && !app.Dispatcher.CheckAccess())
        {
            app.Dispatcher.BeginInvoke(new Action(() => Hide(displayId)));
            return;
        }

        if (_windows.TryGetValue(displayId, out var window) && window.IsVisible)
        {
            window.Hide();
        }
    }

    public void HideAll()
    {
        foreach (var id in _windows.Keys.ToList())
        {
            Hide(id);
        }
    }

    public void Dispose()
    {
        foreach (var window in _windows.Values)
        {
            try
            {
                window.Close();
            }
            catch
            {
            }
        }

        _windows.Clear();
    }

    private Window GetOrCreate(DisplayInfo display)
    {
        if (_windows.TryGetValue(display.Id, out var existing))
        {
            return existing;
        }

        var window = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Black,
            Topmost = true,
            ShowInTaskbar = false,
            ShowActivated = false,
            ResizeMode = ResizeMode.NoResize,
            Focusable = false,
            IsHitTestVisible = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = display.X,
            Top = display.Y,
            Width = Math.Max(1, display.Width),
            Height = Math.Max(1, display.Height),
            Opacity = 0,
        };

        window.SourceInitialized += (_, _) => MakeClickThrough(window);

        _windows[display.Id] = window;
        return window;
    }

    private static void MakeClickThrough(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var style = NativeMethods.GetWindowLong(handle, NativeMethods.GWL_EXSTYLE);
        style |= NativeMethods.WS_EX_TRANSPARENT
                 | NativeMethods.WS_EX_NOACTIVATE
                 | NativeMethods.WS_EX_TOOLWINDOW
                 | NativeMethods.WS_EX_LAYERED;
        NativeMethods.SetWindowLongExStyle(handle, style);
    }

    private static void Place(Window window, DisplayInfo display)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.SetWindowPos(
            handle,
            NativeMethods.HWND_TOPMOST,
            display.X,
            display.Y,
            Math.Max(1, display.Width),
            Math.Max(1, display.Height),
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER);
    }
}

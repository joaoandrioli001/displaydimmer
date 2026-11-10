using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DisplayDimmer.Views;

/// <summary>Gera um PNG da janela. Usado apenas em diagnostico (--shot).</summary>
internal static class WindowSnapshot
{
    public static bool Save(Window window, string path)
    {
        try
        {
            window.UpdateLayout();

            if (window.Content is not FrameworkElement content || content.ActualWidth < 1)
            {
                return false;
            }

            var dpi = VisualTreeHelper.GetDpi(window);
            var width = (int)Math.Ceiling(content.ActualWidth * dpi.DpiScaleX);
            var height = (int)Math.Ceiling(content.ActualHeight * dpi.DpiScaleY);

            var bitmap = new RenderTargetBitmap(width, height, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            bitmap.Render(content);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));

            var folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(folder))
            {
                Directory.CreateDirectory(folder);
            }

            using var stream = File.Create(path);
            encoder.Save(stream);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

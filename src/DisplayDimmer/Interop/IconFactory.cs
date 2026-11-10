using System.IO;
using System.Windows;

namespace DisplayDimmer.Interop;

/// <summary>Cria o HICON da bandeja a partir do app.ico embutido como recurso.</summary>
internal static class IconFactory
{
    private static byte[]? _bytes;

    public static IntPtr Create(int size = 24)
    {
        var bytes = LoadBytes();
        if (bytes is null || bytes.Length < 6)
        {
            return IntPtr.Zero;
        }

        try
        {
            var count = BitConverter.ToUInt16(bytes, 4);
            var bestOffset = -1;
            var bestLength = 0;
            var bestSize = 0;
            var bestDelta = int.MaxValue;

            for (var i = 0; i < count; i++)
            {
                var entry = 6 + i * 16;
                if (entry + 16 > bytes.Length)
                {
                    break;
                }

                int width = bytes[entry] == 0 ? 256 : bytes[entry];
                var length = BitConverter.ToInt32(bytes, entry + 8);
                var offset = BitConverter.ToInt32(bytes, entry + 12);
                var delta = Math.Abs(width - size);

                if (delta < bestDelta && offset > 0 && length > 0 && offset + length <= bytes.Length)
                {
                    bestDelta = delta;
                    bestOffset = offset;
                    bestLength = length;
                    bestSize = width;
                }
            }

            if (bestOffset < 0)
            {
                return IntPtr.Zero;
            }

            var data = new byte[bestLength];
            Array.Copy(bytes, bestOffset, data, 0, bestLength);

            return NativeMethods.CreateIconFromResourceEx(
                data,
                (uint)data.Length,
                true,
                NativeMethods.ICON_RESOURCE_VERSION,
                bestSize,
                bestSize,
                NativeMethods.LR_DEFAULTCOLOR);
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    private static byte[]? LoadBytes()
    {
        if (_bytes is not null)
        {
            return _bytes;
        }

        try
        {
            var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
            if (resource is null)
            {
                return null;
            }

            using var stream = resource.Stream;
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            _bytes = memory.ToArray();
            return _bytes;
        }
        catch
        {
            return null;
        }
    }
}

using System.IO;
using System.Text;
using DisplayDimmer.Interop;
using Microsoft.Win32.SafeHandles;

namespace DisplayDimmer.Services;

/// <summary>Permite que o executavel WinExe escreva no PowerShell, no Prompt de Comando ou em um redirecionamento.</summary>
internal static class ConsoleBridge
{
    private static bool _attached;

    public static void Attach()
    {
        if (_attached)
        {
            return;
        }

        try
        {
            var encoding = new UTF8Encoding(false);
            var std = NativeMethods.GetStdHandle(NativeMethods.STD_OUTPUT_HANDLE);
            var invalid = std == IntPtr.Zero || std == new IntPtr(-1);

            if (invalid)
            {
                if (NativeMethods.GetConsoleWindow() == IntPtr.Zero)
                {
                    NativeMethods.AttachConsole(NativeMethods.ATTACH_PARENT_PROCESS);
                }

                var handle = NativeMethods.CreateFile(
                    "CONOUT$",
                    NativeMethods.GENERIC_WRITE,
                    NativeMethods.FILE_SHARE_WRITE,
                    IntPtr.Zero,
                    NativeMethods.OPEN_EXISTING,
                    0,
                    IntPtr.Zero);

                if (handle == IntPtr.Zero || handle == new IntPtr(-1))
                {
                    return;
                }

                var stream = new FileStream(new SafeFileHandle(handle, ownsHandle: true), FileAccess.Write);
                Console.SetOut(new StreamWriter(stream, encoding) { AutoFlush = true });
            }
            else
            {
                Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), encoding) { AutoFlush = true });
            }

            _attached = true;
        }
        catch
        {
        }
    }

    public static void Write(string text)
    {
        if (!_attached)
        {
            return;
        }

        Console.Out.Write(text);
    }

    public static void WriteLine(string text)
    {
        if (!_attached)
        {
            return;
        }

        Console.Out.WriteLine(text);
    }
}

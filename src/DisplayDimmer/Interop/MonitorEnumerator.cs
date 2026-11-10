using System.Runtime.InteropServices;
using DisplayDimmer.Models;

namespace DisplayDimmer.Interop;

/// <summary>Enumera monitores fisicos, nomes amigaveis e suporte a DDC/CI.</summary>
internal static class MonitorEnumerator
{
    internal static void Diag(string message)
    {
        if (Environment.GetEnvironmentVariable("DISPLAYDIMMER_LOG") is null)
        {
            return;
        }

        try
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "displaydimmer-ipc.log"),
                $"{DateTime.Now:HH:mm:ss.fff} enum: {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    private const int MDT_EFFECTIVE_DPI = 0;

    public static List<DisplayInfo> Enumerate()
    {
        var names = QueryFriendlyNames();
        var displays = new List<DisplayInfo>();

        try
        {
            NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdc, ref NativeMethods.RECT rect, IntPtr data) =>
            {
                try
                {
                    displays.Add(CreateDisplay(hMonitor, names));
                }
                catch
                {
                    // Um monitor problematico nao deve impedir a enumeracao dos demais.
                }

                return true;
            }, IntPtr.Zero);
        }
        catch
        {
        }

        return displays;
    }

    public static void ReleaseHandles(IEnumerable<DisplayInfo> displays)
    {
        foreach (var display in displays)
        {
            if (display.PhysicalHandle == IntPtr.Zero)
            {
                continue;
            }

            try
            {
                var array = new[]
                {
                    new NativeMethods.PHYSICAL_MONITOR
                    {
                        hPhysicalMonitor = display.PhysicalHandle,
                        szPhysicalMonitorDescription = display.Description,
                    },
                };
                NativeMethods.DestroyPhysicalMonitors(1, array);
            }
            catch
            {
            }

            display.PhysicalHandle = IntPtr.Zero;
        }
    }

    private static DisplayInfo CreateDisplay(IntPtr hMonitor, Dictionary<string, (string Friendly, uint Technology)> names)
    {
        var info = new NativeMethods.MONITORINFOEX
        {
            cbSize = Marshal.SizeOf<NativeMethods.MONITORINFOEX>(),
            szDevice = string.Empty,
        };

        NativeMethods.GetMonitorInfo(hMonitor, ref info);

        var gdiName = info.szDevice;
        names.TryGetValue(gdiName, out var meta);

        var deviceId = gdiName;
        var adapter = string.Empty;
        try
        {
            var device = new NativeMethods.DISPLAY_DEVICE { cb = Marshal.SizeOf<NativeMethods.DISPLAY_DEVICE>() };
            if (NativeMethods.EnumDisplayDevices(gdiName, 0, ref device, 0) && !string.IsNullOrWhiteSpace(device.DeviceID))
            {
                deviceId = device.DeviceID;
                adapter = device.DeviceString;
            }
        }
        catch
        {
        }

        var scale = 1.0;
        try
        {
            if (NativeMethods.GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 && dpiX > 0)
            {
                scale = dpiX / 96.0;
            }
        }
        catch
        {
        }

        var display = new DisplayInfo
        {
            Id = deviceId,
            DeviceName = gdiName,
            Name = string.IsNullOrWhiteSpace(meta.Friendly) ? FallbackName(gdiName, adapter) : meta.Friendly,
            AdapterName = adapter,
            OutputTechnology = meta.Technology,
            IsInternal = meta.Technology == NativeMethods.OUTPUT_TECHNOLOGY_INTERNAL,
            IsPrimary = (info.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0,
            X = info.rcMonitor.Left,
            Y = info.rcMonitor.Top,
            Width = info.rcMonitor.Width,
            Height = info.rcMonitor.Height,
            Scale = scale,
            MonitorHandle = hMonitor,
        };

        AttachPhysicalMonitor(display);
        return display;
    }

    private static string FallbackName(string gdiName, string adapter)
    {
        var suffix = gdiName.Replace(@"\\.\", string.Empty);
        if (!string.IsNullOrWhiteSpace(adapter))
        {
            return $"{adapter} ({suffix})";
        }

        return $"Monitor {suffix}";
    }

    private static void AttachPhysicalMonitor(DisplayInfo display)
    {
        try
        {
            if (!NativeMethods.GetNumberOfPhysicalMonitorsFromHMONITOR(display.MonitorHandle, out var count) || count == 0)
            {
                Diag($"{display.Name}: sem monitores fisicos (count={count})");
                return;
            }

            // A leitura e feita em memoria nao gerenciada: o marshaling de arrays com
            // ByValTStr devolvia handles invalidos (0x0) em alguns drivers.
            var size = Marshal.SizeOf<NativeMethods.PHYSICAL_MONITOR>();
            var buffer = Marshal.AllocHGlobal((int)(size * count));

            try
            {
                if (!NativeMethods.GetPhysicalMonitorsFromHMONITORPtr(display.MonitorHandle, count, buffer))
                {
                    Diag($"{display.Name}: GetPhysicalMonitors falhou, erro {Marshal.GetLastWin32Error()}");
                    return;
                }

                var handle = Marshal.ReadIntPtr(buffer, 0);
                var description = Marshal.PtrToStringUni(buffer + IntPtr.Size) ?? string.Empty;

                Diag($"{display.Name}: count={count} size={size} handle=0x{handle.ToInt64():X} desc='{description}'");

                display.PhysicalHandle = handle;
                display.Description = description;

                // Libera os handles extras; o controle usa apenas o primeiro.
                for (var index = 1; index < count; index++)
                {
                    var extra = Marshal.ReadIntPtr(buffer, (int)(index * size));
                    if (extra != IntPtr.Zero)
                    {
                        NativeMethods.DestroyPhysicalMonitor(extra);
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }

            if (display.PhysicalHandle == IntPtr.Zero)
            {
                display.SupportsDdc = false;
                return;
            }

            display.SupportsDdc = true;

            if (NativeMethods.GetMonitorBrightness(display.PhysicalHandle, out var min, out _, out var max) && max > min)
            {
                display.SupportsBrightness = true;
                display.DdcBrightnessMin = (int)min;
                display.DdcBrightnessMax = (int)max;
            }

            if (NativeMethods.GetMonitorContrast(display.PhysicalHandle, out var cmin, out _, out var cmax) && cmax > cmin)
            {
                display.SupportsContrast = true;
                display.DdcContrastMin = (int)cmin;
                display.DdcContrastMax = (int)cmax;
            }

            if (!display.SupportsBrightness && !display.SupportsContrast)
            {
                // Alguns drivers respondem apenas ao VCP direto.
                if (NativeMethods.GetVCPFeatureAndVCPFeatureReply(display.PhysicalHandle, 0x10, out _, out _, out var vcpMax) && vcpMax > 0)
                {
                    display.SupportsBrightness = true;
                    display.DdcBrightnessMin = 0;
                    display.DdcBrightnessMax = (int)vcpMax;
                }
            }

            display.SupportsDdc = display.SupportsBrightness || display.SupportsContrast;
        }
        catch (Exception ex)
        {
            Diag($"{display.Name}: excecao {ex.GetType().Name} {ex.Message}");
            display.SupportsDdc = false;
        }
    }

    private static Dictionary<string, (string Friendly, uint Technology)> QueryFriendlyNames()
    {
        var map = new Dictionary<string, (string Friendly, uint Technology)>(StringComparer.OrdinalIgnoreCase);

        try
        {
            if (NativeMethods.GetDisplayConfigBufferSizes(NativeMethods.QDC_ONLY_ACTIVE_PATHS, out var pathCount, out var modeCount) != 0)
            {
                return map;
            }

            var paths = new NativeMethods.DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new NativeMethods.DISPLAYCONFIG_MODE_INFO[modeCount];

            if (NativeMethods.QueryDisplayConfig(
                    NativeMethods.QDC_ONLY_ACTIVE_PATHS,
                    ref pathCount,
                    paths,
                    ref modeCount,
                    modes,
                    IntPtr.Zero) != 0)
            {
                return map;
            }

            for (var i = 0; i < pathCount; i++)
            {
                var source = new NativeMethods.DISPLAYCONFIG_SOURCE_DEVICE_NAME();
                source.header.type = NativeMethods.DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME;
                source.header.size = (uint)Marshal.SizeOf<NativeMethods.DISPLAYCONFIG_SOURCE_DEVICE_NAME>();
                source.header.adapterId = paths[i].sourceInfo.adapterId;
                source.header.id = paths[i].sourceInfo.id;
                source.viewGdiDeviceName = string.Empty;

                if (NativeMethods.DisplayConfigGetDeviceInfo(ref source) != 0 || string.IsNullOrWhiteSpace(source.viewGdiDeviceName))
                {
                    continue;
                }

                var target = new NativeMethods.DISPLAYCONFIG_TARGET_DEVICE_NAME();
                target.header.type = NativeMethods.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
                target.header.size = (uint)Marshal.SizeOf<NativeMethods.DISPLAYCONFIG_TARGET_DEVICE_NAME>();
                target.header.adapterId = paths[i].targetInfo.adapterId;
                target.header.id = paths[i].targetInfo.id;
                target.monitorFriendlyDeviceName = string.Empty;
                target.monitorDevicePath = string.Empty;

                var hasTarget = NativeMethods.DisplayConfigGetDeviceInfo(ref target) == 0;

                // A tecnologia de saida INTERNAL (0x80000000) identifica a tela do notebook.
                // O campo flags nao serve para isso: o bit 1 indica apenas nome vindo do EDID.
                var technology = paths[i].targetInfo.outputTechnology;
                var friendly = hasTarget ? target.monitorFriendlyDeviceName : string.Empty;
                map[source.viewGdiDeviceName] = (friendly, technology);
            }
        }
        catch
        {
        }

        return map;
    }
}

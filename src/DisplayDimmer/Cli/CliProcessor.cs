using System.Globalization;
using System.Text;
using System.Text.Json;
using DisplayDimmer.Interop;
using DisplayDimmer.Models;
using DisplayDimmer.Services;

namespace DisplayDimmer.Cli;

/// <summary>Contexto compartilhado entre a execucao local e a instancia ja aberta.</summary>
public sealed class CliContext
{
    public required DisplayController Controller { get; init; }

    public required SettingsService Settings { get; init; }

    public Action? ShowWindow { get; init; }

    public Action? RequestExit { get; init; }

    public Func<string, bool>? SetTheme { get; init; }
}

/// <summary>Interpreta os argumentos de linha de comando e devolve o texto de resposta.</summary>
public static class CliProcessor
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static bool HasCommands(string[] args)
        => args.Any(a => !a.Equals("--minimized", StringComparison.OrdinalIgnoreCase)
                         && !a.Equals("--ui", StringComparison.OrdinalIgnoreCase));

    /// <summary>Comandos que apenas consultam o estado e nao devem alterar a tela.</summary>
    public static bool IsReadOnly(string[] args)
    {
        var commands = args
            .Where(a => !a.StartsWith('-'))
            .Select(a => a.ToLowerInvariant())
            .ToList();

        if (args.Any(a => a is "--list" or "--get" or "--status" or "--help" or "-l" or "-g" or "-h"))
        {
            return true;
        }

        return commands.Count > 0 && commands[0] is "list" or "get" or "status" or "help" or "ping";
    }

    public static string Execute(string[] args, CliContext context)
    {
        var request = Parse(args);

        try
        {
            var result = request.Command switch
            {
                "help" or null => Help(),
                "ping" => "pong",
                "list" => List(context, request.Json),
                "get" => Get(context, request, request.Json),
                "status" => Status(context, request.Json),
                "set" => Set(context, request),
                "preset" => ApplyPreset(context, request),
                "reset" => Reset(context, request),
                "automation" => Automation(context, request),
                "theme" => Theme(context, request),
                "show" => Show(context),
                "exit" => Exit(context),
                "debug" => Debug(context),
                "ddc" => DdcTest(context),
                _ => $"Comando desconhecido: {request.Command}\n\n{Help()}",
            };

            context.Settings.Flush();
            return result;
        }
        catch (Exception ex)
        {
            return $"Erro: {ex.Message}";
        }
    }

    private sealed class Request
    {
        public string? Command { get; set; }

        public bool Json { get; set; }

        public string? Target { get; set; }

        public string? PresetName { get; set; }

        public string? Automation { get; set; }

        public string? Theme { get; set; }

        public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private static Request Parse(string[] args)
    {
        var request = new Request();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg is "--json" or "-j")
            {
                request.Json = true;
                continue;
            }

            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                var key = arg[2..].ToLowerInvariant();
                switch (key)
                {
                    case "list":
                    case "get":
                    case "status":
                    case "show":
                    case "exit":
                    case "help":
                    case "reset":
                    case "ping":
                        request.Command ??= key;
                        break;

                    case "debug":
                    case "ddc":
                        request.Command ??= key;
                        break;

                    case "set":
                        request.Command ??= "set";
                        while (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                        {
                            AddValue(request, args[++i], ref i, args);
                        }

                        break;

                    case "preset":
                        request.Command ??= "preset";
                        request.PresetName = Next(args, ref i);
                        break;

                    case "display":
                    case "target":
                        request.Target = Next(args, ref i);
                        break;

                    case "automation":
                        request.Command ??= "automation";
                        request.Automation = Next(args, ref i);
                        break;

                    case "theme":
                        request.Command ??= "theme";
                        request.Theme = Next(args, ref i);
                        break;

                    case "brightness":
                        request.Command ??= "set";
                        request.Values["brightness"] = Next(args, ref i) ?? string.Empty;
                        break;

                    case "contrast":
                        request.Command ??= "set";
                        request.Values["contrast"] = Next(args, ref i) ?? string.Empty;
                        break;

                    case "temperature":
                    case "temp":
                        request.Command ??= "set";
                        request.Values["temperature"] = Next(args, ref i) ?? string.Empty;
                        break;

                    case "black":
                    case "preto":
                        request.Command ??= "set";
                        request.Values["black"] = Next(args, ref i) ?? string.Empty;
                        break;

                    case "white":
                    case "branco":
                        request.Command ??= "set";
                        request.Values["white"] = Next(args, ref i) ?? string.Empty;
                        break;

                    case "software":
                        request.Command ??= "set";
                        request.Values["software"] = Next(args, ref i) ?? string.Empty;
                        break;
                }

                continue;
            }

            if (arg.StartsWith('-') && arg.Length == 2)
            {
                switch (arg[1])
                {
                    case 'l':
                        request.Command ??= "list";
                        break;
                    case 'g':
                        request.Command ??= "get";
                        break;
                    case 's':
                        request.Command ??= "set";
                        break;
                    case 'p':
                        request.Command ??= "preset";
                        request.PresetName = Next(args, ref i);
                        break;
                    case 'd':
                        request.Target = Next(args, ref i);
                        break;
                    case 'b':
                        request.Command ??= "set";
                        request.Values["brightness"] = Next(args, ref i) ?? string.Empty;
                        break;
                    case 'c':
                        request.Command ??= "set";
                        request.Values["contrast"] = Next(args, ref i) ?? string.Empty;
                        break;
                    case 't':
                        request.Command ??= "set";
                        request.Values["temperature"] = Next(args, ref i) ?? string.Empty;
                        break;
                    case 'h':
                        request.Command = "help";
                        break;
                }

                continue;
            }

            request.Command ??= arg.ToLowerInvariant();
        }

        return request;
    }

    private static void AddValue(Request request, string token, ref int index, string[] args)
    {
        var separator = token.IndexOf('=');
        if (separator > 0)
        {
            request.Values[token[..separator].Trim().ToLowerInvariant()] = token[(separator + 1)..].Trim();
            return;
        }

        if (index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            request.Values[token.Trim().ToLowerInvariant()] = args[++index].Trim();
        }
    }

    private static string? Next(string[] args, ref int index)
    {
        if (index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            return args[++index];
        }

        return null;
    }

    private static string Help()
        => """
           Display Dimmer - controle de brilho por linha de comando

           Uso:
             DisplayDimmer.exe --list [--json]
             DisplayDimmer.exe --get [--display all|1|nome] [--json]
             DisplayDimmer.exe --set brightness=40 contrast=55 temp=4200 black=32 white=255 [--display 2]
             DisplayDimmer.exe --preset "Noite"
             DisplayDimmer.exe --reset [--display all]
             DisplayDimmer.exe --automation on|off
             DisplayDimmer.exe --theme dark|light|system
             DisplayDimmer.exe --show
             DisplayDimmer.exe --exit

           Atalhos: -l list, -g get, -s set, -p preset, -d display, -b brilho, -c contraste, -t temperatura, -j json

           Exemplos no PowerShell:
             .\DisplayDimmer.exe -b 45
             .\DisplayDimmer.exe --preset "Noite" --display 2
             .\DisplayDimmer.exe --set brightness=70 temp=5000
           """;

    private static string ResolveTarget(string? spec, DisplayController controller)
    {
        if (string.IsNullOrWhiteSpace(spec) || spec.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return DisplayController.AllTargets;
        }

        if (spec.StartsWith(DisplayController.GroupPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return spec;
        }

        if (int.TryParse(spec, out var index) && index >= 1 && index <= controller.Displays.Count)
        {
            return controller.Displays[index - 1].Id;
        }

        var byId = controller.Displays.FirstOrDefault(d => d.Id.Equals(spec, StringComparison.OrdinalIgnoreCase));
        if (byId is not null)
        {
            return byId.Id;
        }

        var byName = controller.Displays.FirstOrDefault(d => d.Name.Contains(spec, StringComparison.OrdinalIgnoreCase));
        if (byName is not null)
        {
            return byName.Id;
        }

        var group = controller.Settings.Groups.FirstOrDefault(g => g.Name.Equals(spec, StringComparison.OrdinalIgnoreCase));
        if (group is not null)
        {
            return DisplayController.GroupPrefix + group.Id;
        }

        return DisplayController.AllTargets;
    }

    private static string List(CliContext context, bool json)
    {
        var displays = context.Controller.Displays;

        if (json)
        {
            var payload = displays.Select(d =>
            {
                var state = context.Settings.Settings.GetOrCreate(d.Id);
                return new
                {
                    id = d.Id,
                    nome = d.Name,
                    dispositivo = d.DeviceName,
                    interno = d.IsInternal,
                    principal = d.IsPrimary,
                    largura = d.Width,
                    altura = d.Height,
                    ddc = d.SupportsDdc,
                    brilho = state.Brightness,
                    contraste = state.Contrast,
                    temperatura = state.Temperature,
                };
            });

            return JsonSerializer.Serialize(payload, JsonOptions);
        }

        var builder = new StringBuilder();
        builder.AppendLine($"{displays.Count} monitor(es):");

        for (var i = 0; i < displays.Count; i++)
        {
            var display = displays[i];
            var state = context.Settings.Settings.GetOrCreate(display.Id);
            var tags = new List<string> { $"{display.Width}x{display.Height}" };
            if (display.IsInternal)
            {
                tags.Add("interno");
            }

            tags.Add(display.SupportsDdc ? "DDC/CI" : "software");

            builder.AppendLine(
                $"  {i + 1}. {display.Name}  ({string.Join(" · ", tags)})  brilho {state.Brightness}% · contraste {state.Contrast}% · {state.Temperature} K");
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>Diagnostico de DDC/CI: le e reescreve o valor atual para medir suporte real.</summary>
    private static string DdcTest(CliContext context)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Teste DDC/CI (nao altera o brilho: regrava o valor atual)");

        foreach (var display in context.Controller.Displays)
        {
            builder.AppendLine($"- {display.Name}  handle=0x{display.PhysicalHandle.ToInt64():X}");
            builder.AppendLine($"    suporta brilho: {display.SupportsBrightness}  faixa {display.DdcBrightnessMin}-{display.DdcBrightnessMax}");

            if (display.PhysicalHandle == IntPtr.Zero)
            {
                builder.AppendLine("    sem handle fisico: o Windows nao expos DDC/CI para este monitor");
                continue;
            }

            var hasCount = NativeMethods.GetNumberOfPhysicalMonitorsFromHMONITOR(display.MonitorHandle, out var physicalCount);
            builder.AppendLine($"    GetNumberOfPhysicalMonitors: {hasCount} count={physicalCount} erro {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}");

            if (physicalCount > 0)
            {
                var array = new NativeMethods.PHYSICAL_MONITOR[physicalCount];
                var okArray = NativeMethods.GetPhysicalMonitorsFromHMONITOR(display.MonitorHandle, physicalCount, array);
                builder.AppendLine($"    GetPhysicalMonitorsFromHMONITOR: {okArray} erro {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}");
                for (var index = 0; index < array.Length; index++)
                {
                    builder.AppendLine($"      [{index}] handle=0x{array[index].hPhysicalMonitor.ToInt64():X} desc=\"{array[index].szPhysicalMonitorDescription}\"");
                }
            }

            var okRead = NativeMethods.GetMonitorBrightness(display.PhysicalHandle, out var min, out var cur, out var max);
            builder.AppendLine($"    GetMonitorBrightness: {okRead} (min {min}, atual {cur}, max {max}) erro {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}");

            var okVcp = NativeMethods.GetVCPFeatureAndVCPFeatureReply(display.PhysicalHandle, 0x10, out _, out var vcpValue, out var vcpMax);
            builder.AppendLine($"    VCP 0x10 leitura: {okVcp} (atual {vcpValue}, max {vcpMax}) erro {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}");

            var okSet = NativeMethods.SetMonitorBrightness(display.PhysicalHandle, cur);
            builder.AppendLine($"    SetMonitorBrightness({cur}): {okSet} erro {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}");

            var okVcpSet = NativeMethods.SetVCPFeature(display.PhysicalHandle, 0x10, vcpValue);
            builder.AppendLine($"    SetVCPFeature(0x10, {vcpValue}): {okVcpSet} erro {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}");
        }

        return builder.ToString().TrimEnd();
    }

    private static string Debug(CliContext context)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Settings: {context.Settings.FilePath}");
        builder.AppendLine($"Monitores: {context.Controller.Displays.Count}");

        foreach (var display in context.Controller.Displays)
        {
            var state = context.Settings.Settings.GetOrCreate(display.Id);
            builder.AppendLine($"- {display.Name}");
            builder.AppendLine($"    id: {display.Id}");
            builder.AppendLine($"    gdi: {display.DeviceName}  area: {display.X},{display.Y} {display.Width}x{display.Height}  escala: {display.Scale:0.##}");
            builder.AppendLine($"    interno: {display.IsInternal}  principal: {display.IsPrimary}  tecnologia: 0x{display.OutputTechnology:X}");
            builder.AppendLine($"    ddc: {display.SupportsDdc}  brilho hw: {display.SupportsBrightness} ({display.DdcBrightnessMin}-{display.DdcBrightnessMax})  contraste hw: {display.SupportsContrast} ({display.DdcContrastMin}-{display.DdcContrastMax})");
            builder.AppendLine($"    descricao: {display.Description}");
            builder.AppendLine($"    estado: brilho {state.Brightness}% contraste {state.Contrast}% {state.Temperature}K preto {ColorMath.Hex(state.BlackLevel)} branco {ColorMath.Hex(state.WhiteLevel)} controle={state.Control}");
            builder.AppendLine($"    ultima aplicacao: {display.LastApply}");

            var ramp = GammaRamp.Read(display.DeviceName);
            if (ramp is null)
            {
                builder.AppendLine("    rampa atual: nao foi possivel ler");
            }
            else
            {
                builder.AppendLine($"    rampa atual: preto 0x{ramp[0]:X4}  50% 0x{ramp[128]:X4}  branco 0x{ramp[255]:X4}");
            }
        }

        builder.AppendLine($"overlay: {context.Settings.Settings.UseOverlayForSoftwareDimming}");
        return builder.ToString().TrimEnd();
    }

    private static string Get(CliContext context, Request request, bool json)
    {
        var target = ResolveTarget(request.Target, context.Controller);
        var (brightness, contrast, temperature) = context.Controller.GetValues(target);

        if (json)
        {
            return JsonSerializer.Serialize(new
            {
                alvo = target,
                brilho = brightness,
                contraste = contrast,
                temperatura = temperature,
                pretoMaximo = ColorMath.Hex(context.Controller.GetLevels(target).Black),
            }, JsonOptions);
        }

        return $"Alvo: {target}\nBrilho: {brightness}%\nContraste: {contrast}%\nTemperatura: {temperature} K";
    }

    private static string Status(CliContext context, bool json)
    {
        var settings = context.Settings.Settings;
        var (brightness, contrast, temperature) = context.Controller.GetValues(settings.ActiveTarget);

        if (json)
        {
            return JsonSerializer.Serialize(new
            {
                automacoes = settings.AutomationsEnabled,
                tema = settings.Theme,
                alvo = settings.ActiveTarget,
                pretoMaximo = ColorMath.Hex(context.Controller.GetLevels(settings.ActiveTarget).Black),
                brilho = brightness,
                contraste = contrast,
                temperatura = temperature,
                monitores = context.Controller.Displays.Count,
            }, JsonOptions);
        }

        return $"Automacoes: {(settings.AutomationsEnabled ? "ativas" : "pausadas")}\n"
               + $"Tema: {settings.Theme}\n"
               + $"Preto maximo: {ColorMath.Hex(context.Controller.GetLevels(settings.ActiveTarget).Black)}\n"
               + $"Brilho: {brightness}% · Contraste: {contrast}% · {temperature} K";
    }

    private static string Set(CliContext context, Request request)
    {
        var target = ResolveTarget(request.Target, context.Controller);
        var (brightness, contrast, temperature) = context.Controller.GetValues(target);
        var changed = false;

        if (TryGet(request, "brightness", out var b))
        {
            brightness = Math.Clamp(b, 0, 100);
            context.Controller.SetBrightness(target, brightness, relative: false);
            changed = true;
        }

        if (TryGet(request, "contrast", out var c))
        {
            contrast = Math.Clamp(c, 0, 100);
            context.Controller.SetContrast(target, contrast);
            changed = true;
        }

        if (TryGet(request, "temperature", out var t))
        {
            temperature = Math.Clamp(t, ColorMath.MinKelvin, ColorMath.MaxKelvin);
            context.Controller.SetTemperature(target, temperature);
            changed = true;
        }

        if (TryGetLevel(request, "black", out var black))
        {
            context.Controller.SetBlackLevel(target, black);
            changed = true;
        }

        if (TryGetLevel(request, "white", out var white))
        {
            context.Controller.SetWhiteLevel(target, white);
            changed = true;
        }

        if (request.Values.TryGetValue("software", out var software))
        {
            var useSoftware = software is "on" or "true" or "1";
            context.Settings.Settings.UseOverlayForSoftwareDimming = useSoftware;
            context.Controller.ReapplyAll();
            changed = true;
        }

        if (!changed)
        {
            return "Nenhum valor informado. Use brightness=, contrast= ou temp=.";
        }

        var (blackLevel, whiteLevel) = context.Controller.GetLevels(target);
        return $"Brilho {brightness}% · Contraste {contrast}% · {temperature} K · preto {ColorMath.Hex(blackLevel)} · branco {ColorMath.Hex(whiteLevel)}";
    }

    private static bool TryGet(Request request, string key, out int value)
    {
        value = 0;
        return request.Values.TryGetValue(key, out var raw)
               && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>Aceita numero (0-255) ou codigo de cor (#RRGGBB, #RGB).</summary>
    private static bool TryGetLevel(Request request, string key, out int value)
    {
        value = 0;
        if (!request.Values.TryGetValue(key, out var raw))
        {
            return false;
        }

        return TryGet(request, key, out value) || ColorMath.TryParseHexLevel(raw, out value);
    }

    private static string ApplyPreset(CliContext context, Request request)
    {
        var preset = context.Controller.Settings.FindPreset(request.PresetName)
                     ?? context.Controller.Settings.Presets.FirstOrDefault(p =>
                         p.Name.Equals(request.PresetName, StringComparison.OrdinalIgnoreCase));

        if (preset is null)
        {
            return $"Preset nao encontrado: {request.PresetName}";
        }

        var target = ResolveTarget(request.Target, context.Controller);
        context.Controller.ApplyPreset(preset, target);
        return $"Preset aplicado: {preset.Name} ({preset.Summary})";
    }

    private static string Reset(CliContext context, Request request)
    {
        var target = ResolveTarget(request.Target, context.Controller);
        context.Controller.Reset(target);
        return "Niveis restaurados.";
    }

    private static string Automation(CliContext context, Request request)
    {
        var enabled = request.Automation is "on" or "true" or "1" or "ativar";
        context.Settings.Settings.AutomationsEnabled = enabled;
        return enabled ? "Automacoes ativadas." : "Automacoes pausadas.";
    }

    private static string Theme(CliContext context, Request request)
    {
        var theme = ThemeService.Normalize(request.Theme);
        context.Settings.Settings.Theme = theme;
        context.SetTheme?.Invoke(theme);
        return $"Tema: {theme}";
    }

    private static string Show(CliContext context)
    {
        context.ShowWindow?.Invoke();
        return "Janela aberta.";
    }

    private static string Exit(CliContext context)
    {
        var exit = context.RequestExit;
        if (exit is null)
        {
            return "Encerrando.";
        }

        // Adia o encerramento para que a resposta chegue ao processo que chamou.
        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200),
        };

        timer.Tick += (_, _) =>
        {
            timer.Stop();
            exit();
        };

        timer.Start();
        return "Encerrando.";
    }
}

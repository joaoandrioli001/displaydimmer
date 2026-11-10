using System.Text.Json.Serialization;

namespace DisplayDimmer.Models;

/// <summary>Configuracao completa do aplicativo, persistida em JSON.</summary>
public sealed class AppSettings
{
    public int Version { get; set; } = 1;

    /// <summary>dark, light ou system.</summary>
    public string Theme { get; set; } = "dark";

    public bool StartWithWindows { get; set; }

    public bool StartMinimized { get; set; } = true;

    /// <summary>Usa sobreposicao preta para escurecer por software em vez de gamma.</summary>
    public bool UseOverlayForSoftwareDimming { get; set; }

    public int TrayStepPercent { get; set; } = 5;

    public bool EnableNativeBrightnessKeys { get; set; } = true;

    public bool AutomationsEnabled { get; set; } = true;

    public string ActiveTarget { get; set; } = "all";

    public List<Preset> Presets { get; set; } = new();

    public List<MonitorGroup> Groups { get; set; } = new();

    public List<ScheduleEntry> Schedules { get; set; } = new();

    public List<AppRule> Rules { get; set; } = new();

    public Dictionary<string, DisplayState> Displays { get; set; } = new();

    public HotkeySettings Hotkeys { get; set; } = new();

    public DisplayState GetOrCreate(string displayId)
    {
        if (string.IsNullOrWhiteSpace(displayId))
        {
            return DisplayState.Neutral();
        }

        if (!Displays.TryGetValue(displayId, out var state))
        {
            state = DisplayState.Neutral();
            Displays[displayId] = state;
        }

        return state;
    }

    public Preset? FindPreset(string? id)
        => string.IsNullOrWhiteSpace(id) ? null : Presets.FirstOrDefault(p => p.Id == id);

    public MonitorGroup? FindGroup(string? id)
        => string.IsNullOrWhiteSpace(id) ? null : Groups.FirstOrDefault(g => g.Id == id);

    public static AppSettings CreateDefault()
    {
        var settings = new AppSettings();

        settings.Presets.Add(new Preset
        {
            Id = "day",
            Name = "Dia",
            Brightness = 100,
            Contrast = 50,
            Temperature = 6500,
            BuiltIn = true,
        });

        settings.Presets.Add(new Preset
        {
            Id = "night",
            Name = "Noite",
            Brightness = 45,
            Contrast = 48,
            Temperature = 3400,
            BuiltIn = true,
        });

        settings.Presets.Add(new Preset
        {
            Id = "reading",
            Name = "Leitura",
            Brightness = 70,
            Contrast = 60,
            Temperature = 5000,
            BuiltIn = true,
        });

        settings.Schedules.Add(new ScheduleEntry
        {
            Id = "sched-morning",
            Name = "Manha",
            Time = "07:00",
            PresetId = "day",
            TargetId = "all",
            Parameters = DisplayParameter.All,
        });

        settings.Schedules.Add(new ScheduleEntry
        {
            Id = "sched-night",
            Name = "Noite",
            Time = "21:30",
            PresetId = "night",
            TargetId = "all",
            Parameters = DisplayParameter.All,
        });

        return settings;
    }
}

/// <summary>Predefinicao de exibicao reutilizavel.</summary>
public sealed class Preset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    public string Name { get; set; } = "Novo preset";

    public DisplayParameter Parameters { get; set; } = DisplayParameter.All;

    public int Brightness { get; set; } = DisplayState.DefaultBrightness;

    public int Contrast { get; set; } = DisplayState.DefaultContrast;

    public int Temperature { get; set; } = DisplayState.DefaultTemperature;

    public int BlackLevel { get; set; } = DisplayState.DefaultBlackLevel;

    public int WhiteLevel { get; set; } = DisplayState.DefaultWhiteLevel;

    /// <summary>Valores especificos por monitor, quando o preset foi capturado com varios monitores.</summary>
    public Dictionary<string, DisplayState> PerDisplay { get; set; } = new();

    public bool BuiltIn { get; set; }

    [JsonIgnore]
    public string Summary
    {
        get
        {
            var parts = new List<string>();
            if (Parameters.HasFlag(DisplayParameter.Brightness))
            {
                parts.Add($"{Brightness}%");
            }

            if (Parameters.HasFlag(DisplayParameter.Contrast))
            {
                parts.Add($"contraste {Contrast}%");
            }

            if (Parameters.HasFlag(DisplayParameter.Temperature))
            {
                parts.Add($"{Temperature} K");
            }

            if (Parameters.HasFlag(DisplayParameter.Levels))
            {
                parts.Add($"preto {BlackLevel:X2} / branco {WhiteLevel:X2}");
            }

            return parts.Count == 0 ? "sem parametros" : string.Join(" · ", parts);
        }
    }

    public override string ToString() => Name;

    public DisplayState Resolve(string displayId)
    {
        if (PerDisplay.TryGetValue(displayId, out var specific))
        {
            return specific.Clone();
        }

        return new DisplayState
        {
            Brightness = Brightness,
            Contrast = Contrast,
            Temperature = Temperature,
            BlackLevel = BlackLevel,
            WhiteLevel = WhiteLevel,
        };
    }
}

/// <summary>Agendamento de horario.</summary>
public sealed class ScheduleEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    public string Name { get; set; } = "Novo agendamento";

    public bool Enabled { get; set; } = true;

    /// <summary>Horario no formato HH:mm.</summary>
    public string Time { get; set; } = "08:00";

    public string PresetId { get; set; } = string.Empty;

    public string TargetId { get; set; } = "all";

    public DisplayParameter Parameters { get; set; } = DisplayParameter.All;

    public int Brightness { get; set; } = DisplayState.DefaultBrightness;

    public int Contrast { get; set; } = DisplayState.DefaultContrast;

    public int Temperature { get; set; } = DisplayState.DefaultTemperature;

    /// <summary>Nome do preset, preenchido ao montar a lista de automacoes.</summary>
    [JsonIgnore]
    public string PresetName { get; set; } = string.Empty;

    [JsonIgnore]
    public string Detail
    {
        get
        {
            var preset = string.IsNullOrWhiteSpace(PresetName) ? "valores proprios" : PresetName;
            return $"{Time} · {preset}";
        }
    }

    [JsonIgnore]
    public string ToggleLabel => Enabled ? "Pausar" : "Ativar";
}

/// <summary>Regra aplicada quando um processo esta em primeiro plano.</summary>
public sealed class AppRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    public string Name { get; set; } = "Nova regra";

    public bool Enabled { get; set; } = true;

    /// <summary>Nome do processo sem extensao, por exemplo chrome.</summary>
    public string ProcessName { get; set; } = string.Empty;

    /// <summary>Aplica somente quando a janela esta em tela cheia.</summary>
    public bool FullscreenOnly { get; set; }

    public string PresetId { get; set; } = string.Empty;

    public string TargetId { get; set; } = "all";

    public DisplayParameter Parameters { get; set; } = DisplayParameter.All;

    public int Brightness { get; set; } = DisplayState.DefaultBrightness;

    public int Contrast { get; set; } = DisplayState.DefaultContrast;

    public int Temperature { get; set; } = DisplayState.DefaultTemperature;

    public bool RestoreOnExit { get; set; } = true;

    /// <summary>Nome do preset, preenchido ao montar a lista de automacoes.</summary>
    [JsonIgnore]
    public string PresetName { get; set; } = string.Empty;

    [JsonIgnore]
    public string Detail
    {
        get
        {
            var origem = string.IsNullOrWhiteSpace(ProcessName) ? "Tela cheia · qualquer aplicativo" : ProcessName;
            return string.IsNullOrWhiteSpace(PresetName) ? origem : $"{origem} · {PresetName}";
        }
    }

    [JsonIgnore]
    public string ToggleLabel => Enabled ? "Pausar" : "Ativar";
}

/// <summary>Agrupamento de monitores controlados juntos.</summary>
public sealed class MonitorGroup
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    public string Name { get; set; } = "Novo grupo";

    public List<string> DisplayIds { get; set; } = new();

    /// <summary>Resumo dos monitores do grupo, preenchido ao montar a lista.</summary>
    [JsonIgnore]
    public string DisplaySummary { get; set; } = string.Empty;
}

/// <summary>Atalhos globais configurados pelo usuario.</summary>
public sealed class HotkeySettings
{
    public string BrightnessUp { get; set; } = "Ctrl+Alt+Up";

    public string BrightnessDown { get; set; } = "Ctrl+Alt+Down";

    public string ShowWindow { get; set; } = "Ctrl+Alt+D";

    public string ToggleAutomations { get; set; } = "Ctrl+Alt+A";

    /// <summary>Atalho por preset: chave = id do preset.</summary>
    public Dictionary<string, string> Presets { get; set; } = new();
}

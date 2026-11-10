using System.Text.Json.Serialization;

namespace DisplayDimmer.Models;

/// <summary>Como o brilho e o contraste de um monitor sao aplicados.</summary>
public enum DisplayControl
{
    /// <summary>Usa DDC/CI quando funciona de verdade; cai para software quando nao.</summary>
    Auto,

    /// <summary>Forca DDC/CI (hardware).</summary>
    Hardware,

    /// <summary>Forca rampa de gamma (software).</summary>
    Software,
}

/// <summary>Valores de exibicao de um monitor.</summary>
public sealed class DisplayState
{
    public const int DefaultBrightness = 100;
    public const int DefaultContrast = 50;
    public const int DefaultTemperature = 6500;
    public const int DefaultBlackLevel = 0x20;
    public const int DefaultWhiteLevel = 0xFF;

    public int Brightness { get; set; } = DefaultBrightness;

    public int Contrast { get; set; } = DefaultContrast;

    public int Temperature { get; set; } = DefaultTemperature;

    /// <summary>Preto mais escuro possivel na rampa de gamma (0 a 255).</summary>
    public int BlackLevel { get; set; } = DefaultBlackLevel;

    /// <summary>Branco mais claro possivel na rampa de gamma (0 a 255).</summary>
    public int WhiteLevel { get; set; } = DefaultWhiteLevel;

    /// <summary>Modo de controle deste monitor.</summary>
    public DisplayControl Control { get; set; } = DisplayControl.Auto;

    /// <summary>Compatibilidade com configuracoes antigas que gravavam UseHardware.</summary>
    [JsonPropertyName("UseHardware")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LegacyUseHardware { get; set; }

    [JsonIgnore]
    public bool UseHardware
    {
        get => Control != DisplayControl.Software;
        set => Control = value ? DisplayControl.Auto : DisplayControl.Software;
    }

    public DisplayState Clone() => new()
    {
        Brightness = Brightness,
        Contrast = Contrast,
        Temperature = Temperature,
        BlackLevel = BlackLevel,
        WhiteLevel = WhiteLevel,
        Control = Control,
    };

    public void CopyFrom(DisplayState other)
    {
        Brightness = other.Brightness;
        Contrast = other.Contrast;
        Temperature = other.Temperature;
        BlackLevel = other.BlackLevel;
        WhiteLevel = other.WhiteLevel;
        Control = other.Control;
    }

    public static DisplayState Neutral() => new();

    public override string ToString() => $"{Brightness}% · {Contrast}% · {Temperature}K";
}

/// <summary>Parametros que uma automacao ou preset pode alterar.</summary>
[Flags]
public enum DisplayParameter
{
    None = 0,
    Brightness = 1,
    Contrast = 2,
    Temperature = 4,
    Levels = 8,
    All = Brightness | Contrast | Temperature | Levels,
}

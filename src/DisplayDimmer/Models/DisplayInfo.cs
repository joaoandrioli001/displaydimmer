namespace DisplayDimmer.Models;

/// <summary>Representa um monitor fisico conectado ao Windows.</summary>
public sealed class DisplayInfo
{
    /// <summary>Identificador estavel usado na configuracao (caminho do dispositivo ou nome GDI).</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Nome GDI, por exemplo \\.\DISPLAY1. Usado para a rampa de gamma.</summary>
    public string DeviceName { get; init; } = string.Empty;

    /// <summary>Nome amigavel do monitor (EDID), por exemplo "LG ULTRAGEAR".</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Descricao tecnica vinda do DDC/CI, quando disponivel.</summary>
    public string Description { get; set; } = string.Empty;

    public string AdapterName { get; init; } = string.Empty;

    public bool IsInternal { get; init; }

    /// <summary>Tecnologia de saida informada pelo Windows (0x80000000 = interno).</summary>
    public uint OutputTechnology { get; init; }

    public bool IsPrimary { get; init; }

    public int X { get; init; }

    public int Y { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }

    /// <summary>Escala do monitor em relacao a 96 DPI (1.0 = 100 por cento).</summary>
    public double Scale { get; init; } = 1.0;

    public IntPtr MonitorHandle { get; init; }

    /// <summary>Handle DDC/CI do monitor fisico. Zero quando o monitor nao suporta DDC/CI.</summary>
    public IntPtr PhysicalHandle { get; set; }

    public bool SupportsDdc { get; set; }

    public bool SupportsBrightness { get; set; }

    public bool SupportsContrast { get; set; }

    public int DdcBrightnessMin { get; set; }

    public int DdcBrightnessMax { get; set; } = 100;

    public int DdcContrastMin { get; set; }

    public int DdcContrastMax { get; set; } = 100;

    /// <summary>Resumo da ultima aplicacao (diagnostico).</summary>
    public string LastApply { get; set; } = string.Empty;

    /// <summary>Rotulo curto exibido no seletor de monitores.</summary>
    public string Label => IsInternal ? $"{Name} (tela do notebook)" : Name;

    public string Detail
    {
        get
        {
            var tecnologia = SupportsDdc ? "DDC/CI" : "software";
            return $"{Width}x{Height} · {tecnologia}";
        }
    }

    public bool ContainsPoint(int px, int py)
        => px >= X && px < X + Width && py >= Y && py < Y + Height;
}

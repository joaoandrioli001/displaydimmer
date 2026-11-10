namespace DisplayDimmer.Services;

/// <summary>Calculos de cor usados na rampa de gamma (brilho, contraste e temperatura).</summary>
public static class ColorMath
{
    public const int NeutralContrast = 50;
    public const int NeutralKelvin = 6500;
    public const int MinKelvin = 1900;
    public const int MaxKelvin = 10000;

    /// <summary>Preto mais escuro permitido quando o clamp esta ligado: #202020.</summary>
    public const int BlackFloor = 0x20;

    private static readonly (double R, double G, double B) KelvinReference = KelvinToRgb(NeutralKelvin);

    /// <summary>
    /// Monta uma rampa de 768 posicoes (R, G, B) para o SetDeviceGammaRamp.
    /// </summary>
    /// <param name="brightnessFactor">Fator de brilho por software entre 0 e 1.</param>
    /// <param name="contrastPercent">Contraste de 0 a 100, com 50 sendo neutro.</param>
    /// <param name="kelvin">Temperatura de cor entre 1900 K e 10000 K.</param>
    /// <param name="blackLevel">Preto maximo em 0-255: nenhum pixel fica mais escuro que isso.</param>
    /// <param name="whiteLevel">Branco maximo em 0-255: nenhum pixel fica mais claro que isso.</param>
    public static ushort[] BuildRamp(
        double brightnessFactor,
        double contrastPercent,
        int kelvin,
        int blackLevel,
        int whiteLevel)
    {
        var contrast = 0.4 + Math.Clamp(contrastPercent, 0, 100) / 100.0 * 1.2;
        var brightness = Math.Clamp(brightnessFactor, 0, 1);
        var (tr, tg, tb) = TemperatureMultipliers(kelvin);

        var black = Math.Clamp(blackLevel, 0, 250) / 255.0;
        var white = Math.Clamp(whiteLevel, blackLevel + 5, 255) / 255.0;
        var range = white - black;

        var ramp = new ushort[GammaRampSize * 3];
        for (var i = 0; i < GammaRampSize; i++)
        {
            var value = i / 255.0;
            value = (value - 0.5) * contrast + 0.5;
            value *= brightness;
            value = Math.Clamp(value, 0.0, 1.0);

            ramp[i] = ToRampValue(black + range * Math.Clamp(value * tr, 0.0, 1.0));
            ramp[GammaRampSize + i] = ToRampValue(black + range * Math.Clamp(value * tg, 0.0, 1.0));
            ramp[GammaRampSize * 2 + i] = ToRampValue(black + range * Math.Clamp(value * tb, 0.0, 1.0));
        }

        return ramp;
    }

    /// <summary>Representacao hexadecimal de um nivel de 0 a 255.</summary>
    public static string Hex(int level) => $"#{Math.Clamp(level, 0, 255):X2}{Math.Clamp(level, 0, 255):X2}{Math.Clamp(level, 0, 255):X2}";

    /// <summary>
    /// Interpreta um codigo de cor digitado: #RRGGBB, #RGB ou um nivel de 0 a 255 em hexadecimal.
    /// Para cores nao neutras usa a media dos canais, porque os niveis sao tons de cinza.
    /// </summary>
    public static bool TryParseHexLevel(string? text, out int level)
    {
        level = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var value = text.Trim().TrimStart('#').Trim();

        if (value.Length is 1 or 2)
        {
            return int.TryParse(value, System.Globalization.NumberStyles.HexNumber, null, out level)
                   && level is >= 0 and <= 255;
        }

        if (value.Length == 3)
        {
            if (!int.TryParse(value, System.Globalization.NumberStyles.HexNumber, null, out var shortValue))
            {
                return false;
            }

            var r = (shortValue >> 8) & 0xF;
            var g = (shortValue >> 4) & 0xF;
            var b = shortValue & 0xF;
            level = (int)Math.Round((r * 17 + g * 17 + b * 17) / 3.0);
            return true;
        }

        if (value.Length == 6)
        {
            if (!int.TryParse(value, System.Globalization.NumberStyles.HexNumber, null, out var rgb))
            {
                return false;
            }

            var r = (rgb >> 16) & 0xFF;
            var g = (rgb >> 8) & 0xFF;
            var b = rgb & 0xFF;
            level = (int)Math.Round((r + g + b) / 3.0);
            return true;
        }

        return false;
    }

    public const int GammaRampSize = 256;

    private static ushort ToRampValue(double value) => (ushort)Math.Clamp(Math.Round(value * 65535.0), 0, 65535);

    /// <summary>Multiplicadores por canal normalizados para 6500 K = (1, 1, 1).</summary>
    public static (double R, double G, double B) TemperatureMultipliers(int kelvin)
    {
        var (r, g, b) = KelvinToRgb(kelvin);
        return (
            Math.Clamp(r / KelvinReference.R, 0, 1),
            Math.Clamp(g / KelvinReference.G, 0, 1),
            Math.Clamp(b / KelvinReference.B, 0, 1));
    }

    /// <summary>Aproximacao de Tanner Helland para a cor de um corpo negro.</summary>
    public static (double R, double G, double B) KelvinToRgb(int kelvin)
    {
        var t = Math.Clamp(kelvin, MinKelvin, MaxKelvin) / 100.0;

        double r;
        double g;
        double b;

        if (t <= 66)
        {
            r = 255;
            g = 99.4708025861 * Math.Log(t) - 161.1195681661;
            b = t <= 19 ? 0 : 138.5177312231 * Math.Log(t - 10) - 305.0447927307;
        }
        else
        {
            r = 329.698727446 * Math.Pow(t - 60, -0.1332047592);
            g = 288.1221695283 * Math.Pow(t - 60, -0.0755148492);
            b = 255;
        }

        return (Math.Clamp(r, 0, 255), Math.Clamp(g, 0, 255), Math.Clamp(b, 0, 255));
    }

    /// <summary>Rotulo da temperatura no formato usado na janela principal.</summary>
    public static string TemperatureLabel(int kelvin)
    {
        if (Math.Abs(kelvin - NeutralKelvin) <= 50)
        {
            return $"Neutral · {kelvin:N0} K";
        }

        return kelvin < NeutralKelvin ? $"Warm · {kelvin:N0} K" : $"Cool · {kelvin:N0} K";
    }

    /// <summary>Rotulo do contraste no formato usado na janela principal.</summary>
    public static string ContrastLabel(int contrast)
    {
        if (contrast == NeutralContrast)
        {
            return $"Neutral · {contrast}%";
        }

        return contrast < NeutralContrast ? $"Soft · {contrast}%" : $"Sharp · {contrast}%";
    }

    /// <summary>Estima o preto resultante para exibir na interface.</summary>
    public static string BlackLevelPreview(bool clampBlack)
        => clampBlack ? "#202020" : "#000000";
}

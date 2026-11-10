using DisplayDimmer.Interop;
using DisplayDimmer.Models;

namespace DisplayDimmer.Services;

/// <summary>
/// Aplica brilho, contraste e temperatura nos monitores, escolhendo entre DDC/CI (hardware)
/// e rampa de gamma/sobreposicao (software). Mantem os niveis relativos ao ajustar todos.
/// </summary>
public sealed class DisplayController : IDisposable
{
    public const string AllTargets = "all";
    public const string GroupPrefix = "group:";

    private readonly SettingsService _settingsService;
    private readonly OverlayService _overlay;
    private readonly object _gate = new();
    private readonly HashSet<string> _hardwareFailed = new();
    private readonly HashSet<string> _hardwareVerified = new();
    private List<DisplayInfo> _displays = new();

    public DisplayController(SettingsService settingsService, OverlayService overlay)
    {
        _settingsService = settingsService;
        _overlay = overlay;
    }

    public event EventHandler? DisplaysChanged;

    public event EventHandler? StateChanged;

    public IReadOnlyList<DisplayInfo> Displays => _displays;

    public AppSettings Settings => _settingsService.Settings;

    /// <summary>Quando falso, a enumeracao nao altera nada (usado por comandos somente leitura).</summary>
    public bool ApplyOnRefresh { get; set; } = true;

    public void Refresh()
    {
        lock (_gate)
        {
            MonitorEnumerator.ReleaseHandles(_displays);
            _displays = MonitorEnumerator.Enumerate();
            _hardwareFailed.Clear();
            _hardwareVerified.Clear();

            foreach (var display in _displays)
            {
                if (!Settings.Displays.ContainsKey(display.Id))
                {
                    // Primeira vez com este monitor: adota o que ja esta no painel em vez de forcar 100%.
                    var state = Settings.GetOrCreate(display.Id);

                    if (display.SupportsDdc && display.SupportsBrightness
                        && DdcCi.TryReadBrightness(display.PhysicalHandle, out var brightness))
                    {
                        state.Brightness = brightness;
                    }

                    if (display.SupportsDdc && display.SupportsContrast
                        && DdcCi.TryReadContrast(display.PhysicalHandle, out var contrast))
                    {
                        state.Contrast = contrast;
                    }
                }
                else
                {
                    Settings.GetOrCreate(display.Id);
                }
            }

            if (ApplyOnRefresh)
            {
                foreach (var display in _displays)
                {
                    ApplyDisplay(display);
                }
            }
        }

        DisplaysChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Reaplica os valores salvos sem reenumerar (apos trocar tema, por exemplo).</summary>
    public void ReapplyAll()
    {
        foreach (var display in _displays.ToList())
        {
            ApplyDisplay(display);
        }
    }

    public DisplayInfo? FindDisplay(string id) => _displays.FirstOrDefault(d => d.Id == id);

    public IReadOnlyList<DisplayInfo> ResolveTarget(string? target)
    {
        if (string.IsNullOrWhiteSpace(target) || target == AllTargets)
        {
            return _displays;
        }

        if (target.StartsWith(GroupPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var group = Settings.FindGroup(target[GroupPrefix.Length..]);
            if (group is null)
            {
                return _displays;
            }

            var members = _displays.Where(d => group.DisplayIds.Contains(d.Id)).ToList();
            return members.Count > 0 ? members : _displays;
        }

        var single = _displays.Where(d => d.Id == target).ToList();
        return single.Count > 0 ? single : _displays;
    }

    /// <summary>Valores medios do alvo, usados para posicionar os sliders.</summary>
    public (int Brightness, int Contrast, int Temperature) GetValues(string? target)
    {
        var displays = ResolveTarget(target);
        if (displays.Count == 0)
        {
            return (DisplayState.DefaultBrightness, DisplayState.DefaultContrast, DisplayState.DefaultTemperature);
        }

        return (
            (int)Math.Round(displays.Average(d => Settings.GetOrCreate(d.Id).Brightness)),
            (int)Math.Round(displays.Average(d => Settings.GetOrCreate(d.Id).Contrast)),
            (int)Math.Round(displays.Average(d => Settings.GetOrCreate(d.Id).Temperature)));
    }

    /// <summary>Niveis de preto e branco do primeiro monitor do alvo.</summary>
    public (int Black, int White) GetLevels(string? target)
    {
        var display = ResolveTarget(target).FirstOrDefault();
        if (display is null)
        {
            return (DisplayState.DefaultBlackLevel, DisplayState.DefaultWhiteLevel);
        }

        var state = Settings.GetOrCreate(display.Id);
        return (state.BlackLevel, state.WhiteLevel);
    }

    public void SetBrightness(string? target, int value, bool relative)
    {
        var displays = ResolveTarget(target);
        value = Math.Clamp(value, 0, 100);

        if (relative && displays.Count > 1)
        {
            var average = displays.Average(d => Settings.GetOrCreate(d.Id).Brightness);
            var delta = value - average;

            foreach (var display in displays)
            {
                var state = Settings.GetOrCreate(display.Id);
                state.Brightness = (int)Math.Clamp(Math.Round(state.Brightness + delta), 0, 100);
                ApplyDisplay(display);
            }
        }
        else
        {
            foreach (var display in displays)
            {
                Settings.GetOrCreate(display.Id).Brightness = value;
                ApplyDisplay(display);
            }
        }

        AfterChange();
    }

    public void SetContrast(string? target, int value)
    {
        value = Math.Clamp(value, 0, 100);
        foreach (var display in ResolveTarget(target))
        {
            Settings.GetOrCreate(display.Id).Contrast = value;
            ApplyDisplay(display);
        }

        AfterChange();
    }

    public void SetTemperature(string? target, int kelvin)
    {
        kelvin = Math.Clamp(kelvin, ColorMath.MinKelvin, ColorMath.MaxKelvin);
        foreach (var display in ResolveTarget(target))
        {
            Settings.GetOrCreate(display.Id).Temperature = kelvin;
            ApplyDisplay(display);
        }

        AfterChange();
    }

    public void SetBlackLevel(string? target, int level)
    {
        level = Math.Clamp(level, 0, 160);

        foreach (var display in ResolveTarget(target))
        {
            var state = Settings.GetOrCreate(display.Id);
            state.BlackLevel = level;

            if (state.WhiteLevel < level + 8)
            {
                state.WhiteLevel = Math.Min(255, level + 8);
            }

            ApplyDisplay(display);
        }

        AfterChange();
    }

    public void SetWhiteLevel(string? target, int level)
    {
        foreach (var display in ResolveTarget(target))
        {
            var state = Settings.GetOrCreate(display.Id);
            var minimum = state.BlackLevel + 8;
            state.WhiteLevel = Math.Clamp(level, minimum, 255);
            ApplyDisplay(display);
        }

        AfterChange();
    }

    public void StepBrightness(string? target, int delta)
    {
        var (brightness, _, _) = GetValues(target);
        SetBrightness(target, brightness + delta, relative: true);
    }

    public void Reset(string? target)
    {
        foreach (var display in ResolveTarget(target))
        {
            var state = Settings.GetOrCreate(display.Id);
            state.Brightness = DisplayState.DefaultBrightness;
            state.Contrast = DisplayState.DefaultContrast;
            state.Temperature = DisplayState.DefaultTemperature;
            state.BlackLevel = DisplayState.DefaultBlackLevel;
            state.WhiteLevel = DisplayState.DefaultWhiteLevel;
            ApplyDisplay(display);
        }

        AfterChange();
    }

    public void ApplyPreset(Preset preset, string? target = null, DisplayParameter? filter = null)
    {
        var displays = ResolveTarget(target ?? Settings.ActiveTarget);
        var parameters = filter ?? preset.Parameters;

        foreach (var display in displays)
        {
            var state = Settings.GetOrCreate(display.Id);
            var values = preset.Resolve(display.Id);
            ApplyValues(state, values, parameters);
            ApplyDisplay(display);
        }

        AfterChange();
    }

    public void ApplyValues(string? target, DisplayParameter parameters, int brightness, int contrast, int temperature, int blackLevel = 0, int whiteLevel = 255)
    {
        var values = new DisplayState
        {
            Brightness = brightness,
            Contrast = contrast,
            Temperature = temperature,
            BlackLevel = blackLevel == 0 ? DisplayState.DefaultBlackLevel : blackLevel,
            WhiteLevel = whiteLevel,
        };

        foreach (var display in ResolveTarget(target))
        {
            var state = Settings.GetOrCreate(display.Id);
            ApplyValues(state, values, parameters);
            ApplyDisplay(display);
        }

        AfterChange();
    }

    public void ApplySchedule(ScheduleEntry entry)
    {
        var preset = Settings.FindPreset(entry.PresetId);
        if (preset is not null)
        {
            ApplyPreset(preset, entry.TargetId, entry.Parameters);
            return;
        }

        ApplyValues(entry.TargetId, entry.Parameters, entry.Brightness, entry.Contrast, entry.Temperature);
    }

    public void ApplyRule(AppRule rule)
    {
        var preset = Settings.FindPreset(rule.PresetId);
        if (preset is not null)
        {
            ApplyPreset(preset, rule.TargetId, rule.Parameters);
            return;
        }

        ApplyValues(rule.TargetId, rule.Parameters, rule.Brightness, rule.Contrast, rule.Temperature);
    }

    /// <summary>Captura o estado atual de todos os monitores para criar um preset.</summary>
    public DisplayState CaptureCurrent(string? target = null)
    {
        var (brightness, contrast, temperature) = GetValues(target ?? AllTargets);
        var first = ResolveTarget(target ?? AllTargets).FirstOrDefault();
        var state = first is null ? DisplayState.Neutral() : Settings.GetOrCreate(first.Id);

        return new DisplayState
        {
            Brightness = brightness,
            Contrast = contrast,
            Temperature = temperature,
            BlackLevel = state.BlackLevel,
            WhiteLevel = state.WhiteLevel,
        };
    }

    /// <summary>Copia os valores atuais de todos os monitores (usado antes de aplicar uma regra).</summary>
    public Dictionary<string, DisplayState> Snapshot()
    {
        var snapshot = new Dictionary<string, DisplayState>();
        foreach (var display in _displays)
        {
            snapshot[display.Id] = Settings.GetOrCreate(display.Id).Clone();
        }

        return snapshot;
    }

    /// <summary>Volta aos valores capturados por <see cref="Snapshot"/>.</summary>
    public void Restore(IReadOnlyDictionary<string, DisplayState> snapshot)
    {
        foreach (var display in _displays)
        {
            if (!snapshot.TryGetValue(display.Id, out var state))
            {
                continue;
            }

            Settings.GetOrCreate(display.Id).CopyFrom(state);
            ApplyDisplay(display);
        }

        AfterChange();
    }

    public void CapturePerDisplay(Preset preset)
    {
        preset.PerDisplay.Clear();
        foreach (var display in _displays)
        {
            preset.PerDisplay[display.Id] = Settings.GetOrCreate(display.Id).Clone();
        }
    }

    public void SetUseHardware(string displayId, bool useHardware)
        => SetControl(displayId, useHardware ? DisplayControl.Auto : DisplayControl.Software);

    /// <summary>Define como o monitor e controlado e reaplica os valores.</summary>
    public void SetControl(string displayId, DisplayControl control)
    {
        var display = FindDisplay(displayId);
        if (display is null)
        {
            return;
        }

        Settings.GetOrCreate(displayId).Control = control;
        _hardwareFailed.Remove(displayId);
        _hardwareVerified.Remove(displayId);
        ApplyDisplay(display);
        AfterChange();
    }

    public void ApplyDisplay(DisplayInfo display)
    {
        var settings = Settings;
        var state = settings.GetOrCreate(display.Id);

        var hardwareApplied = false;
        var wantsHardware = state.Control != DisplayControl.Software
                            && display.SupportsDdc
                            && display.SupportsBrightness
                            && !_hardwareFailed.Contains(display.Id);

        if (wantsHardware)
        {
            hardwareApplied = DdcCi.TrySetBrightness(
                display.PhysicalHandle,
                state.Brightness,
                display.DdcBrightnessMin,
                display.DdcBrightnessMax);

            // Primeira escrita: confirma lendo de volta. Alguns drivers aceitam o comando
            // e nao alteram nada, o que deixava o slider sem efeito na tela.
            if (hardwareApplied && _hardwareVerified.Add(display.Id))
            {
                if (DdcCi.TryReadBrightness(display.PhysicalHandle, out var readback)
                    && Math.Abs(readback - state.Brightness) > 3)
                {
                    hardwareApplied = false;
                }
            }

            if (!hardwareApplied)
            {
                _hardwareFailed.Add(display.Id);
                display.SupportsBrightness = false;
                display.SupportsDdc = display.SupportsContrast;
            }
        }

        var hardwareContrast = false;
        if (hardwareApplied && state.Control != DisplayControl.Software && display.SupportsDdc && display.SupportsContrast)
        {
            hardwareContrast = DdcCi.TrySetContrast(
                display.PhysicalHandle,
                state.Contrast,
                display.DdcContrastMin,
                display.DdcContrastMax);
        }

        var useOverlay = settings.UseOverlayForSoftwareDimming && !hardwareApplied;
        var brightnessFactor = hardwareApplied || useOverlay ? 1.0 : state.Brightness / 100.0;
        var contrast = hardwareContrast ? ColorMath.NeutralContrast : state.Contrast;

        var ramp = ColorMath.BuildRamp(
            brightnessFactor,
            contrast,
            state.Temperature,
            state.BlackLevel,
            state.WhiteLevel);

        var gammaOk = GammaRamp.Apply(display.DeviceName, ramp);
        _overlay.SetDim(display, useOverlay ? state.Brightness : 100);

        var via = hardwareApplied ? "DDC/CI" : useOverlay ? "sobreposicao" : "gamma";
        display.LastApply = $"{via} {state.Brightness}% (gamma {(gammaOk ? "ok" : "falhou")}, preto {state.BlackLevel:X2}, branco {state.WhiteLevel:X2})";
    }

    /// <summary>Indica se o monitor esta sendo controlado por hardware neste momento.</summary>
    public bool IsHardwareActive(string displayId)
    {
        var display = FindDisplay(displayId);
        return display is not null
               && display.SupportsBrightness
               && !_hardwareFailed.Contains(displayId)
               && Settings.GetOrCreate(displayId).Control != DisplayControl.Software;
    }

    private static void ApplyValues(DisplayState state, DisplayState values, DisplayParameter parameters)
    {
        if (parameters.HasFlag(DisplayParameter.Brightness))
        {
            state.Brightness = Math.Clamp(values.Brightness, 0, 100);
        }

        if (parameters.HasFlag(DisplayParameter.Contrast))
        {
            state.Contrast = Math.Clamp(values.Contrast, 0, 100);
        }

        if (parameters.HasFlag(DisplayParameter.Temperature))
        {
            state.Temperature = Math.Clamp(values.Temperature, ColorMath.MinKelvin, ColorMath.MaxKelvin);
        }

        if (parameters.HasFlag(DisplayParameter.Levels))
        {
            state.BlackLevel = Math.Clamp(values.BlackLevel, 0, 128);
            state.WhiteLevel = Math.Clamp(values.WhiteLevel, state.BlackLevel + 8, 255);
        }
    }

    private void AfterChange()
    {
        _settingsService.SaveSoon();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        MonitorEnumerator.ReleaseHandles(_displays);
        _displays.Clear();
    }
}

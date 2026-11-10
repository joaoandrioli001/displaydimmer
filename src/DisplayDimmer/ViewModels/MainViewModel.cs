using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using DisplayDimmer.Models;
using DisplayDimmer.Services;

namespace DisplayDimmer.ViewModels;

/// <summary>Item do seletor: todos os monitores, um grupo ou um monitor.</summary>
public sealed class TargetOption
{
    public string Id { get; init; } = DisplayController.AllTargets;

    public string Name { get; init; } = "Todos os monitores";

    public string? Subtitle { get; init; }

    public bool IsAll => Id == DisplayController.AllTargets;

    public override string ToString() => Name;
}

/// <summary>Estado da janela principal.</summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly DisplayController _controller;
    private readonly SettingsService _settingsService;
    private bool _syncing;
    private TargetOption? _selectedTarget;
    private double _brightness = 100;
    private double _contrast = 50;
    private double _temperature = 6500;
    private double _blackLevel = DisplayState.DefaultBlackLevel;
    private double _whiteLevel = DisplayState.DefaultWhiteLevel;
    private bool _levelsExpanded;

    public MainViewModel(DisplayController controller, SettingsService settingsService)
    {
        _controller = controller;
        _settingsService = settingsService;
        Targets = new ObservableCollection<TargetOption>();

        _controller.DisplaysChanged += (_, _) => RefreshTargets();
        _controller.StateChanged += (_, _) => LoadValues();
        _settingsService.Changed += (_, _) => OnPropertyChanged(nameof(BlackLevelMinimum));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<TargetOption> Targets { get; }

    public TargetOption? SelectedTarget
    {
        get => _selectedTarget;
        set
        {
            if (!Set(ref _selectedTarget, value))
            {
                return;
            }

            if (value is not null)
            {
                _settingsService.Settings.ActiveTarget = value.Id;
            }

            LoadValues();
        }
    }

    public double Brightness
    {
        get => _brightness;
        set
        {
            if (!Set(ref _brightness, Math.Round(value)))
            {
                return;
            }

            OnPropertyChanged(nameof(BrightnessText));
            if (!_syncing)
            {
                _controller.SetBrightness(SelectedTarget?.Id, (int)_brightness, relative: true);
            }
        }
    }

    public double Contrast
    {
        get => _contrast;
        set
        {
            if (!Set(ref _contrast, Math.Round(value)))
            {
                return;
            }

            OnPropertyChanged(nameof(ContrastText));
            if (!_syncing)
            {
                _controller.SetContrast(SelectedTarget?.Id, (int)_contrast);
            }
        }
    }

    public double Temperature
    {
        get => _temperature;
        set
        {
            if (!Set(ref _temperature, Math.Round(value)))
            {
                return;
            }

            OnPropertyChanged(nameof(TemperatureText));
            if (!_syncing)
            {
                _controller.SetTemperature(SelectedTarget?.Id, (int)_temperature);
            }
        }
    }

    public double BlackLevel
    {
        get => _blackLevel;
        set
        {
            if (!Set(ref _blackLevel, Math.Round(value)))
            {
                return;
            }

            OnPropertyChanged(nameof(BlackLevelText));
            OnPropertyChanged(nameof(LevelsSummary));
            OnPropertyChanged(nameof(WhiteLevelMinimum));

            if (!_syncing)
            {
                _controller.SetBlackLevel(SelectedTarget?.Id, (int)_blackLevel);
                LoadLevels();
            }
        }
    }

    public double WhiteLevel
    {
        get => _whiteLevel;
        set
        {
            if (!Set(ref _whiteLevel, Math.Round(value)))
            {
                return;
            }

            OnPropertyChanged(nameof(WhiteLevelText));
            OnPropertyChanged(nameof(LevelsSummary));

            if (!_syncing)
            {
                _controller.SetWhiteLevel(SelectedTarget?.Id, (int)_whiteLevel);
                LoadLevels();
            }
        }
    }

    public bool LevelsExpanded
    {
        get => _levelsExpanded;
        set
        {
            if (Set(ref _levelsExpanded, value))
            {
                OnPropertyChanged(nameof(LevelsVisibility));
            }
        }
    }

    public System.Windows.Visibility LevelsVisibility
        => _levelsExpanded ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

    public int BlackLevelMinimum => 0;

    public int WhiteLevelMinimum => (int)_blackLevel + 8;

    public string BlackLevelText => ColorMath.Hex((int)_blackLevel);

    public string WhiteLevelText => ColorMath.Hex((int)_whiteLevel);

    public string LevelsSummary => $"Preto {BlackLevelText} · Branco {WhiteLevelText}";

    public string BrightnessText => $"{_brightness:0}%";

    public string ContrastText => ColorMath.ContrastLabel((int)_contrast);

    public string TemperatureText => ColorMath.TemperatureLabel((int)_temperature);

    public string ClampSummary => string.Empty;

    public bool HasMultipleDisplays => _controller.Displays.Count > 1;

    public void RefreshTargets()
    {
        var previous = SelectedTarget?.Id ?? _settingsService.Settings.ActiveTarget;
        Targets.Clear();
        Targets.Add(new TargetOption { Id = DisplayController.AllTargets, Name = "Todos os monitores" });

        foreach (var group in _settingsService.Settings.Groups)
        {
            Targets.Add(new TargetOption
            {
                Id = DisplayController.GroupPrefix + group.Id,
                Name = group.Name,
                Subtitle = "grupo",
            });
        }

        foreach (var display in _controller.Displays)
        {
            Targets.Add(new TargetOption
            {
                Id = display.Id,
                Name = display.Name,
                Subtitle = display.Detail,
            });
        }

        SelectedTarget = Targets.FirstOrDefault(t => t.Id == previous) ?? Targets[0];
        OnPropertyChanged(nameof(HasMultipleDisplays));
        LoadValues();
    }

    public void LoadValues()
    {
        var (brightness, contrast, temperature) = _controller.GetValues(SelectedTarget?.Id);

        _syncing = true;
        Brightness = brightness;
        Contrast = contrast;
        Temperature = temperature;
        _syncing = false;

        OnPropertyChanged(nameof(BrightnessText));
        OnPropertyChanged(nameof(ContrastText));
        OnPropertyChanged(nameof(TemperatureText));
        LoadLevels();
    }

    public void LoadLevels()
    {
        var (black, white) = _controller.GetLevels(SelectedTarget?.Id);

        _syncing = true;
        BlackLevel = black;
        WhiteLevel = white;
        _syncing = false;

        OnPropertyChanged(nameof(BlackLevelText));
        OnPropertyChanged(nameof(WhiteLevelText));
        OnPropertyChanged(nameof(LevelsSummary));
        OnPropertyChanged(nameof(WhiteLevelMinimum));
        OnPropertyChanged(nameof(BlackLevelMinimum));
    }

    public void ToggleLevels() => LevelsExpanded = !LevelsExpanded;

    public void ResetLevels() => _controller.Reset(SelectedTarget?.Id);

    public void ApplyPreset(Preset preset) => _controller.ApplyPreset(preset, SelectedTarget?.Id);

    public void StepBrightness(int delta) => _controller.StepBrightness(SelectedTarget?.Id, delta);

    public DisplayController Controller => _controller;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

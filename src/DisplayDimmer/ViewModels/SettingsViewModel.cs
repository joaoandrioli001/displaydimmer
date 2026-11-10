using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using DisplayDimmer.Models;
using DisplayDimmer.Services;

namespace DisplayDimmer.ViewModels;

/// <summary>Monitor exibido na aba de monitores.</summary>
public sealed class DisplayCard : INotifyPropertyChanged
{
    public required DisplayInfo Display { get; init; }

    public string Label => Display.Label;

    public string Detail => Display.Detail;

    public DisplayControl Control { get; set; } = DisplayControl.Auto;

    /// <summary>Indice usado pelo seletor: 0 automatico, 1 DDC/CI, 2 software.</summary>
    public int ControlIndex
    {
        get => (int)Control;
        set => Control = (DisplayControl)Math.Clamp(value, 0, 2);
    }

    public bool DdcAvailable => Display.SupportsDdc;

    public bool NoDdc => !Display.SupportsDdc;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Atualiza os textos apos o controlador mudar de modo ou cair para software.</summary>
    public void RefreshStatus()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Detail)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NoDdc)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DdcAvailable)));
    }
}

/// <summary>Atalho associado a um preset.</summary>
public sealed class PresetHotkeyCard : INotifyPropertyChanged
{
    private string _hotkey = string.Empty;

    public required Preset Preset { get; init; }

    public string Name => Preset.Name;

    public string Hotkey
    {
        get => _hotkey;
        set
        {
            if (_hotkey == value)
            {
                return;
            }

            _hotkey = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Hotkey)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>Dados das abas de configuracao.</summary>
public sealed class SettingsViewModel
{
    private readonly DisplayController _controller;
    private readonly SettingsService _settingsService;

    public SettingsViewModel(DisplayController controller, SettingsService settingsService)
    {
        _controller = controller;
        _settingsService = settingsService;
    }

    public ObservableCollection<Preset> Presets { get; } = new();

    public ObservableCollection<ScheduleEntry> Schedules { get; } = new();

    public ObservableCollection<AppRule> Rules { get; } = new();

    public ObservableCollection<DisplayCard> Displays { get; } = new();

    public ObservableCollection<MonitorGroup> Groups { get; } = new();

    public ObservableCollection<PresetHotkeyCard> PresetHotkeys { get; } = new();

    public AppSettings Settings => _settingsService.Settings;

    public void Reload()
    {
        Presets.Clear();
        foreach (var preset in Settings.Presets)
        {
            Presets.Add(preset);
        }

        Schedules.Clear();
        foreach (var schedule in Settings.Schedules)
        {
            schedule.PresetName = Settings.FindPreset(schedule.PresetId)?.Name ?? "valores proprios";
            Schedules.Add(schedule);
        }

        Rules.Clear();
        foreach (var rule in Settings.Rules)
        {
            rule.PresetName = Settings.FindPreset(rule.PresetId)?.Name ?? "valores proprios";
            Rules.Add(rule);
        }

        Displays.Clear();
        foreach (var display in _controller.Displays)
        {
            Displays.Add(new DisplayCard
            {
                Display = display,
                Control = Settings.GetOrCreate(display.Id).Control,
            });
        }

        Groups.Clear();
        foreach (var group in Settings.Groups)
        {
            var names = group.DisplayIds
                .Select(id => _controller.Displays.FirstOrDefault(d => d.Id == id)?.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name));

            group.DisplaySummary = string.Join(" · ", names);
            Groups.Add(group);
        }

        PresetHotkeys.Clear();
        foreach (var preset in Settings.Presets)
        {
            Settings.Hotkeys.Presets.TryGetValue(preset.Id, out var hotkey);
            PresetHotkeys.Add(new PresetHotkeyCard { Preset = preset, Hotkey = hotkey ?? string.Empty });
        }
    }

    public void Save() => _settingsService.SaveSoon();
}

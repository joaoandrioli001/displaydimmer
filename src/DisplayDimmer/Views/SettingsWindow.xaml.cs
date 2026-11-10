using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DisplayDimmer.Models;
using DisplayDimmer.Services;
using DisplayDimmer.ViewModels;

namespace DisplayDimmer.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;
    private readonly DisplayController _controller;
    private readonly SettingsService _settingsService;
    private readonly ThemeService _themeService;
    private readonly Func<IReadOnlyList<string>>? _hotkeysChanged;
    private readonly Action? _automationsChanged;
    private bool _loading;
    private bool _suppressControlEvents;

    public SettingsWindow(
        SettingsViewModel viewModel,
        DisplayController controller,
        SettingsService settingsService,
        ThemeService themeService,
        Func<IReadOnlyList<string>>? hotkeysChanged,
        Action? automationsChanged)
    {
        _viewModel = viewModel;
        _controller = controller;
        _settingsService = settingsService;
        _themeService = themeService;
        _hotkeysChanged = hotkeysChanged;
        _automationsChanged = automationsChanged;

        InitializeComponent();

        PresetsList.ItemsSource = _viewModel.Presets;
        SchedulesList.ItemsSource = _viewModel.Schedules;
        RulesList.ItemsSource = _viewModel.Rules;
        DisplaysList.ItemsSource = _viewModel.Displays;
        GroupsList.ItemsSource = _viewModel.Groups;
        PresetHotkeysList.ItemsSource = _viewModel.PresetHotkeys;

        SchedulePresetCombo.ItemsSource = _viewModel.Presets;
        SchedulePresetCombo.SelectedValuePath = nameof(Preset.Id);
        RulePresetCombo.ItemsSource = _viewModel.Presets;
        RulePresetCombo.SelectedValuePath = nameof(Preset.Id);

        Loaded += (_, _) =>
        {
            _suppressControlEvents = true;
            _viewModel.Reload();
            _suppressControlEvents = false;
            LoadFromSettings();
        };
    }

    /// <summary>Seleciona uma aba pelo identificador (usado no diagnostico).</summary>
    public void SelectTab(string tag)
    {
        foreach (var button in new[] { NavGeral, NavPresets, NavAutomacoes, NavMonitores, NavAtalhos })
        {
            if (string.Equals(button.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
            {
                button.IsChecked = true;
                UpdateLayout();
                return;
            }
        }
    }

    public static readonly string[] Tabs = { "geral", "presets", "automacoes", "monitores", "atalhos" };

    private void LoadFromSettings()
    {
        _loading = true;
        var settings = _settingsService.Settings;

        SelectByTag(ThemeCombo, settings.Theme);
        SelectByTag(TrayStepCombo, settings.TrayStepPercent.ToString());

        StartWithWindowsCheck.IsChecked = settings.StartWithWindows;
        StartMinimizedCheck.IsChecked = settings.StartMinimized;
        OverlayCheck.IsChecked = settings.UseOverlayForSoftwareDimming;
        NativeKeysCheck.IsChecked = settings.EnableNativeBrightnessKeys;
        AutomationsCheck.IsChecked = settings.AutomationsEnabled;

        HotkeyUpBox.Text = settings.Hotkeys.BrightnessUp;
        HotkeyDownBox.Text = settings.Hotkeys.BrightnessDown;
        HotkeyShowBox.Text = settings.Hotkeys.ShowWindow;
        HotkeyAutomationBox.Text = settings.Hotkeys.ToggleAutomations;

        if (SchedulePresetCombo.SelectedIndex < 0 && _viewModel.Presets.Count > 0)
        {
            SchedulePresetCombo.SelectedIndex = 0;
        }

        if (RulePresetCombo.SelectedIndex < 0 && _viewModel.Presets.Count > 0)
        {
            RulePresetCombo.SelectedIndex = 0;
        }

        RebuildGroupPanel();
        _loading = false;
    }

    private static void SelectByTag(ComboBox combo, string tag)
    {
        foreach (var item in combo.Items)
        {
            if (item is ComboBoxItem { Tag: string value } && value.Equals(tag, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }
    }

    private static string? SelectedTag(ComboBox combo)
        => combo.SelectedItem is ComboBoxItem { Tag: string value } ? value : null;

    private void RebuildGroupPanel()
    {
        GroupDisplaysPanel.Children.Clear();
        foreach (var display in _controller.Displays)
        {
            var check = new CheckBox
            {
                Content = display.Name,
                Tag = display.Id,
                Margin = new Thickness(0, 0, 16, 6),
                Style = (Style)FindResource("AppCheckBox"),
            };

            GroupDisplaysPanel.Children.Add(check);
        }
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (PanelGeral is null)
        {
            return;
        }

        var tag = (sender as FrameworkElement)?.Tag as string ?? "geral";
        PanelGeral.Visibility = tag == "geral" ? Visibility.Visible : Visibility.Collapsed;
        PanelPresets.Visibility = tag == "presets" ? Visibility.Visible : Visibility.Collapsed;
        PanelAutomacoes.Visibility = tag == "automacoes" ? Visibility.Visible : Visibility.Collapsed;
        PanelMonitores.Visibility = tag == "monitores" ? Visibility.Visible : Visibility.Collapsed;
        PanelAtalhos.Visibility = tag == "atalhos" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        _settingsService.Flush();
        Close();
    }

    private void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        var tag = SelectedTag(ThemeCombo) ?? "dark";
        _settingsService.Settings.Theme = tag;
        _themeService.Apply(tag);
        _settingsService.SaveSoon();
    }

    private void StartWithWindows_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        var enabled = StartWithWindowsCheck.IsChecked == true;
        var ok = StartupService.SetEnabled(enabled);
        _settingsService.Settings.StartWithWindows = enabled && ok;
        _settingsService.SaveSoon();

        if (!ok && enabled)
        {
            MessageBox.Show(this, "Nao foi possivel gravar a entrada de inicializacao.", "Display Dimmer",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void General_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        var settings = _settingsService.Settings;
        settings.StartMinimized = StartMinimizedCheck.IsChecked == true;
        settings.UseOverlayForSoftwareDimming = OverlayCheck.IsChecked == true;
        settings.EnableNativeBrightnessKeys = NativeKeysCheck.IsChecked == true;

        _controller.ReapplyAll();
        _settingsService.SaveSoon();
    }

    private void TrayStep_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        if (int.TryParse(SelectedTag(TrayStepCombo), out var step))
        {
            _settingsService.Settings.TrayStepPercent = step;
            _settingsService.SaveSoon();
        }
    }

    private void SavePreset_Click(object sender, RoutedEventArgs e)
    {
        var name = PresetNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            name = $"Preset {_settingsService.Settings.Presets.Count + 1}";
        }

        var settings = _settingsService.Settings;
        var preset = settings.Presets.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (preset is null)
        {
            preset = new Preset { Name = name };
            settings.Presets.Add(preset);
        }

        var current = _controller.CaptureCurrent(DisplayController.AllTargets);
        preset.Brightness = current.Brightness;
        preset.Contrast = current.Contrast;
        preset.Temperature = current.Temperature;
        preset.Parameters = DisplayParameter.All;
        _controller.CapturePerDisplay(preset);

        PresetNameBox.Clear();
        _settingsService.SaveSoon();
        ReloadView();
    }

    private void ApplyPreset_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is Preset preset)
        {
            _controller.ApplyPreset(preset);
        }
    }

    private void DeletePreset_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not Preset preset)
        {
            return;
        }

        var settings = _settingsService.Settings;
        settings.Presets.Remove(preset);
        settings.Hotkeys.Presets.Remove(preset.Id);

        foreach (var schedule in settings.Schedules.Where(s => s.PresetId == preset.Id))
        {
            schedule.PresetId = string.Empty;
        }

        foreach (var rule in settings.Rules.Where(r => r.PresetId == preset.Id))
        {
            rule.PresetId = string.Empty;
        }

        _settingsService.SaveSoon();
        ReloadView();
        _hotkeysChanged?.Invoke();
    }

    private void Automations_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _settingsService.Settings.AutomationsEnabled = AutomationsCheck.IsChecked == true;
        _settingsService.SaveSoon();
        _automationsChanged?.Invoke();
    }

    private DisplayParameter ReadParameters(CheckBox brightness, CheckBox contrast, CheckBox temperature)
    {
        var parameters = DisplayParameter.None;

        if (brightness.IsChecked == true)
        {
            parameters |= DisplayParameter.Brightness;
        }

        if (contrast.IsChecked == true)
        {
            parameters |= DisplayParameter.Contrast;
        }

        if (temperature.IsChecked == true)
        {
            parameters |= DisplayParameter.Temperature;
        }

        return parameters == DisplayParameter.None ? DisplayParameter.All : parameters;
    }

    private void AddSchedule_Click(object sender, RoutedEventArgs e)
    {
        var time = ScheduleTimeBox.Text.Trim();
        if (!TimeSpan.TryParse(time, out var parsed) || parsed.TotalHours >= 24)
        {
            MessageBox.Show(this, "Informe o horario no formato HH:mm, por exemplo 21:30.", "Display Dimmer",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var name = ScheduleNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            name = $"Agendamento {_settingsService.Settings.Schedules.Count + 1}";
        }

        var preset = SchedulePresetCombo.SelectedItem as Preset;

        _settingsService.Settings.Schedules.Add(new ScheduleEntry
        {
            Name = name,
            Time = parsed.ToString(@"hh\:mm"),
            PresetId = preset?.Id ?? string.Empty,
            Parameters = ReadParameters(SchedParamBrightness, SchedParamContrast, SchedParamTemperature),
            Brightness = preset?.Brightness ?? DisplayState.DefaultBrightness,
            Contrast = preset?.Contrast ?? DisplayState.DefaultContrast,
            Temperature = preset?.Temperature ?? DisplayState.DefaultTemperature,
        });

        ScheduleNameBox.Clear();
        _settingsService.SaveSoon();
        ReloadView();
    }

    private void ToggleSchedule_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ScheduleEntry entry)
        {
            return;
        }

        entry.Enabled = !entry.Enabled;
        _settingsService.SaveSoon();
        ReloadView();
    }

    private void DeleteSchedule_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ScheduleEntry entry)
        {
            return;
        }

        _settingsService.Settings.Schedules.Remove(entry);
        _settingsService.SaveSoon();
        ReloadView();
    }

    private void AddRule_Click(object sender, RoutedEventArgs e)
    {
        var process = RuleProcessBox.Text.Trim();
        var fullscreen = RuleFullscreenCheck.IsChecked == true;

        if (string.IsNullOrWhiteSpace(process) && !fullscreen)
        {
            MessageBox.Show(this, "Informe o nome do processo ou marque \"Somente em tela cheia\".",
                "Display Dimmer", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var name = RuleNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            name = string.IsNullOrWhiteSpace(process) ? "Tela cheia" : process;
        }

        var preset = RulePresetCombo.SelectedItem as Preset;

        _settingsService.Settings.Rules.Add(new AppRule
        {
            Name = name,
            ProcessName = process,
            FullscreenOnly = fullscreen,
            RestoreOnExit = RuleRestoreCheck.IsChecked == true,
            PresetId = preset?.Id ?? string.Empty,
            Parameters = DisplayParameter.All,
            Brightness = preset?.Brightness ?? DisplayState.DefaultBrightness,
            Contrast = preset?.Contrast ?? DisplayState.DefaultContrast,
            Temperature = preset?.Temperature ?? DisplayState.DefaultTemperature,
        });

        RuleNameBox.Clear();
        RuleProcessBox.Clear();
        RuleFullscreenCheck.IsChecked = false;
        _settingsService.SaveSoon();
        ReloadView();
        _automationsChanged?.Invoke();
    }

    private void ToggleRule_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not AppRule rule)
        {
            return;
        }

        rule.Enabled = !rule.Enabled;
        _settingsService.SaveSoon();
        ReloadView();
    }

    private void DeleteRule_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not AppRule rule)
        {
            return;
        }

        _settingsService.Settings.Rules.Remove(rule);
        _settingsService.SaveSoon();
        ReloadView();
    }

    private void DisplayControl_Changed(object sender, SelectionChangedEventArgs e)
    {
        // Ignora o evento disparado enquanto a lista e montada ou recarregada.
        if (_loading || _suppressControlEvents || (sender as FrameworkElement)?.DataContext is not DisplayCard card)
        {
            return;
        }

        var combo = sender as ComboBox;
        var control = (DisplayControl)Math.Clamp(combo?.SelectedIndex ?? 0, 0, 2);
        _controller.SetControl(card.Display.Id, control);
        _settingsService.SaveSoon();
        card.RefreshStatus();
    }

    private void ReloadView()
    {
        _suppressControlEvents = true;
        _viewModel.Reload();
        _suppressControlEvents = false;
    }

    private void AddGroup_Click(object sender, RoutedEventArgs e)
    {
        var name = GroupNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show(this, "Informe um nome para o grupo.", "Display Dimmer",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var ids = GroupDisplaysPanel.Children
            .OfType<CheckBox>()
            .Where(check => check.IsChecked == true)
            .Select(check => check.Tag as string)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToList();

        if (ids.Count == 0)
        {
            MessageBox.Show(this, "Selecione ao menos um monitor para o grupo.", "Display Dimmer",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _settingsService.Settings.Groups.Add(new MonitorGroup { Name = name, DisplayIds = ids });
        GroupNameBox.Clear();

        foreach (var check in GroupDisplaysPanel.Children.OfType<CheckBox>())
        {
            check.IsChecked = false;
        }

        _settingsService.SaveSoon();
        ReloadView();
    }

    private void DeleteGroup_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not MonitorGroup group)
        {
            return;
        }

        _settingsService.Settings.Groups.Remove(group);
        _settingsService.SaveSoon();
        ReloadView();
    }

    private void SaveHotkeys_Click(object sender, RoutedEventArgs e)
    {
        var settings = _settingsService.Settings;
        settings.Hotkeys.BrightnessUp = HotkeyUpBox.Text.Trim();
        settings.Hotkeys.BrightnessDown = HotkeyDownBox.Text.Trim();
        settings.Hotkeys.ShowWindow = HotkeyShowBox.Text.Trim();
        settings.Hotkeys.ToggleAutomations = HotkeyAutomationBox.Text.Trim();

        settings.Hotkeys.Presets.Clear();
        foreach (var card in _viewModel.PresetHotkeys)
        {
            var hotkey = card.Hotkey.Trim();
            if (hotkey.Length > 0)
            {
                settings.Hotkeys.Presets[card.Preset.Id] = hotkey;
            }
        }

        _settingsService.SaveSoon();
        var failures = _hotkeysChanged?.Invoke() ?? Array.Empty<string>();

        HotkeyStatusText.Text = failures.Count == 0
            ? "Atalhos salvos e aplicados."
            : "Nao foi possivel registrar: " + string.Join(", ", failures) + ". Verifique se outro programa ja usa a combinacao.";
    }
}

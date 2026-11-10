using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DisplayDimmer.Cli;
using DisplayDimmer.Interop;
using DisplayDimmer.Models;
using DisplayDimmer.Services;
using DisplayDimmer.ViewModels;
using DisplayDimmer.Views;

namespace DisplayDimmer;

public partial class App : Application
{
    private const string MutexName = @"Local\DisplayDimmer.SingleInstance";
    private const char ArgumentSeparator = '\u001F';
    private const int SM_CMONITORS = 80;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    private Mutex? _mutex;
    private SettingsService? _settingsService;
    private OverlayService? _overlay;
    private DisplayController? _controller;
    private ThemeService? _themeService;
    private TrayService? _tray;
    private HotkeyService? _hotkeyService;
    private BrightnessKeyService? _brightnessKeys;
    private ScheduleService? _schedule;
    private AppRuleService? _rules;
    private IpcService? _ipc;
    private DispatcherTimer? _displayWatch;
    private DispatcherTimer? _themeWatch;
    private MainWindow? _mainWindow;
    private MainViewModel? _mainViewModel;
    private SettingsWindow? _settingsWindow;
    private string _displaySignature = string.Empty;
    private string? _shotMain;
    private string? _shotSettings;
    private string? _shotTabs;
    private bool _shotLevels;
    private bool _forceShow;
    private bool _exiting;

    public static App Instance => (App)Current;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var args = e.Args;
        _shotMain = ExtractOption(ref args, "--shot");
        _shotSettings = ExtractOption(ref args, "--shot-settings");
        _shotTabs = ExtractOption(ref args, "--shot-tabs");
        _shotLevels = RemoveFlag(ref args, "--shot-levels");
        _forceShow = RemoveFlag(ref args, "--show")
                     || RemoveFlag(ref args, "--ui");

        var hasCommands = CliProcessor.HasCommands(args);
        var wantsWindow = _forceShow || _shotMain is not null || _shotSettings is not null || _shotTabs is not null;

        _mutex = new Mutex(initiallyOwned: true, MutexName, out var isFirstInstance);

        if (!isFirstInstance)
        {
            ForwardToRunningInstance(args, hasCommands);
            _mutex.Dispose();
            _mutex = null;
            Shutdown(0);
            return;
        }

        DispatcherUnhandledException += (_, args2) =>
        {
            args2.Handled = true;
            ConsoleBridge.WriteLine($"Erro inesperado: {args2.Exception.Message}");
        };

        _settingsService = new SettingsService();
        _settingsService.Load();

        _themeService = new ThemeService();
        _themeService.Apply(_settingsService.Settings.Theme);

        _overlay = new OverlayService { Enabled = false };
        _controller = new DisplayController(_settingsService, _overlay);

        if (hasCommands && !wantsWindow)
        {
            // Consultas nao devem mexer na tela nem no brilho.
            _controller.ApplyOnRefresh = !CliProcessor.IsReadOnly(args);
        }

        _controller.Refresh();

        if (hasCommands && !wantsWindow)
        {
            RunHeadless(args);
            return;
        }

        BuildUi(
            minimized: args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase)),
            forceShow: _forceShow);
    }

    private void BuildUi(bool minimized, bool forceShow)
    {
        _overlay!.Enabled = true;

        _mainViewModel = new MainViewModel(_controller!, _settingsService!);
        _mainViewModel.RefreshTargets();

        _mainWindow = new MainWindow(_mainViewModel);
        _mainWindow.SettingsRequested += (_, _) => ShowSettings();

        _tray = new TrayService { MenuFactory = BuildTrayMenu };
        _tray.ShowRequested += ShowMainWindow;
        _tray.Wheel += OnTrayWheel;
        _tray.Start();

        _hotkeyService = new HotkeyService();
        _hotkeyService.Start();
        RegisterHotkeys();

        _brightnessKeys = new BrightnessKeyService();
        _brightnessKeys.BrightnessUp += () => StepBrightness(+_settingsService!.Settings.TrayStepPercent);
        _brightnessKeys.BrightnessDown += () => StepBrightness(-_settingsService!.Settings.TrayStepPercent);

        if (_settingsService!.Settings.EnableNativeBrightnessKeys)
        {
            _brightnessKeys.Start();
        }

        _schedule = new ScheduleService(_controller!, _settingsService);
        _schedule.Start();
        _schedule.CatchUp();

        _rules = new AppRuleService(_controller!, _settingsService);
        _rules.Start();

        _ipc = new IpcService();
        _ipc.Start(HandleIpcCommand);

        StartWatchers();

        UpdateTrayTooltip();

        // Na primeira execucao a janela sempre aparece; depois vale a preferencia do usuario.
        var showWindow = forceShow || _shotMain is not null || _shotSettings is not null || _shotTabs is not null
                         || (!minimized && (!_settingsService.Settings.StartMinimized || _settingsService.IsFirstRun));
        if (showWindow)
        {
            ShowMainWindow();
        }

        if (_shotLevels && _mainViewModel is not null)
        {
            _mainViewModel.LevelsExpanded = true;
        }

        if (_shotMain is not null || _shotSettings is not null || _shotTabs is not null)
        {
            StartSnapshot();
        }
    }

    /// <summary>Diagnostico: salva PNGs das janelas e encerra.</summary>
    private void StartSnapshot()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();

            if (_shotSettings is not null || _shotTabs is not null)
            {
                ShowSettings();
            }

            var second = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            second.Tick += (_, _) =>
            {
                second.Stop();

                if (_shotMain is not null && _mainWindow is not null)
                {
                    WindowSnapshot.Save(_mainWindow, _shotMain);
                }

                if (_shotSettings is not null && _settingsWindow is not null)
                {
                    WindowSnapshot.Save(_settingsWindow, _shotSettings);
                }

                if (_shotTabs is not null && _settingsWindow is not null)
                {
                    foreach (var tab in SettingsWindow.Tabs)
                    {
                        _settingsWindow.SelectTab(tab);
                        WindowSnapshot.Save(_settingsWindow, System.IO.Path.Combine(_shotTabs, $"tab-{tab}.png"));
                    }
                }

                ExitApp();
            };
            second.Start();
        };
        timer.Start();
    }

    private static bool RemoveFlag(ref string[] args, string name)
    {
        var list = args.ToList();
        var removed = list.RemoveAll(a => a.Equals(name, StringComparison.OrdinalIgnoreCase)) > 0;
        args = list.ToArray();
        return removed;
    }

    private static string? ExtractOption(ref string[] args, string name)
    {
        var list = args.ToList();
        var index = list.FindIndex(a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return null;
        }

        string? value = index + 1 < list.Count && !list[index + 1].StartsWith("--", StringComparison.Ordinal)
            ? list[index + 1]
            : null;

        list.RemoveRange(index, value is null ? 1 : 2);
        args = list.ToArray();
        return value;
    }

    private void StartWatchers()
    {
        _displaySignature = CurrentDisplaySignature();

        _displayWatch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        _displayWatch.Tick += (_, _) =>
        {
            var signature = CurrentDisplaySignature();
            if (signature == _displaySignature)
            {
                return;
            }

            _displaySignature = signature;
            _controller!.Refresh();
            _mainViewModel!.RefreshTargets();
            UpdateTrayTooltip();
        };
        _displayWatch.Start();

        _themeWatch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _themeWatch.Tick += (_, _) => _themeService!.RefreshSystemTheme();
        _themeWatch.Start();
    }

    private static string CurrentDisplaySignature()
    {
        var count = NativeMethods.GetSystemMetrics(SM_CMONITORS);
        var width = NativeMethods.GetSystemMetrics(SM_CXVIRTUALSCREEN);
        var height = NativeMethods.GetSystemMetrics(SM_CYVIRTUALSCREEN);
        return $"{count}:{width}x{height}";
    }

    private void RunHeadless(string[] args)
    {
        ConsoleBridge.Attach();

        var context = new CliContext
        {
            Controller = _controller!,
            Settings = _settingsService!,
            ShowWindow = null,
            RequestExit = null,
        };

        ConsoleBridge.WriteLine(CliProcessor.Execute(args, context));
        _settingsService!.Flush();
        _controller!.Dispose();
        Shutdown(0);
    }

    private void ForwardToRunningInstance(string[] args, bool hasCommands)
    {
        var payload = args.Length == 0 ? "show" : string.Join(ArgumentSeparator, args);
        var response = IpcService.Send(payload, 1500) ?? IpcService.Send(payload, 1500);

        if (hasCommands)
        {
            ConsoleBridge.Attach();
            ConsoleBridge.WriteLine(response ?? "O Display Dimmer nao respondeu. Tente novamente.");
        }
    }

    private string HandleIpcCommand(string command)
    {
        if (command == "ping")
        {
            return "pong";
        }

        return Dispatcher.Invoke(() =>
        {
            var args = command.Split(ArgumentSeparator, StringSplitOptions.RemoveEmptyEntries);
            var context = new CliContext
            {
                Controller = _controller!,
                Settings = _settingsService!,
                ShowWindow = ShowMainWindow,
                RequestExit = ExitApp,
                SetTheme = ApplyTheme,
            };

            var result = CliProcessor.Execute(args, context);
            UpdateTrayTooltip();
            _mainViewModel?.LoadValues();
            return result;
        });
    }

    private bool ApplyTheme(string theme)
    {
        _themeService!.Apply(theme);
        return true;
    }

    private IReadOnlyList<string> RegisterHotkeys()
    {
        if (_hotkeyService is null)
        {
            return Array.Empty<string>();
        }

        _hotkeyService.UnregisterAll();

        var settings = _settingsService!.Settings;
        _hotkeyService.Register(settings.Hotkeys.BrightnessUp, () => StepBrightness(settings.TrayStepPercent));
        _hotkeyService.Register(settings.Hotkeys.BrightnessDown, () => StepBrightness(-settings.TrayStepPercent));
        _hotkeyService.Register(settings.Hotkeys.ShowWindow, ShowMainWindow);
        _hotkeyService.Register(settings.Hotkeys.ToggleAutomations, ToggleAutomations);

        foreach (var preset in settings.Presets)
        {
            if (settings.Hotkeys.Presets.TryGetValue(preset.Id, out var gesture) && !string.IsNullOrWhiteSpace(gesture))
            {
                var captured = preset;
                _hotkeyService.Register(gesture, () => ApplyPreset(captured));
            }
        }

        Log($"atalhos registrados; falhas: {_hotkeyService.Failures.Count}");
        return _hotkeyService.Failures;
    }

    private static void Log(string message)
    {
        if (Environment.GetEnvironmentVariable("DISPLAYDIMMER_LOG") is null)
        {
            return;
        }

        try
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "displaydimmer-ipc.log"),
                $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    private void StepBrightness(int delta)
    {
        var target = _settingsService!.Settings.ActiveTarget;
        _controller!.StepBrightness(target, delta);
        UpdateTrayTooltip();
        _mainViewModel?.LoadValues();
    }

    private void ApplyPreset(Preset preset)
    {
        _controller!.ApplyPreset(preset);
        UpdateTrayTooltip();
        _mainViewModel?.LoadValues();
    }

    private void ToggleAutomations()
    {
        var settings = _settingsService!.Settings;
        settings.AutomationsEnabled = !settings.AutomationsEnabled;
        _settingsService.SaveSoon();

        if (settings.AutomationsEnabled)
        {
            _rules?.Reevaluate();
        }

        UpdateTrayTooltip();
        _tray?.ShowBalloon("Display Dimmer", settings.AutomationsEnabled ? "Automacoes ativas" : "Automacoes pausadas");
    }

    private void OnTrayWheel(int delta)
    {
        var step = _settingsService!.Settings.TrayStepPercent;
        StepBrightness(delta > 0 ? step : -step);
    }

    private void UpdateTrayTooltip()
    {
        if (_tray is null || _controller is null)
        {
            return;
        }

        var (brightness, _, temperature) = _controller.GetValues(_settingsService!.Settings.ActiveTarget);
        var automations = _settingsService.Settings.AutomationsEnabled ? string.Empty : " · automacoes pausadas";
        _tray.SetTooltip($"Display Dimmer · {brightness}% · {temperature} K{automations}");
    }

    private void ShowMainWindow()
    {
        if (_mainWindow is null)
        {
            return;
        }

        _mainViewModel?.RefreshTargets();
        _mainWindow.ShowAndFocus();
    }

    private void ShowSettings()
    {
        if (_settingsWindow is { IsLoaded: true })
        {
            _settingsWindow.Activate();
            return;
        }

        var viewModel = new SettingsViewModel(_controller!, _settingsService!);
        _settingsWindow = new SettingsWindow(
            viewModel,
            _controller!,
            _settingsService!,
            _themeService!,
            RegisterHotkeys,
            () =>
            {
                _rules?.Reevaluate();
                _mainViewModel?.LoadValues();
            });

        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private ContextMenu BuildTrayMenu()
    {
        var menu = new ContextMenu();
        var settings = _settingsService!.Settings;

        menu.Items.Add(CreateMenuItem("Abrir Display Dimmer", ShowMainWindow));
        menu.Items.Add(new Separator());

        foreach (var preset in settings.Presets)
        {
            var captured = preset;
            menu.Items.Add(CreateMenuItem($"{preset.Name}  ·  {preset.Summary}", () => ApplyPreset(captured)));
        }

        if (settings.Presets.Count > 0)
        {
            menu.Items.Add(new Separator());
        }

        var automations = CreateMenuItem("Automacoes", ToggleAutomations);
        automations.IsCheckable = true;
        automations.IsChecked = settings.AutomationsEnabled;
        menu.Items.Add(automations);

        var startup = CreateMenuItem("Iniciar com o Windows", () =>
        {
            var enabled = !StartupService.IsEnabled();
            StartupService.SetEnabled(enabled);
            settings.StartWithWindows = enabled;
            _settingsService.SaveSoon();
        });
        startup.IsCheckable = true;
        startup.IsChecked = StartupService.IsEnabled();
        menu.Items.Add(startup);

        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem("Configuracoes...", ShowSettings));
        menu.Items.Add(CreateMenuItem("Sair", ExitApp));

        return menu;
    }

    private static MenuItem CreateMenuItem(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private void ExitApp()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;

        try
        {
            _settingsService?.Flush();
            _displayWatch?.Stop();
            _themeWatch?.Stop();
            _ipc?.Dispose();
            _schedule?.Dispose();
            _rules?.Dispose();
            _brightnessKeys?.Dispose();
            _hotkeyService?.Dispose();
            _tray?.Dispose();

            // Devolve os monitores ao estado neutro para nao deixar a tela escurecida.
            if (_controller is not null)
            {
                foreach (var display in _controller.Displays)
                {
                    GammaRamp.Reset(display.DeviceName);
                }
            }

            _overlay?.Dispose();
            _controller?.Dispose();
            _settingsWindow?.Close();
            _mainWindow?.CloseForReal();
        }
        catch
        {
        }

        Shutdown(0);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        _mutex = null;
        base.OnExit(e);
    }
}

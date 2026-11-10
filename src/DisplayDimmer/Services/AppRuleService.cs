using System.Diagnostics;
using System.Windows.Threading;
using DisplayDimmer.Interop;
using DisplayDimmer.Models;

namespace DisplayDimmer.Services;

/// <summary>Aplica presets quando um aplicativo esta em primeiro plano ou em tela cheia.</summary>
public sealed class AppRuleService : IDisposable
{
    private const int WS_CAPTION = 0x00C00000;

    private readonly DisplayController _controller;
    private readonly SettingsService _settings;
    private readonly DispatcherTimer _timer;
    private string? _activeRuleId;
    private Dictionary<string, DisplayState>? _snapshot;

    public AppRuleService(DisplayController controller, SettingsService settings)
    {
        _controller = controller;
        _settings = settings;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Evaluate();
    }

    public event EventHandler<AppRule>? Applied;

    public string? ActiveRuleId => _activeRuleId;

    public void Start() => _timer.Start();

    public void Stop()
    {
        _timer.Stop();
        Deactivate();
    }

    /// <summary>Forca a reavaliacao imediata (apos editar regras).</summary>
    public void Reevaluate()
    {
        _activeRuleId = null;
        _snapshot = null;
        Evaluate();
    }

    private void Evaluate()
    {
        if (!_settings.Settings.AutomationsEnabled)
        {
            Deactivate();
            return;
        }

        var (processName, fullscreen) = GetForegroundInfo();
        var rule = MatchRule(processName, fullscreen);

        if (rule is null)
        {
            Deactivate();
            return;
        }

        if (rule.Id == _activeRuleId)
        {
            return;
        }

        if (_activeRuleId is not null)
        {
            // Troca direta de regra: restaura antes de aplicar a nova.
            Deactivate();
        }

        _snapshot ??= _controller.Snapshot();
        _activeRuleId = rule.Id;
        _controller.ApplyRule(rule);
        Applied?.Invoke(this, rule);
    }

    private AppRule? MatchRule(string? processName, bool fullscreen)
    {
        var rules = _settings.Settings.Rules.Where(r => r.Enabled).ToList();

        if (fullscreen)
        {
            var fullscreenRule = rules.FirstOrDefault(r => r.FullscreenOnly && string.IsNullOrWhiteSpace(r.ProcessName));
            if (fullscreenRule is not null)
            {
                return fullscreenRule;
            }
        }

        if (string.IsNullOrWhiteSpace(processName))
        {
            return null;
        }

        return rules.FirstOrDefault(r =>
            !string.IsNullOrWhiteSpace(r.ProcessName)
            && string.Equals(Clean(r.ProcessName), processName, StringComparison.OrdinalIgnoreCase)
            && (!r.FullscreenOnly || fullscreen));
    }

    private static string Clean(string processName)
    {
        var name = processName.Trim();
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }

    private void Deactivate()
    {
        if (_activeRuleId is null)
        {
            return;
        }

        var rule = _settings.Settings.Rules.FirstOrDefault(r => r.Id == _activeRuleId);
        _activeRuleId = null;

        if (rule?.RestoreOnExit == true && _snapshot is not null)
        {
            _controller.Restore(_snapshot);
        }

        _snapshot = null;
    }

    private static (string? ProcessName, bool Fullscreen) GetForegroundInfo()
    {
        try
        {
            var hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
            {
                return (null, false);
            }

            NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0 || pid == Environment.ProcessId)
            {
                return (null, false);
            }

            string? name = null;
            try
            {
                name = Process.GetProcessById((int)pid).ProcessName;
            }
            catch
            {
            }

            return (name, IsFullscreen(hwnd));
        }
        catch
        {
            return (null, false);
        }
    }

    private static bool IsFullscreen(IntPtr hwnd)
    {
        try
        {
            if (!NativeMethods.GetWindowRect(hwnd, out var rect))
            {
                return false;
            }

            var style = NativeMethods.GetWindowLong(hwnd, -16); // GWL_STYLE
            if ((style & WS_CAPTION) == WS_CAPTION)
            {
                return false;
            }

            var monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
            if (monitor == IntPtr.Zero)
            {
                return false;
            }

            var info = new NativeMethods.MONITORINFOEX
            {
                cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFOEX>(),
                szDevice = string.Empty,
            };

            if (!NativeMethods.GetMonitorInfo(monitor, ref info))
            {
                return false;
            }

            const int tolerance = 2;
            return Math.Abs(rect.Left - info.rcMonitor.Left) <= tolerance
                   && Math.Abs(rect.Top - info.rcMonitor.Top) <= tolerance
                   && Math.Abs(rect.Right - info.rcMonitor.Right) <= tolerance
                   && Math.Abs(rect.Bottom - info.rcMonitor.Bottom) <= tolerance;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose() => Stop();
}

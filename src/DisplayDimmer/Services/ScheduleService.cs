using System.Windows.Threading;
using DisplayDimmer.Models;

namespace DisplayDimmer.Services;

/// <summary>Aplica presets ou valores em horarios definidos ao longo do dia.</summary>
public sealed class ScheduleService : IDisposable
{
    private readonly DisplayController _controller;
    private readonly SettingsService _settings;
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<string, DateTime> _lastRun = new();

    public ScheduleService(DisplayController controller, SettingsService settings)
    {
        _controller = controller;
        _settings = settings;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _timer.Tick += (_, _) => Evaluate();
    }

    public event EventHandler<ScheduleEntry>? Applied;

    public void Start()
    {
        Evaluate();
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    /// <summary>Aplica o agendamento mais recente do dia (usado ao iniciar o aplicativo).</summary>
    public void CatchUp()
    {
        var now = DateTime.Now;
        ScheduleEntry? latest = null;
        var latestTime = TimeSpan.MinValue;

        foreach (var entry in _settings.Settings.Schedules.Where(e => e.Enabled))
        {
            if (!TimeSpan.TryParse(entry.Time, out var time) || time > now.TimeOfDay)
            {
                continue;
            }

            if (time > latestTime)
            {
                latestTime = time;
                latest = entry;
            }
        }

        if (latest is null)
        {
            return;
        }

        _controller.ApplySchedule(latest);
        _lastRun[latest.Id] = now;
        Applied?.Invoke(this, latest);
    }

    private void Evaluate()
    {
        if (!_settings.Settings.AutomationsEnabled)
        {
            return;
        }

        var now = DateTime.Now;

        foreach (var entry in _settings.Settings.Schedules.Where(e => e.Enabled).ToList())
        {
            if (!TimeSpan.TryParse(entry.Time, out var time))
            {
                continue;
            }

            var scheduled = now.Date + time;
            var atraso = now - scheduled;

            // Aplica no horario e tambem recupera ate 30 minutos depois (suspensao, hibernacao).
            if (atraso < TimeSpan.Zero || atraso > TimeSpan.FromMinutes(30))
            {
                continue;
            }

            if (_lastRun.TryGetValue(entry.Id, out var last) && last.Date == now.Date && last.TimeOfDay >= time)
            {
                continue;
            }

            _controller.ApplySchedule(entry);
            _lastRun[entry.Id] = now;
            Applied?.Invoke(this, entry);
        }
    }

    public void Dispose()
    {
        _timer.Stop();
    }
}

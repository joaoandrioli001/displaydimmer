using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using DisplayDimmer.Models;

namespace DisplayDimmer.Services;

/// <summary>Carrega e grava a configuracao em %APPDATA%\DisplayDimmer\settings.json.</summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly object _gate = new();
    private Timer? _debounce;

    public SettingsService(string? folder = null)
    {
        FolderPath = folder ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DisplayDimmer");

        Settings = new AppSettings();
    }

    public string FolderPath { get; }

    public string FilePath => Path.Combine(FolderPath, "settings.json");

    public AppSettings Settings { get; private set; }

    /// <summary>Verdadeiro quando o arquivo de configuracao ainda nao existia.</summary>
    public bool IsFirstRun { get; private set; }

    public event EventHandler? Changed;

    public void Load()
    {
        try
        {
            Directory.CreateDirectory(FolderPath);

            if (!File.Exists(FilePath))
            {
                IsFirstRun = true;
                Settings = AppSettings.CreateDefault();
                Save();
                return;
            }

            var json = File.ReadAllText(FilePath);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            Settings = loaded ?? AppSettings.CreateDefault();
            Normalize(Settings);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            BackupCorruptedFile();
            Settings = AppSettings.CreateDefault();
            Save();
        }
    }

    private void Normalize(AppSettings settings)
    {
        settings.Presets ??= new List<Preset>();
        settings.Groups ??= new List<MonitorGroup>();
        settings.Schedules ??= new List<ScheduleEntry>();
        settings.Rules ??= new List<AppRule>();
        settings.Displays ??= new Dictionary<string, DisplayState>();
        settings.Hotkeys ??= new HotkeySettings();
        settings.Hotkeys.Presets ??= new Dictionary<string, string>();

        foreach (var preset in settings.Presets)
        {
            preset.PerDisplay ??= new Dictionary<string, DisplayState>();
        }

        foreach (var group in settings.Groups)
        {
            group.DisplayIds ??= new List<string>();
        }

        if (settings.TrayStepPercent is < 1 or > 25)
        {
            settings.TrayStepPercent = 5;
        }

        // Migracao: versoes antigas gravavam UseHardware em vez de Control.
        foreach (var state in settings.Displays.Values)
        {
            if (state.LegacyUseHardware == false)
            {
                state.Control = DisplayControl.Software;
            }

            state.LegacyUseHardware = null;
            state.BlackLevel = Math.Clamp(state.BlackLevel, 0, 128);
            state.WhiteLevel = Math.Clamp(state.WhiteLevel, state.BlackLevel + 8, 255);
        }
    }

    private void BackupCorruptedFile()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return;
            }

            var backup = Path.Combine(FolderPath, $"settings.corrupted-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.Move(FilePath, backup, overwrite: true);
        }
        catch
        {
        }
    }

    public void Save()
    {
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(FolderPath);
                var json = JsonSerializer.Serialize(Settings, JsonOptions);
                var temp = FilePath + ".tmp";
                File.WriteAllText(temp, json);
                File.Move(temp, FilePath, overwrite: true);
            }
            catch
            {
                // Falha de disco nao deve derrubar o aplicativo.
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Grava com atraso para nao escrever em disco a cada movimento de slider.</summary>
    public void SaveSoon()
    {
        lock (_gate)
        {
            _debounce?.Dispose();
            _debounce = new Timer(_ => Save(), null, 600, Timeout.Infinite);
        }
    }

    public void Flush()
    {
        lock (_gate)
        {
            _debounce?.Dispose();
            _debounce = null;
        }

        Save();
    }
}

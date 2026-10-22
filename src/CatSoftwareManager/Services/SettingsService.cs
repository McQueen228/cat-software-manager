using System.Text;
using System.Text.Json;
using CatSoftwareManager.Models;

namespace CatSoftwareManager.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public SettingsService()
    {
        AppDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CatSoftwareManager");
        SettingsPath = Path.Combine(AppDataDirectory, "settings.json");
        LogDirectory = Path.Combine(AppDataDirectory, "logs");
    }

    public string AppDataDirectory { get; }
    public string SettingsPath { get; }
    public string LogDirectory { get; }
    public AppSettings Current { get; private set; } = new();

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath, Encoding.UTF8);
                Current = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
        }
        catch (Exception ex)
        {
            WriteLog("Settings load failed", ex);
            Current = new AppSettings();
        }

        Current.IgnoredApps = new HashSet<string>(
            (IEnumerable<string>?)Current.IgnoredApps ?? Array.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);
        Current.SkippedVersions = new Dictionary<string, string>(
            Current.SkippedVersions ?? new Dictionary<string, string>(),
            StringComparer.OrdinalIgnoreCase);
        return Current;
    }

    public void Save(AppSettings? settings = null)
    {
        var value = settings ?? Current;
        try
        {
            Directory.CreateDirectory(AppDataDirectory);
            var tempPath = SettingsPath + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(value, JsonOptions), new UTF8Encoding(false));
            File.Move(tempPath, SettingsPath, true);
        }
        catch (Exception ex)
        {
            WriteLog("Settings save failed", ex);
        }
    }

    public void WriteLog(string message, Exception? exception = null)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            var line = $"{DateTimeOffset.Now:O} {message}";
            if (exception is not null)
            {
                line += Environment.NewLine + exception;
            }

            File.AppendAllText(
                Path.Combine(LogDirectory, $"manager-{DateTime.Now:yyyyMMdd}.log"),
                line + Environment.NewLine,
                new UTF8Encoding(false));
        }
        catch
        {
            // Logging must never prevent the application from starting.
        }
    }
}

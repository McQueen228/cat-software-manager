namespace CatSoftwareManager.Models;

public sealed class AppSettings
{
    public string Language { get; set; } = "zh-CN";
    public string Theme { get; set; } = "System";
    public bool AutoStartEnabled { get; set; } = true;
    public string LastAutoScanDate { get; set; } = "";
    public string LastManagerUpdateCheckDate { get; set; } = "";
    public HashSet<string> IgnoredApps { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> SkippedVersions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

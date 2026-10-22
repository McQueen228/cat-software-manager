namespace CatSoftwareManager.Models;

public enum ApplicationKind
{
    Desktop,
    Msix
}

public enum UpdateStatus
{
    Unknown,
    UpToDate,
    UpdateAvailable,
    Skipped,
    Ignored
}

public enum VersionSourceKind
{
    Unknown,
    Official,
    Winget
}

public sealed class ApplicationInfo
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public string CurrentVersion { get; init; } = "";
    public string Publisher { get; init; } = "";
    public string? InstallLocation { get; init; }
    public string? UninstallString { get; init; }
    public ApplicationKind Kind { get; init; }

    public string? WingetId { get; set; }
    public string? WingetLatestVersion { get; set; }
    public string? LatestVersion { get; set; }
    public string? UpdatePageUrl { get; set; }
    public string? HomepageUrl { get; set; }
    public string? OfficialSourceName { get; set; }
    public VersionSourceKind SourceKind { get; set; }
    public UpdateStatus Status { get; set; } = UpdateStatus.Unknown;
}

public sealed class WingetPackage
{
    public string Name { get; init; } = "";
    public string Id { get; init; } = "";
    public string Version { get; init; } = "";
    public string Available { get; init; } = "";
    public string Source { get; init; } = "";
}

public enum VersionSourceType
{
    Regex,
    GitHubPage,
    ChromeVersions,
    VsCodeUpdate,
    EdgeUpdate,
    PythonLatest
}

public sealed class VersionSourceDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required IReadOnlyList<string> Match { get; init; }
    public string? Publisher { get; init; }
    public string? WingetId { get; init; }
    public string? HomepageUrl { get; init; }
    public string? UpdatePageUrl { get; init; }
    public required VersionSourceType SourceType { get; init; }
    public required string Url { get; init; }
    public string? Pattern { get; init; }
    public bool PickHighest { get; init; }
}

public sealed class VersionSourceResult
{
    public required string LatestVersion { get; init; }
    public required string SourceName { get; init; }
    public string? SourceUrl { get; init; }
    public string? Error { get; init; }
}

public sealed class ScanResult
{
    public IReadOnlyList<ApplicationInfo> Applications { get; init; } = Array.Empty<ApplicationInfo>();
    public DateTimeOffset CheckedAt { get; init; } = DateTimeOffset.Now;
}

public sealed class UpdateOutcome
{
    public required ApplicationInfo Application { get; init; }
    public bool Success { get; init; }
    public bool RestartRequired { get; init; }
    public bool PageOpened { get; init; }
    public string Message { get; init; } = "";
}

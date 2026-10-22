using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CatSoftwareManager.Models;

namespace CatSoftwareManager.Services;

public sealed class SoftwareScanService
{
    private static readonly Regex ParentheticalNoise = new(
        @"\((?:x64|x86|user|preview|64-bit|32-bit)\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TrailingVersion = new(
        @"\s+v?\d+(?:\.\d+){1,4}.*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly InstalledApplicationScanner _installedScanner;
    private readonly AppxApplicationScanner _appxScanner;
    private readonly WingetService _winget;
    private readonly OfficialVersionService _officialVersions;
    private readonly SettingsService _settings;

    public SoftwareScanService(
        InstalledApplicationScanner installedScanner,
        AppxApplicationScanner appxScanner,
        WingetService winget,
        OfficialVersionService officialVersions,
        SettingsService settings)
    {
        _installedScanner = installedScanner;
        _appxScanner = appxScanner;
        _winget = winget;
        _officialVersions = officialVersions;
        _settings = settings;
    }

    public async Task<ScanResult> ScanAsync(
        AppSettings settings,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report("installed");
        var desktopTask = Task.Run(_installedScanner.Scan, cancellationToken);
        var msixTask = _appxScanner.ScanAsync(_settings, cancellationToken);
        var wingetTask = _winget.GetInstalledPackagesAsync(cancellationToken);
        await Task.WhenAll(desktopTask, msixTask, wingetTask);

        var applications = desktopTask.Result
            .Concat(msixTask.Result)
            .GroupBy(app => app.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        var definitions = _officialVersions.LoadDefinitions();
        foreach (var application in applications)
        {
            var definition = _officialVersions.Match(application, definitions);
            if (definition is null)
            {
                continue;
            }

            application.WingetId = definition.WingetId;
            application.HomepageUrl = definition.HomepageUrl;
            application.UpdatePageUrl = definition.UpdatePageUrl;
        }

        var wingetPackages = wingetTask.Result;
        EnrichWithWinget(applications, wingetPackages);

        progress?.Report("official");
        var officialResults = await _officialVersions.GetLatestVersionsAsync(
            applications,
            definitions,
            progress,
            cancellationToken);

        progress?.Report("evaluate");
        foreach (var application in applications)
        {
            if (officialResults.TryGetValue(application.Id, out var result))
            {
                application.LatestVersion = result.LatestVersion;
                application.OfficialSourceName = result.SourceName;
                application.SourceKind = VersionSourceKind.Official;
            }

            EvaluateStatus(application, settings);
        }

        return new ScanResult
        {
            Applications = applications
                .OrderBy(app => app.Status == UpdateStatus.UpdateAvailable ? 0 : 1)
                .ThenBy(app => app.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToArray(),
            CheckedAt = DateTimeOffset.Now
        };
    }

    private static void EnrichWithWinget(
        IReadOnlyList<ApplicationInfo> applications,
        IReadOnlyList<WingetPackage> packages)
    {
        var byName = packages
            .Where(package => !IsRegistryOnlyPackage(package.Id))
            .GroupBy(package => NormalizeTitle(package.Name), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(package => package.Source.Equals("winget", StringComparison.OrdinalIgnoreCase))
                    .First(),
                StringComparer.OrdinalIgnoreCase);
        var byId = packages
            .Where(package => !string.IsNullOrWhiteSpace(package.Id))
            .GroupBy(package => package.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var application in applications)
        {
            WingetPackage? package = null;
            if (!string.IsNullOrWhiteSpace(application.WingetId))
            {
                byId.TryGetValue(application.WingetId, out package);
            }

            if (package is null)
            {
                byName.TryGetValue(NormalizeTitle(application.DisplayName), out package);
            }

            if (package is null || IsRegistryOnlyPackage(package.Id))
            {
                continue;
            }

            application.WingetId = package.Id;
            var wingetLatest = !string.IsNullOrWhiteSpace(package.Available)
                ? package.Available
                : package.Version;
            if (string.IsNullOrWhiteSpace(wingetLatest) ||
                wingetLatest.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            application.WingetLatestVersion = wingetLatest;
            if (application.SourceKind != VersionSourceKind.Official)
            {
                application.LatestVersion = wingetLatest;
                application.SourceKind = VersionSourceKind.Winget;
            }
        }
    }

    private static void EvaluateStatus(ApplicationInfo application, AppSettings settings)
    {
        if (settings.IgnoredApps.Contains(application.Id))
        {
            application.Status = UpdateStatus.Ignored;
            return;
        }

        if (string.IsNullOrWhiteSpace(application.LatestVersion) ||
            string.IsNullOrWhiteSpace(application.CurrentVersion) ||
            !VersionComparer.TryCompare(application.CurrentVersion, application.LatestVersion, out var comparison))
        {
            application.Status = UpdateStatus.Unknown;
            return;
        }

        if (comparison >= 0)
        {
            application.Status = UpdateStatus.UpToDate;
            return;
        }

        application.Status =
            settings.SkippedVersions.TryGetValue(application.Id, out var skippedVersion) &&
            string.Equals(skippedVersion, application.LatestVersion, StringComparison.OrdinalIgnoreCase)
                ? UpdateStatus.Skipped
                : UpdateStatus.UpdateAvailable;
    }

    private static bool IsRegistryOnlyPackage(string id) =>
        id.StartsWith("ARP\\", StringComparison.OrdinalIgnoreCase) ||
        id.StartsWith("MSIX\\", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeTitle(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormKC).Trim();
        normalized = ParentheticalNoise.Replace(normalized, "");
        normalized = TrailingVersion.Replace(normalized, "");
        normalized = normalized
            .Replace("：", ":", StringComparison.Ordinal)
            .Replace("专业版", "", StringComparison.OrdinalIgnoreCase);
        return new string(normalized
            .Where(character => char.IsLetterOrDigit(character))
            .Select(char.ToLowerInvariant)
            .ToArray());
    }
}

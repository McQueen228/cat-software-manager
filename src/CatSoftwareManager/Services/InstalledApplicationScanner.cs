using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CatSoftwareManager.Models;
using Microsoft.Win32;

namespace CatSoftwareManager.Services;

public sealed class InstalledApplicationScanner
{
    private const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";
    private static readonly Regex DottedVersion = new(
        @"(?<!\d)(\d+\.\d+(?:\.\d+){0,4})(?!\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex[] ExcludedNamePatterns =
    [
        new(@"^Microsoft Visual C\+\+", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^Microsoft \.NET", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^Microsoft Windows Desktop Runtime", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^Microsoft Windows Application Compatibility", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^Windows Software Development Kit", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^Windows SDK", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^WPTx64$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^Bonjour$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^CEF for ", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^Microsoft Visual Studio Tools for Applications", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^Microsoft Visual Studio Installer$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bRedistributable\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bRuntime\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bSDK\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bDriver\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\b驱动(程序)?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^Raw Common Components$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bCommon Components$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bLogin Manager$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bControl Panels$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bAddOn$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^Lenovo Vantage Service$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^猫猫的软件管理器$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^Cat Software Manager$", RegexOptions.IgnoreCase | RegexOptions.Compiled)
    ];

    public IReadOnlyList<ApplicationInfo> Scan()
    {
        var candidates = new List<ApplicationInfo>();
        foreach (var (hive, view) in GetRegistryTargets())
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var uninstallKey = baseKey.OpenSubKey(UninstallKeyPath, writable: false);
            if (uninstallKey is null)
            {
                continue;
            }

            foreach (var subKeyName in uninstallKey.GetSubKeyNames())
            {
                try
                {
                    using var subKey = uninstallKey.OpenSubKey(subKeyName, writable: false);
                    var app = ReadApplication(subKey, subKeyName);
                    if (app is not null)
                    {
                        candidates.Add(app);
                    }
                }
                catch
                {
                    // A broken uninstall entry should not stop the scan.
                }
            }
        }

        return candidates
            .GroupBy(
                app => app.DisplayName.Trim(),
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(app => !string.IsNullOrWhiteSpace(app.InstallLocation))
                .ThenByDescending(app => !string.IsNullOrWhiteSpace(app.Publisher))
                .ThenByDescending(app => !string.IsNullOrWhiteSpace(app.CurrentVersion))
                .First())
            .OrderBy(app => app.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<(RegistryHive Hive, RegistryView View)> GetRegistryTargets()
    {
        yield return (RegistryHive.CurrentUser, RegistryView.Registry64);
        yield return (RegistryHive.CurrentUser, RegistryView.Registry32);
        yield return (RegistryHive.LocalMachine, RegistryView.Registry64);
        yield return (RegistryHive.LocalMachine, RegistryView.Registry32);
    }

    private static ApplicationInfo? ReadApplication(RegistryKey? key, string subKeyName)
    {
        if (key is null)
        {
            return null;
        }

        var name = (key.GetValue("DisplayName") as string)?.Trim();
        if (string.IsNullOrWhiteSpace(name) || ShouldExclude(name))
        {
            return null;
        }

        if (ToInt(key.GetValue("SystemComponent")) == 1 ||
            ToInt(key.GetValue("NoDisplay")) == 1 ||
            !string.IsNullOrWhiteSpace(key.GetValue("ParentKeyName") as string))
        {
            return null;
        }

        var releaseType = key.GetValue("ReleaseType") as string;
        if (releaseType is not null &&
            (releaseType.Contains("Update", StringComparison.OrdinalIgnoreCase) ||
             releaseType.Contains("Hotfix", StringComparison.OrdinalIgnoreCase) ||
             releaseType.Contains("Security", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var publisher = (key.GetValue("Publisher") as string)?.Trim() ?? "";
        var version = CleanVersion((key.GetValue("DisplayVersion") as string) ?? "");
        if (string.IsNullOrWhiteSpace(version) || IsArchitectureOnly(version))
        {
            var match = DottedVersion.Match(name);
            if (match.Success)
            {
                version = match.Groups[1].Value;
            }
        }

        var installLocation = (key.GetValue("InstallLocation") as string)?.Trim().Trim('"');
        var uninstallString = (key.GetValue("QuietUninstallString") as string)?.Trim()
                              ?? (key.GetValue("UninstallString") as string)?.Trim();

        return new ApplicationInfo
        {
            Id = BuildStableId(publisher, name),
            DisplayName = name,
            CurrentVersion = version,
            Publisher = publisher,
            InstallLocation = string.IsNullOrWhiteSpace(installLocation) ? null : installLocation,
            UninstallString = uninstallString,
            Kind = ApplicationKind.Desktop
        };
    }

    private static bool ShouldExclude(string name) =>
        ExcludedNamePatterns.Any(pattern => pattern.IsMatch(name)) ||
        name.StartsWith("Update for ", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("Security Update for ", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("Hotfix for ", StringComparison.OrdinalIgnoreCase);

    private static int ToInt(object? value) => value switch
    {
        int number => number,
        long number => (int)number,
        string text when int.TryParse(text, out var number) => number,
        _ => 0
    };

    private static string CleanVersion(string version) =>
        version.Trim().TrimStart('v', 'V').Trim();

    private static bool IsArchitectureOnly(string version) =>
        Regex.IsMatch(
            version,
            @"^(?:\d+(?:-bit)?|\d+(?:\.\d+)*-(?:32|64)(?:-bit)?)$",
            RegexOptions.IgnoreCase);

    private static string NormalizePublisher(string publisher) =>
        publisher.Replace(" ", "", StringComparison.Ordinal).ToLowerInvariant();

    internal static string BuildStableId(string publisher, string displayName)
    {
        var raw = $"{NormalizePublisher(publisher)}|{displayName.Trim().ToLowerInvariant()}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }
}

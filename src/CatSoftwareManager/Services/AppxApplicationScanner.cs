using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CatSoftwareManager.Models;

namespace CatSoftwareManager.Services;

public sealed class AppxApplicationScanner
{
    private static readonly string[] ExcludedNameParts =
    [
        "Driver",
        "Framework",
        "Runtime",
        "Extension",
        "ContextPkg",
        "SparseApp",
        "Printer"
    ];

    public async Task<IReadOnlyList<ApplicationInfo>> ScanAsync(
        SettingsService settings,
        CancellationToken cancellationToken = default)
    {
        var script = """
$ErrorActionPreference = 'SilentlyContinue'
$startApps = Get-StartApps
$items = Get-AppxPackage | Where-Object { -not $_.IsFramework -and -not $_.IsResourcePackage } | ForEach-Object {
    $family = [string]$_.PackageFamilyName
    $startApp = $startApps | Where-Object { $_.AppID -like ($family + '!*') } | Select-Object -First 1
    $manifest = Get-AppxPackageManifest $_
    $display = [string]$manifest.Package.Properties.DisplayName
    if ([string]::IsNullOrWhiteSpace($display) -or $display.StartsWith('ms-resource:')) {
        $display = if ($null -ne $startApp) { [string]$startApp.Name } else { $_.Name }
    }
    [pscustomobject]@{
        Name = $display
        PackageName = $_.Name
        Version = [string]$_.Version
        Publisher = [string]$_.Publisher
        InstallLocation = [string]$_.InstallLocation
        HasStartApp = $null -ne $startApp
    }
}
$json = $items | ConvertTo-Json -Compress -Depth 3
if ([string]::IsNullOrWhiteSpace($json)) { '[]' } else { $json }
""";

        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe"),
            Arguments = $"-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return Array.Empty<ApplicationInfo>();
            }

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(45), cancellationToken);
            var json = await outputTask;
            var errors = await errorTask;
            if (process.ExitCode != 0 && string.IsNullOrWhiteSpace(json))
            {
                settings.WriteLog($"MSIX scan exited with {process.ExitCode}: {errors}");
                return Array.Empty<ApplicationInfo>();
            }

            using var document = JsonDocument.Parse(json);
            var items = document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement.EnumerateArray().ToArray()
                : new[] { document.RootElement.Clone() };

            return items
                .Select(ToApplication)
                .Where(app => app is not null)
                .Cast<ApplicationInfo>()
                .GroupBy(app => app.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(app => app.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
        catch (Exception ex)
        {
            settings.WriteLog("MSIX scan failed", ex);
            return Array.Empty<ApplicationInfo>();
        }
    }

    private static ApplicationInfo? ToApplication(JsonElement item)
    {
        var name = GetString(item, "Name");
        var packageName = GetString(item, "PackageName");
        var publisher = GetString(item, "Publisher");
        var hasStartApp = item.TryGetProperty("HasStartApp", out var startElement) &&
                          startElement.ValueKind == JsonValueKind.True;
        if (string.IsNullOrWhiteSpace(name) || !hasStartApp || IsSystemPackage(name, packageName, publisher))
        {
            return null;
        }

        return new ApplicationInfo
        {
            Id = InstalledApplicationScanner.BuildStableId(publisher, name),
            DisplayName = name,
            CurrentVersion = GetString(item, "Version"),
            Publisher = publisher,
            InstallLocation = GetString(item, "InstallLocation"),
            Kind = ApplicationKind.Msix
        };
    }

    private static bool IsSystemPackage(string name, string packageName, string publisher)
    {
        if (packageName.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase) ||
            packageName.StartsWith("MicrosoftCorporationII.", StringComparison.OrdinalIgnoreCase) ||
            packageName.StartsWith("MicrosoftWindows.", StringComparison.OrdinalIgnoreCase) ||
            publisher.Contains("O=Microsoft Corporation", StringComparison.OrdinalIgnoreCase) ||
            publisher.Contains("CN=Microsoft Windows", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return ExcludedNameParts.Any(part =>
            name.Contains(part, StringComparison.OrdinalIgnoreCase) ||
            packageName.Contains(part, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind is not JsonValueKind.Null
            ? property.ToString().Trim()
            : "";
}

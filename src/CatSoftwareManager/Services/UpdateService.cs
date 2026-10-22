using System.Diagnostics;
using CatSoftwareManager.Models;

namespace CatSoftwareManager.Services;

public sealed class UpdateService
{
    private readonly WingetService _winget;
    private readonly SettingsService _settings;

    public UpdateService(WingetService winget, SettingsService settings)
    {
        _winget = winget;
        _settings = settings;
    }

    public IReadOnlyList<string> GetRunningExecutables(ApplicationInfo application)
    {
        if (string.IsNullOrWhiteSpace(application.InstallLocation) ||
            !Directory.Exists(application.InstallLocation))
        {
            return Array.Empty<string>();
        }

        var installRoot = Path.GetFullPath(application.InstallLocation)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var currentProcessId = Environment.ProcessId;
        var running = new List<string>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.Id == currentProcessId)
                {
                    continue;
                }

                var fileName = process.MainModule?.FileName;
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    continue;
                }

                var fullPath = Path.GetFullPath(fileName);
                if (fullPath.StartsWith(installRoot, StringComparison.OrdinalIgnoreCase))
                {
                    running.Add(Path.GetFileName(fileName));
                }
            }
            catch
            {
                // Access denied is expected for elevated or protected processes.
            }
            finally
            {
                process.Dispose();
            }
        }

        return running.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<UpdateOutcome> UpdateAsync(
        ApplicationInfo application,
        IProgress<string>? progress,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(application.WingetId) &&
            !application.WingetId.StartsWith("ARP\\", StringComparison.OrdinalIgnoreCase) &&
            !application.WingetId.StartsWith("MSIX\\", StringComparison.OrdinalIgnoreCase) &&
            _winget.ExecutablePath is not null &&
            WingetCanReachLatest(application))
        {
            try
            {
                var result = await _winget.UpgradeAsync(
                    application.WingetId,
                    progress,
                    cancellationToken);
                if (result.Success)
                {
                    return new UpdateOutcome
                    {
                        Application = application,
                        Success = true,
                        RestartRequired = result.RestartRequired,
                        Message = result.Message
                    };
                }

                _settings.WriteLog(
                    $"winget update failed for {application.DisplayName}: {result.Message}");
            }
            catch (Exception ex)
            {
                _settings.WriteLog($"Update command failed for {application.DisplayName}", ex);
            }
        }

        if (!string.IsNullOrWhiteSpace(application.UpdatePageUrl) &&
            Uri.TryCreate(application.UpdatePageUrl, UriKind.Absolute, out var updateUri))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = updateUri.ToString(),
                    UseShellExecute = true
                });
                return new UpdateOutcome
                {
                    Application = application,
                    Success = false,
                    PageOpened = true,
                    Message = "The official update page was opened."
                };
            }
            catch (Exception ex)
            {
                _settings.WriteLog($"Could not open update page for {application.DisplayName}", ex);
            }
        }

        return new UpdateOutcome
        {
            Application = application,
            Success = false,
            Message = "No usable update channel was found."
        };
    }

    private static bool WingetCanReachLatest(ApplicationInfo application)
    {
        if (application.SourceKind != VersionSourceKind.Official ||
            string.IsNullOrWhiteSpace(application.WingetLatestVersion) ||
            string.IsNullOrWhiteSpace(application.LatestVersion))
        {
            return true;
        }

        return VersionComparer.TryCompare(
                   application.WingetLatestVersion,
                   application.LatestVersion,
                   out var comparison) &&
               comparison >= 0;
    }
}

using System.Diagnostics;
using System.Globalization;
using System.Text;
using CatSoftwareManager.Models;

namespace CatSoftwareManager.Services;

public sealed class WingetService
{
    private readonly SettingsService _settings;

    public WingetService(SettingsService settings)
    {
        _settings = settings;
    }

    public string? ExecutablePath => FindExecutable();

    public async Task<IReadOnlyList<WingetPackage>> GetInstalledPackagesAsync(
        CancellationToken cancellationToken = default)
    {
        if (ExecutablePath is null)
        {
            return Array.Empty<WingetPackage>();
        }

        ProcessResult result;
        try
        {
            result = await RunAsync(
                "list --accept-source-agreements --disable-interactivity",
                TimeSpan.FromSeconds(90),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _settings.WriteLog("winget list failed", ex);
            return Array.Empty<WingetPackage>();
        }

        if (string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            _settings.WriteLog($"winget list returned no output. Exit={result.ExitCode}. {result.StandardError}");
            return Array.Empty<WingetPackage>();
        }

        return ParseTable(result.StandardOutput);
    }

    public async Task<(bool Success, bool RestartRequired, string Message)> UpgradeAsync(
        string wingetId,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (ExecutablePath is null)
        {
            return (false, false, "Winget is not available.");
        }

        progress?.Report($"winget upgrade --id {wingetId}");
        var result = await RunAsync(
            $"upgrade --id \"{wingetId.Replace("\"", "\\\"", StringComparison.Ordinal)}\" --exact " +
            "--silent --accept-package-agreements --accept-source-agreements --disable-interactivity",
            TimeSpan.FromMinutes(30),
            cancellationToken);

        var combined = string.Join(
            Environment.NewLine,
            new[] { result.StandardOutput, result.StandardError }
                .Where(text => !string.IsNullOrWhiteSpace(text)));
        var restartRequired = result.ExitCode is 3010 or 1641 ||
                              combined.Contains("restart", StringComparison.OrdinalIgnoreCase) ||
                              combined.Contains("重启", StringComparison.OrdinalIgnoreCase);
        var success = result.ExitCode is 0 or 3010 or 1641;
        return (success, restartRequired, combined.Trim());
    }

    public static IReadOnlyList<WingetPackage> ParseTable(string output)
    {
        var lines = output
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n');
        var headerIndex = Array.FindIndex(
            lines,
            line => line.Contains("Name", StringComparison.Ordinal) &&
                    line.Contains("Id", StringComparison.Ordinal) &&
                    line.Contains("Version", StringComparison.Ordinal) &&
                    line.Contains("Source", StringComparison.Ordinal));
        if (headerIndex < 0)
        {
            return Array.Empty<WingetPackage>();
        }

        var header = lines[headerIndex];
        var idColumn = header.IndexOf("Id", StringComparison.Ordinal);
        var versionColumn = header.IndexOf("Version", StringComparison.Ordinal);
        var availableColumn = header.IndexOf("Available", StringComparison.Ordinal);
        var sourceColumn = header.IndexOf("Source", StringComparison.Ordinal);
        if (idColumn <= 0 || versionColumn <= idColumn || availableColumn <= versionColumn || sourceColumn <= availableColumn)
        {
            return Array.Empty<WingetPackage>();
        }

        var packages = new List<WingetPackage>();
        foreach (var line in lines.Skip(headerIndex + 1))
        {
            if (string.IsNullOrWhiteSpace(line) ||
                line.All(character => character is '-' or ' '))
            {
                continue;
            }

            var name = SliceByDisplayColumns(line, 0, idColumn).Trim();
            var id = SliceByDisplayColumns(line, idColumn, versionColumn).Trim();
            var version = SliceByDisplayColumns(line, versionColumn, availableColumn).Trim();
            var available = SliceByDisplayColumns(line, availableColumn, sourceColumn).Trim();
            var source = SliceByDisplayColumns(line, sourceColumn, int.MaxValue).Trim();

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            packages.Add(new WingetPackage
            {
                Name = name,
                Id = id,
                Version = version,
                Available = available,
                Source = source
            });
        }

        return packages;
    }

    private async Task<ProcessResult> RunAsync(
        string arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var executable = ExecutablePath
                         ?? throw new InvalidOperationException("winget.exe was not found.");
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException("Could not start winget.exe.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken).WaitAsync(timeout, cancellationToken);
        }
        catch
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // The process may have already exited.
            }

            throw;
        }

        return new ProcessResult(
            process.ExitCode,
            await outputTask,
            await errorTask);
    }

    private static string? FindExecutable()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var directory in path.Split(
                     Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(directory, "winget.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var aliasPath = Path.Combine(localAppData, "Microsoft", "WindowsApps", "winget.exe");
        return File.Exists(aliasPath) ? aliasPath : null;
    }

    private static string SliceByDisplayColumns(string value, int start, int end)
    {
        if (end <= start)
        {
            return "";
        }

        var builder = new StringBuilder();
        var column = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            var width = GetRuneWidth(rune);
            if (column >= start && column < end)
            {
                builder.Append(rune);
            }

            column += width;
            if (column >= end)
            {
                break;
            }
        }

        return builder.ToString();
    }

    private static int GetRuneWidth(Rune rune)
    {
        var category = Rune.GetUnicodeCategory(rune);
        if (category is UnicodeCategory.NonSpacingMark or
            UnicodeCategory.EnclosingMark or
            UnicodeCategory.Format)
        {
            return 0;
        }

        var value = rune.Value;
        if (value < 0x1100)
        {
            return 1;
        }

        return value <= 0x115F ||
               value is 0x2329 or 0x232A ||
               value is >= 0x2E80 and <= 0xA4CF ||
               value is >= 0xAC00 and <= 0xD7A3 ||
               value is >= 0xF900 and <= 0xFAFF ||
               value is >= 0xFE10 and <= 0xFE19 ||
               value is >= 0xFE30 and <= 0xFE6F ||
               value is >= 0xFF00 and <= 0xFF60 ||
               value is >= 0xFFE0 and <= 0xFFE6 ||
               value is >= 0x1F300 and <= 0x1FAFF ||
               value is >= 0x20000 and <= 0x3FFFD
            ? 2
            : 1;
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}

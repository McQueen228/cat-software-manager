using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using CatSoftwareManager.ViewModels;

namespace CatSoftwareManager.Services;

public static class ManagerUpdateService
{
    // These values are replaced with the public repository location after upload.
    public const string GitHubOwner = "McQueen228";
    public const string GitHubRepository = "cat-software-manager";

    public static async Task CheckFromUiAsync(Window owner, MainViewModel viewModel)
    {
        try
        {
            var release = await GetLatestReleaseAsync();
            if (release is null || !VersionComparer.IsNewer(CurrentVersion, release.Version))
            {
                MessageBox.Show(
                    owner,
                    release is null
                        ? viewModel.Localization["AppSelfUpdateUnavailable"]
                        : viewModel.Localization["ManagerUpdateLatest"],
                    viewModel.Localization["CheckManagerUpdate"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var answer = MessageBox.Show(
                owner,
                viewModel.Localization.Format("ManagerUpdateOpen", release.Version),
                viewModel.Localization["CheckManagerUpdate"],
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
            if (answer == MessageBoxResult.Yes)
            {
                await DownloadAndStartInstallerAsync(release, viewModel);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                owner,
                $"{viewModel.Localization["AppSelfUpdateUnavailable"]}{Environment.NewLine}{ex.Message}",
                viewModel.Localization["CheckManagerUpdate"],
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    public static async Task<ManagerRelease?> GetLatestReleaseAsync(
        CancellationToken cancellationToken = default)
    {
        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("CatSoftwareManager", CurrentVersion));
        using var response = await httpClient.GetAsync(
            $"https://github.com/{GitHubOwner}/{GitHubRepository}/releases/latest",
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var finalUri = response.RequestMessage?.RequestUri;
        if (finalUri is null)
        {
            return null;
        }

        var match = Regex.Match(
            finalUri.AbsolutePath,
            @"/releases/tag/(?<version>v?[^/?#]+)",
            RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return null;
        }

        var version = match.Groups["version"].Value.TrimStart('v', 'V');
        return new ManagerRelease(version, finalUri, match.Groups["version"].Value);
    }

    public static async Task DownloadAndStartInstallerAsync(
        ManagerRelease release,
        MainViewModel viewModel,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var assetName = $"CatSoftwareManager-Setup-v{release.Version}-x64.exe";
            var downloadUri = new Uri(
                $"https://github.com/{GitHubOwner}/{GitHubRepository}/releases/download/" +
                $"{release.TagName}/{assetName}");
            var destination = Path.Combine(
                Path.GetTempPath(),
                $"CatSoftwareManager-Setup-v{release.Version}-x64.exe");
            using var httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(10)
            };
            httpClient.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("CatSoftwareManager", CurrentVersion));
            using var response = await httpClient.GetAsync(
                downloadUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var target = new FileStream(
                             destination,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             81920,
                             useAsync: true))
            {
                await source.CopyToAsync(target, cancellationToken);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = destination,
                Arguments = "/SILENT /CLOSEAPPLICATIONS /NORESTART /SUPPRESSMSGBOXES",
                UseShellExecute = true
            });
            _ = Application.Current.Dispatcher.BeginInvoke(() =>
            {
                if (Application.Current is App app)
                {
                    app.ExitApplication();
                }
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                viewModel.Localization.Format("ManagerUpdateInstallFailed", ex.Message),
                viewModel.Localization["CheckManagerUpdate"],
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private static string CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    public sealed record ManagerRelease(string Version, Uri ReleasePage, string TagName);
}

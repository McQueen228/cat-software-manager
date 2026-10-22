using System.IO;
using System.Reflection;
using CatSoftwareManager.Models;
using CatSoftwareManager.Services;
using CatSoftwareManager.ViewModels;
using CatSoftwareManager.Views;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CatSoftwareManager.SmokeTests;

internal static class Program
{
    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        if (args.Length >= 2 &&
            string.Equals(args[0], "--render-ui", StringComparison.OrdinalIgnoreCase))
        {
            Exception? renderFailure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    RenderUiPreview(args[1]);
                }
                catch (Exception ex)
                {
                    renderFailure = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (renderFailure is not null)
            {
                throw renderFailure;
            }

            return 0;
        }

        var failures = new List<string>();
        CheckVersionComparison(failures);
        CheckWingetParsing(failures);
        await CheckPythonOfficialSourceAsync(failures);
        await CheckInstalledSoftwareScanAsync(failures);

        if (failures.Count == 0)
        {
            Console.WriteLine("SMOKE TESTS PASSED");
            return 0;
        }

        Console.WriteLine("SMOKE TESTS FAILED");
        foreach (var failure in failures)
        {
            Console.WriteLine("- " + failure);
        }

        return 1;
    }

    private static void CheckVersionComparison(List<string> failures)
    {
        Assert(VersionComparer.IsNewer("1.2.3", "1.2.4"), "Simple version increase", failures);
        Assert(VersionComparer.IsNewer("0.100.2", "0.101.2362.0"), "PowerToys version increase", failures);
        Assert(VersionComparer.IsNewer("1.1.6+3", "1.1.9+6"), "Build suffix increase", failures);
        Assert(!VersionComparer.IsNewer("26.1", "26.1.0"), "Equivalent padded versions", failures);
        Assert(!VersionComparer.IsNewer("2.0", "1.9"), "Older latest version", failures);
    }

    private static void CheckWingetParsing(List<string> failures)
    {
        const string output = """
Name                                      Id                         Version      Available    Source
-----------------------------------------------------------------------------------------------------------
7-Zip 26.03 (x64)                         7zip.7zip                  26.03        26.05        winget
微信                                       Tencent.WeChat.Universal   4.1.15.13    4.1.15.56    winget
Google Chrome                              Google.Chrome              156.0.0
""";
        var packages = WingetService.ParseTable(output);
        Assert(packages.Count == 3, "Winget table row count", failures);
        Assert(
            packages.Any(package =>
                package.Id == "Tencent.WeChat.Universal" &&
                package.Name == "微信" &&
                package.Available == "4.1.15.56"),
            "Winget table handles Chinese display widths",
            failures);
    }

    private static async Task CheckInstalledSoftwareScanAsync(List<string> failures)
    {
        var settingsService = new SettingsService();
        var settings = settingsService.Load();
        var winget = new WingetService(settingsService);
        var scanner = new SoftwareScanService(
            new InstalledApplicationScanner(),
            new AppxApplicationScanner(),
            winget,
            new OfficialVersionService(settingsService),
            settingsService);
        var result = await scanner.ScanAsync(settings);
        Assert(result.Applications.Count >= 10, "At least ten applications were discovered", failures);
        Assert(
            result.Applications.Any(application =>
                application.DisplayName.Contains("Chrome", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(application.LatestVersion)),
            "Chrome received a detected latest version",
            failures);
        var cachedResult = scanner.LoadCachedResult();
        Assert(
            cachedResult is not null &&
            cachedResult.Applications.Count == result.Applications.Count,
            "Scan cache round-trips all applications",
            failures);

        Console.WriteLine($"Discovered {result.Applications.Count} applications.");
        foreach (var application in result.Applications.Take(30))
        {
            Console.WriteLine(
                $"{application.DisplayName} | {application.CurrentVersion} | " +
                $"{application.LatestVersion} | {application.Status} | " +
                $"{application.SourceKind} | {application.WingetId}");
        }
    }

    private static async Task CheckPythonOfficialSourceAsync(List<string> failures)
    {
        var settingsService = new SettingsService();
        var service = new OfficialVersionService(settingsService);
        var definitions = service.LoadDefinitions();
        var application = new ApplicationInfo
        {
            Id = "python-test",
            DisplayName = "Python 3.15.0",
            CurrentVersion = "3.15.0",
            Publisher = "Python Software Foundation"
        };
        var results = await service.GetLatestVersionsAsync(
            [application],
            definitions,
            null,
            CancellationToken.None);
        results.TryGetValue(application.Id, out var result);
        Assert(
            result is not null &&
            VersionComparer.TryCompare("3.15.1", result.LatestVersion, out var comparison) &&
            comparison <= 0,
            "Python latest source is Python 3.15.1 or the next stable release",
            failures);
        Console.WriteLine($"Python official latest: {result?.LatestVersion ?? "unavailable"}");
    }

    private static void Assert(bool condition, string description, List<string> failures)
    {
        if (!condition)
        {
            failures.Add(description);
        }
    }

    private static void RenderUiPreview(string outputPath)
    {
        var application = new CatSoftwareManager.App();
        application.InitializeComponent();
        var settingsService = new SettingsService();
        var settings = settingsService.Load();
        var localization = new LocalizationService();
        localization.SetLanguage(
            Environment.GetEnvironmentVariable("CATSM_PREVIEW_LANG") ?? "zh-CN");
        var theme = new ThemeService();
        theme.Apply(Environment.GetEnvironmentVariable("CATSM_PREVIEW_THEME") ?? "Light");
        var winget = new WingetService(settingsService);
        var scan = new SoftwareScanService(
            new InstalledApplicationScanner(),
            new AppxApplicationScanner(),
            winget,
            new OfficialVersionService(settingsService),
            settingsService);
        var viewModel = new MainViewModel(
            scan,
            new UpdateService(winget, settingsService),
            settingsService,
            localization,
            new StartupService(settingsService),
            theme);
        var previewRows = new[]
        {
            PreviewApp("示例浏览器", "Example Browser Ltd.", "128.0.6613.120", "129.0.6650.42", "Example Browser 官方版本服务"),
            PreviewApp("示例代码编辑器", "Example Tools", "2.4.1", "2.5.0", "Example Editor 官方版本服务"),
            PreviewApp("示例压缩工具", "Example Utilities", "24.08", "24.09", "Example Utilities 官方版本服务"),
            PreviewApp("示例笔记应用", "Example Notes", "3.8.2", "3.8.2", "Example Notes 官方版本服务", UpdateStatus.UpToDate),
            PreviewApp("示例音频工具", "Example Audio", "1.9.4", "2.0.0", "Winget", UpdateStatus.UpdateAvailable, VersionSourceKind.Winget),
            PreviewApp("示例图片查看器", "Example Imaging", "5.2.0", "5.2.0", "Winget", UpdateStatus.UpToDate, VersionSourceKind.Winget),
            PreviewApp("示例开发工具", "Example Dev", "0.93.1", "", "", UpdateStatus.Unknown),
            PreviewApp("示例录屏软件", "Example Screen", "4.1.0", "4.2.0", "Example Screen 官方版本服务")
        };
        var rows = previewRows
            .Select(application => new AppRowViewModel(application, localization))
            .ToList();
        var allRowsField = typeof(MainViewModel).GetField(
            "_allRows",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var allRows = (List<AppRowViewModel>)allRowsField!.GetValue(viewModel)!;
        allRows.AddRange(rows);
        typeof(MainViewModel).GetMethod(
                "ApplyFilter",
                BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(viewModel, null);
        var footer = string.Equals(
            localization.Language,
            "en-US",
            StringComparison.OrdinalIgnoreCase)
            ? "UI preview · sample data"
            : "界面预览 · 示例数据";
        typeof(MainViewModel).GetField(
                "_footerText",
                BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(viewModel, footer);

        var window = new MainWindow(viewModel, settingsService)
        {
            Width = 760,
            Height = 720
        };
        window.ShowSettings();

        var root = (FrameworkElement)window.Content;
        root.Measure(new Size(760, 720));
        root.Arrange(new Rect(0, 0, 760, 720));
        root.UpdateLayout();

        var bitmap = new RenderTargetBitmap(760, 720, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        using var stream = File.Create(outputPath);
        encoder.Save(stream);
        Console.WriteLine(outputPath);
    }

    private static ApplicationInfo PreviewApp(
        string name,
        string publisher,
        string current,
        string latest,
        string sourceName,
        UpdateStatus status = UpdateStatus.UpdateAvailable,
        VersionSourceKind sourceKind = VersionSourceKind.Official)
    {
        return new ApplicationInfo
        {
            Id = name,
            DisplayName = name,
            Publisher = publisher,
            CurrentVersion = current,
            LatestVersion = latest,
            OfficialSourceName = sourceName,
            SourceKind = sourceKind,
            Status = status
        };
    }
}

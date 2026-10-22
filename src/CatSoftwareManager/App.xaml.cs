using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Threading;
using CatSoftwareManager.Services;
using CatSoftwareManager.ViewModels;
using CatSoftwareManager.Views;

namespace CatSoftwareManager;

public partial class App : System.Windows.Application
{
    private const string MutexName = @"Local\CatSoftwareManager.SingleInstance";
    private const string ShowEventName = @"Local\CatSoftwareManager.Show";
    private Mutex? _instanceMutex;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _registeredWait;
    private SettingsService? _settingsService;
    private MainViewModel? _viewModel;
    private MainWindow? _flyoutWindow;
    private TrayService? _trayService;
    private UpdatePromptWindow? _promptWindow;
    private DispatcherTimer? _dailyTimer;
    private bool _isExiting;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        TraceStartup("OnStartup entered");
        _instanceMutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (!createdNew)
        {
            try
            {
                EventWaitHandle.OpenExisting(ShowEventName).Set();
            }
            catch
            {
                // If the first process is still starting, exiting is still better than a duplicate tray icon.
            }

            Shutdown();
            return;
        }

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _registeredWait = ThreadPool.RegisterWaitForSingleObject(
            _showEvent,
            (_, _) => Dispatcher.BeginInvoke(() => _flyoutWindow?.ShowFlyout()),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

        _settingsService = new SettingsService();
        var settings = _settingsService.Load();
        TraceStartup("Settings loaded");
        var localization = new LocalizationService();
        localization.SetLanguage(settings.Language);
        var themeService = new ThemeService();
        themeService.Apply(settings.Theme);
        var startupService = new StartupService(_settingsService);
        var wingetService = new WingetService(_settingsService);
        var scanService = new SoftwareScanService(
            new InstalledApplicationScanner(),
            new AppxApplicationScanner(),
            wingetService,
            new OfficialVersionService(_settingsService),
            _settingsService);
        var updateService = new UpdateService(wingetService, _settingsService);
        _viewModel = new MainViewModel(
            scanService,
            updateService,
            _settingsService,
            localization,
            startupService,
            themeService);
        _viewModel.LoadCachedScan();
        TraceStartup("ViewModel created");
        _flyoutWindow = new MainWindow(_viewModel, _settingsService);
        TraceStartup("MainWindow created");
        _flyoutWindow.Closing += (_, args) =>
        {
            if (!_isExiting)
            {
                args.Cancel = true;
                _flyoutWindow.HideFlyout();
            }
        };
        _viewModel.UpdatesFound += ShowUpdates;
        _trayService = new TrayService(
            _flyoutWindow,
            _viewModel,
            localization,
            ExitApplication);
        TraceStartup("Tray created");

        _dailyTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMinutes(5)
        };
        _dailyTimer.Tick += async (_, _) => await CheckDailyAsync();

        var isAutoStart = e.Args.Any(argument =>
            string.Equals(argument, "--autostart", StringComparison.OrdinalIgnoreCase));
        if (isAutoStart)
        {
            _dailyTimer.Start();
            await Task.Delay(1800);
            await CheckDailyAsync();
        }
        else
        {
            TraceStartup("Showing flyout");
            _flyoutWindow.ShowFlyout();
            TraceStartup("Flyout shown");
            await _viewModel.ScanAsync(automatic: false);
            TraceStartup("Initial scan finished");
        }
    }

    public void ExitApplication()
    {
        if (_isExiting)
        {
            return;
        }

        _isExiting = true;
        _settingsService?.Save();
        _promptWindow?.Close();
        _trayService?.Dispose();
        _flyoutWindow?.Close();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _isExiting = true;
        _dailyTimer?.Stop();
        _registeredWait?.Unregister(null);
        _trayService?.Dispose();
        _showEvent?.Dispose();
        if (_instanceMutex is not null)
        {
            try
            {
                _instanceMutex.ReleaseMutex();
            }
            catch
            {
                // The mutex may not be owned if startup failed.
            }

            _instanceMutex.Dispose();
        }

        base.OnExit(e);
    }

    private async Task CheckDailyAsync()
    {
        if (_viewModel is null ||
            string.Equals(
                _viewModel.Settings.LastAutoScanDate,
                DateTime.Today.ToString("yyyy-MM-dd"),
                StringComparison.Ordinal))
        {
            return;
        }

        if (System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus !=
                System.Windows.Forms.PowerLineStatus.Online ||
            !NetworkInterface.GetIsNetworkAvailable())
        {
            return;
        }

        var state = await _viewModel.TryAutomaticCheckAsync();
        if (state == AutomaticCheckState.Checked)
        {
            _dailyTimer?.Start();
            await CheckManagerUpdateAsync();
        }
    }

    private async Task CheckManagerUpdateAsync()
    {
        if (_viewModel is null ||
            string.Equals(
                _viewModel.Settings.LastManagerUpdateCheckDate,
                DateTime.Today.ToString("yyyy-MM-dd"),
                StringComparison.Ordinal))
        {
            return;
        }

        var release = await ManagerUpdateService.GetLatestReleaseAsync();
        if (release is null)
        {
            return;
        }

        _viewModel.Settings.LastManagerUpdateCheckDate = DateTime.Today.ToString("yyyy-MM-dd");
        _settingsService?.Save();
        var currentVersion = GetType().Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        if (!VersionComparer.IsNewer(currentVersion, release.Version))
        {
            return;
        }

        var result = MessageBox.Show(
            _viewModel.Localization.Format("ManagerUpdateOpen", release.Version),
            _viewModel.Localization["CheckManagerUpdate"],
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);
        if (result == MessageBoxResult.Yes)
        {
            await ManagerUpdateService.DownloadAndStartInstallerAsync(release, _viewModel);
        }
    }

    private void ShowUpdates(object? sender, IReadOnlyList<AppRowViewModel> updates)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (updates.Count == 0 || _promptWindow is { IsVisible: true })
            {
                return;
            }

            _flyoutWindow?.HideFlyout();
            _trayService?.ShowUpdatePopup(updates);
            _promptWindow = new UpdatePromptWindow(_viewModel!, updates);
            _promptWindow.Closed += (_, _) => _promptWindow = null;
            _promptWindow.Show();
            _promptWindow.Activate();
        });
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _settingsService?.WriteLog("Unhandled dispatcher exception", e.Exception);
        MessageBox.Show(
            e.Exception.Message,
            "猫猫的软件管理器",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            _settingsService?.WriteLog("Unhandled application exception", exception);
        }
    }

    private static void TraceStartup(string message)
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("CATSM_TRACE"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CatSoftwareManager",
                "startup-trace.log");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch
        {
            // Diagnostic tracing is optional.
        }
    }
}

using System.Collections.ObjectModel;
using System.Net.NetworkInformation;
using System.Windows;
using CatSoftwareManager.Infrastructure;
using CatSoftwareManager.Models;
using CatSoftwareManager.Services;
using CatSoftwareManager.Views;

namespace CatSoftwareManager.ViewModels;

public enum AutomaticCheckState
{
    Checked,
    AlreadyChecked,
    Battery,
    Offline,
    Failed
}

public sealed class MainViewModel : ObservableObject
{
    private readonly SoftwareScanService _scanService;
    private readonly UpdateService _updateService;
    private readonly SettingsService _settingsService;
    private readonly LocalizationService _localization;
    private readonly StartupService _startupService;
    private readonly ThemeService _themeService;
    private readonly AsyncRelayCommand _refreshCommand;
    private readonly AsyncRelayCommand _updateCommand;
    private readonly RelayCommand _toggleRowStateCommand;
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private readonly SemaphoreSlim _updateGate = new(1, 1);
    private readonly List<AppRowViewModel> _allRows = [];
    private string _searchText = "";
    private bool _onlyUpdates;
    private bool _isBusy;
    private string _busyMessage = "";
    private string _footerText = "";
    private DateTimeOffset? _lastCheckedAt;
    private bool _lastScanSucceeded;

    public MainViewModel(
        SoftwareScanService scanService,
        UpdateService updateService,
        SettingsService settingsService,
        LocalizationService localization,
        StartupService startupService,
        ThemeService themeService)
    {
        _scanService = scanService;
        _updateService = updateService;
        _settingsService = settingsService;
        _localization = localization;
        _startupService = startupService;
        _themeService = themeService;

        _refreshCommand = new AsyncRelayCommand(_ => ScanAsync(automatic: false), _ => !IsBusy);
        _updateCommand = new AsyncRelayCommand(
            parameter => UpdateSingleAsync(parameter as AppRowViewModel),
            parameter => parameter is AppRowViewModel row && row.CanUpdate && !IsBusy);
        _toggleRowStateCommand = new RelayCommand(
            parameter => ToggleRowState(parameter as AppRowViewModel),
            parameter => parameter is AppRowViewModel row && (row.CanIgnore || row.CanRestore));

        _localization.LanguageChanged += (_, _) =>
        {
            RefreshUiText();
            foreach (var row in _allRows)
            {
                row.Refresh();
            }
        };
        RefreshUiText();
    }

    public ObservableCollection<AppRowViewModel> VisibleApps { get; } = [];
    public IReadOnlyList<AppRowViewModel> AllRows => _allRows;
    public AppSettings Settings => _settingsService.Current;
    public LocalizationService Localization => _localization;

    public string Title => _localization["AppTitle"];
    public string Subtitle => _localization["Subtitle"];
    public string SearchHint => _localization["SearchHint"];
    public string UpdatesOnlyText => _localization["UpdatesOnly"];
    public string RefreshText => _localization["Refresh"];
    public string RefreshShortText => _localization["RefreshShort"];
    public string SettingsText => _localization["Settings"];
    public string LanguageText => _localization["Language"];
    public string ChineseText => _localization["Chinese"];
    public string EnglishText => _localization["English"];
    public string ThemeText => _localization["Theme"];
    public string SystemThemeText => _localization["SystemTheme"];
    public string LightThemeText => _localization["LightTheme"];
    public string DarkThemeText => _localization["DarkTheme"];
    public string AutoStartText => _localization["AutoStart"];
    public string CheckManagerUpdateText => _localization["CheckManagerUpdate"];
    public string VersionText => _localization.Format("Version", GetType().Assembly.GetName().Version?.ToString(3) ?? "1.0.0");
    public string OpenLogsText => _localization["OpenLogs"];
    public string EmptyText => _allRows.Count == 0
        ? _localization["NoApps"]
        : _localization["NoMatching"];

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value))
            {
                return;
            }

            ApplyFilter();
        }
    }

    public bool OnlyUpdates
    {
        get => _onlyUpdates;
        set
        {
            if (!SetProperty(ref _onlyUpdates, value))
            {
                return;
            }

            ApplyFilter();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value))
            {
                return;
            }

            _refreshCommand.RaiseCanExecuteChanged();
            _updateCommand.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(IsNotBusy));
            OnPropertyChanged(nameof(SummaryText));
        }
    }

    public bool IsNotBusy => !IsBusy;

    public string BusyMessage
    {
        get => _busyMessage;
        private set => SetProperty(ref _busyMessage, value);
    }

    public string FooterText
    {
        get => _footerText;
        private set => SetProperty(ref _footerText, value);
    }

    public string SummaryText
    {
        get
        {
            var updateCount = _allRows.Count(row => row.Status == UpdateStatus.UpdateAvailable);
            return updateCount > 0
                ? _localization.Format("UpdateAvailableCount", updateCount)
                : _localization["AllUpToDate"];
        }
    }

    public bool IsEmpty => VisibleApps.Count == 0;
    public bool HasUpdates => _allRows.Any(row => row.Status == UpdateStatus.UpdateAvailable);
    public IReadOnlyList<AppRowViewModel> UpdateRows =>
        _allRows.Where(row => row.Status == UpdateStatus.UpdateAvailable).ToArray();

    public int LanguageIndex
    {
        get => string.Equals(Settings.Language, "en-US", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        set
        {
            var language = value == 1 ? "en-US" : "zh-CN";
            if (string.Equals(Settings.Language, language, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Settings.Language = language;
            _localization.SetLanguage(language);
            _settingsService.Save();
            OnPropertyChanged();
            RefreshUiText();
            ApplyFilter();
            UiPreferencesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public int ThemeIndex
    {
        get => Settings.Theme switch
        {
            "Light" => 1,
            "Dark" => 2,
            _ => 0
        };
        set
        {
            var theme = value switch
            {
                1 => "Light",
                2 => "Dark",
                _ => "System"
            };
            if (string.Equals(Settings.Theme, theme, StringComparison.Ordinal))
            {
                return;
            }

            Settings.Theme = theme;
            _themeService.Apply(theme);
            _settingsService.Save();
            OnPropertyChanged();
            UiPreferencesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool AutoStartEnabled
    {
        get => Settings.AutoStartEnabled;
        set
        {
            if (Settings.AutoStartEnabled == value)
            {
                return;
            }

            Settings.AutoStartEnabled = value;
            _startupService.SetEnabled(value);
            _settingsService.Save();
            OnPropertyChanged();
        }
    }

    public System.Windows.Input.ICommand RefreshCommand => _refreshCommand;
    public System.Windows.Input.ICommand UpdateCommand => _updateCommand;
    public System.Windows.Input.ICommand ToggleRowStateCommand => _toggleRowStateCommand;

    public event EventHandler<IReadOnlyList<AppRowViewModel>>? UpdatesFound;
    public event EventHandler? UiPreferencesChanged;

    public async Task ScanAsync(bool automatic = false)
    {
        if (!await _scanGate.WaitAsync(0))
        {
            return;
        }

        try
        {
            IsBusy = true;
            _lastScanSucceeded = false;
            BusyMessage = _localization["Checking"];
            FooterText = BusyMessage;
            var progress = new Progress<string>(phase =>
            {
                BusyMessage = phase switch
                {
                    "installed" => _localization["ScanningInstalled"],
                    "official" => _localization["ScanningOfficial"],
                    "winget" => _localization["ScanningWinget"],
                    _ => _localization["Checking"]
                };
                FooterText = BusyMessage;
            });

            var result = await _scanService.ScanAsync(Settings, progress);
            _lastCheckedAt = result.CheckedAt;
            _allRows.Clear();
            foreach (var application in result.Applications)
            {
                _allRows.Add(new AppRowViewModel(application, _localization));
            }

            ApplyFilter();
            FooterText = $"{_localization["Ready"]} · {_localization.Format("LastChecked", result.CheckedAt.LocalDateTime.ToString("g"))}";
            _lastScanSucceeded = true;

            if (automatic && UpdateRows.Count > 0)
            {
                UpdatesFound?.Invoke(this, UpdateRows);
            }
        }
        catch (Exception ex)
        {
            _settingsService.WriteLog("Software scan failed", ex);
            FooterText = ex.Message;
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(SummaryText));
            OnPropertyChanged(nameof(HasUpdates));
            OnPropertyChanged(nameof(UpdateRows));
            _updateCommand.RaiseCanExecuteChanged();
            _scanGate.Release();
        }
    }

    public void LoadCachedScan()
    {
        var cached = _scanService.LoadCachedResult();
        if (cached is null || cached.Applications.Count == 0)
        {
            return;
        }

        _lastCheckedAt = cached.CheckedAt;
        _allRows.Clear();
        foreach (var application in cached.Applications)
        {
            _allRows.Add(new AppRowViewModel(application, _localization));
        }

        ApplyFilter();
        FooterText =
            $"{_localization["Ready"]} · " +
            _localization.Format("LastChecked", cached.CheckedAt.LocalDateTime.ToString("g"));
    }

    public async Task<AutomaticCheckState> TryAutomaticCheckAsync()
    {
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        if (string.Equals(Settings.LastAutoScanDate, today, StringComparison.Ordinal))
        {
            FooterText = _localization["DailyAlreadyChecked"];
            return AutomaticCheckState.AlreadyChecked;
        }

        if (System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus !=
            System.Windows.Forms.PowerLineStatus.Online)
        {
            FooterText = _localization["PowerOffline"];
            return AutomaticCheckState.Battery;
        }

        if (!NetworkInterface.GetIsNetworkAvailable())
        {
            FooterText = _localization["NetworkOffline"];
            return AutomaticCheckState.Offline;
        }

        await ScanAsync(automatic: true);
        if (!_lastScanSucceeded)
        {
            return AutomaticCheckState.Failed;
        }

        Settings.LastAutoScanDate = today;
        _settingsService.Save();
        return AutomaticCheckState.Checked;
    }

    public async Task<IReadOnlyList<UpdateOutcome>> UpdateRowsAsync(
        IReadOnlyList<AppRowViewModel> rows,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!await _updateGate.WaitAsync(0, cancellationToken))
        {
            return Array.Empty<UpdateOutcome>();
        }

        var outcomes = new List<UpdateOutcome>();
        try
        {
            IsBusy = true;
            foreach (var row in rows.Where(row => row.CanUpdate))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                var running = _updateService.GetRunningExecutables(row.Application);
                if (running.Count > 0)
                {
                    var proceed = false;
                    while (running.Count > 0)
                    {
                        var message = _localization.Format(
                            "RunningProcessMessage",
                            $"{row.DisplayName} ({string.Join(", ", running)})");
                        var keepWaiting = ConfirmDialogWindow.ShowRetry(
                            _localization["RunningProcessTitle"],
                            message,
                            _localization["Retry"],
                            _localization["Cancel"]);
                        if (!keepWaiting)
                        {
                            break;
                        }

                        running = _updateService.GetRunningExecutables(row.Application);
                        proceed = running.Count == 0;
                    }

                    if (!proceed)
                    {
                        continue;
                    }
                }

                BusyMessage = _localization.Format("Updating", row.DisplayName);
                progress?.Report(BusyMessage);
                var outcome = await _updateService.UpdateAsync(row.Application, null, cancellationToken);
                outcomes.Add(outcome);
            }
        }
        finally
        {
            IsBusy = false;
            _updateGate.Release();
        }

        if (outcomes.Any(outcome => outcome.Success))
        {
            await ScanAsync(automatic: false);
        }

        return outcomes;
    }

    public void Ignore(AppRowViewModel row)
    {
        Settings.IgnoredApps.Add(row.Id);
        Settings.SkippedVersions.Remove(row.Id);
        row.Application.Status = UpdateStatus.Ignored;
        _settingsService.Save();
        row.Refresh();
        AfterRowStateChanged();
    }

    public void Skip(AppRowViewModel row)
    {
        if (string.IsNullOrWhiteSpace(row.Application.LatestVersion))
        {
            return;
        }

        Settings.SkippedVersions[row.Id] = row.Application.LatestVersion;
        row.Application.Status = UpdateStatus.Skipped;
        _settingsService.Save();
        row.Refresh();
        AfterRowStateChanged();
    }

    public void Restore(AppRowViewModel row)
    {
        Settings.IgnoredApps.Remove(row.Id);
        Settings.SkippedVersions.Remove(row.Id);
        SoftwareScanService.EvaluateStatus(row.Application, Settings);

        _settingsService.Save();
        row.Refresh();
        AfterRowStateChanged();
    }

    private async Task UpdateSingleAsync(AppRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var outcomes = await UpdateRowsAsync([row]);
        if (outcomes.Any(outcome => outcome.RestartRequired))
        {
            var result = MessageBox.Show(
                _localization["RestartPrompt"],
                _localization["PromptTitle"],
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "shutdown",
                    Arguments = "/r /t 15",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            }
        }

        var failure = outcomes.FirstOrDefault(outcome => !outcome.Success && !outcome.PageOpened);
        if (failure is not null)
        {
            MessageBox.Show(
                _localization.Format("UpdateFailedDetail", failure.Application.DisplayName, failure.Message),
                _localization["PromptTitle"],
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void ToggleRowState(AppRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        if (row.CanRestore)
        {
            Restore(row);
        }
        else if (row.CanIgnore)
        {
            Ignore(row);
        }
    }

    private void AfterRowStateChanged()
    {
        ApplyFilter();
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(HasUpdates));
        OnPropertyChanged(nameof(UpdateRows));
        _toggleRowStateCommand.RaiseCanExecuteChanged();
        _updateCommand.RaiseCanExecuteChanged();
    }

    private void ApplyFilter()
    {
        VisibleApps.Clear();
        foreach (var row in _allRows
                     .Where(row => !OnlyUpdates || row.Status == UpdateStatus.UpdateAvailable)
                     .Where(row => row.Matches(SearchText)))
        {
            VisibleApps.Add(row);
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(SummaryText));
    }

    private void RefreshUiText()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(SearchHint));
        OnPropertyChanged(nameof(UpdatesOnlyText));
        OnPropertyChanged(nameof(RefreshText));
        OnPropertyChanged(nameof(RefreshShortText));
        OnPropertyChanged(nameof(SettingsText));
        OnPropertyChanged(nameof(LanguageText));
        OnPropertyChanged(nameof(ChineseText));
        OnPropertyChanged(nameof(EnglishText));
        OnPropertyChanged(nameof(ThemeText));
        OnPropertyChanged(nameof(SystemThemeText));
        OnPropertyChanged(nameof(LightThemeText));
        OnPropertyChanged(nameof(DarkThemeText));
        OnPropertyChanged(nameof(AutoStartText));
        OnPropertyChanged(nameof(CheckManagerUpdateText));
        OnPropertyChanged(nameof(VersionText));
        OnPropertyChanged(nameof(OpenLogsText));
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(FooterText));
        OnPropertyChanged(nameof(LanguageIndex));
        OnPropertyChanged(nameof(ThemeIndex));
    }
}

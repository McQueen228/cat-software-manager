using CatSoftwareManager.Infrastructure;
using CatSoftwareManager.Models;
using CatSoftwareManager.Services;

namespace CatSoftwareManager.ViewModels;

public sealed class AppRowViewModel : ObservableObject
{
    private readonly LocalizationService _localization;
    private bool _isSelected = true;

    public AppRowViewModel(ApplicationInfo application, LocalizationService localization)
    {
        Application = application;
        _localization = localization;
    }

    public ApplicationInfo Application { get; }
    public string Id => Application.Id;
    public string DisplayName => Application.DisplayName;
    public string Publisher => Application.Publisher;
    public UpdateStatus Status => Application.Status;
    public VersionSourceKind SourceKind => Application.SourceKind;
    public string CurrentVersion => string.IsNullOrWhiteSpace(Application.CurrentVersion)
        ? _localization["CurrentUnknown"]
        : Application.CurrentVersion;
    public string LatestVersion => string.IsNullOrWhiteSpace(Application.LatestVersion)
        ? _localization["LatestUnknown"]
        : Application.LatestVersion;
    public string CurrentLabel => _localization["Current"];
    public string LatestLabel => _localization["Latest"];
    public string StatusText => Application.Status switch
    {
        UpdateStatus.UpToDate => _localization["UpToDate"],
        UpdateStatus.UpdateAvailable => _localization["Available"],
        UpdateStatus.Skipped => _localization["Skipped"],
        UpdateStatus.Ignored => _localization["Ignored"],
        _ => _localization["Unknown"]
    };

    public string SourceText => Application.SourceKind switch
    {
        VersionSourceKind.Official when !string.IsNullOrWhiteSpace(Application.OfficialSourceName) =>
            $"{_localization["SourceOfficial"]} · {Application.OfficialSourceName}",
        VersionSourceKind.Official => _localization["SourceOfficial"],
        VersionSourceKind.Winget => _localization["SourceWinget"],
        _ => _localization["SourceUnknown"]
    };

    public bool CanUpdate =>
        Application.Status is UpdateStatus.UpdateAvailable or UpdateStatus.Skipped;

    public bool UsesOfficialDownloadPage =>
        Application.SourceKind == VersionSourceKind.Official &&
        !string.IsNullOrWhiteSpace(Application.WingetLatestVersion) &&
        VersionComparer.IsNewer(Application.WingetLatestVersion, Application.LatestVersion);

    public bool CanIgnore => Application.Status == UpdateStatus.UpdateAvailable;

    public bool CanRestore =>
        Application.Status is UpdateStatus.Ignored or UpdateStatus.Skipped;

    public string PrimaryActionText => Application.Status switch
    {
        UpdateStatus.UpToDate => _localization["UpToDate"],
        UpdateStatus.Ignored => _localization["Ignored"],
        UpdateStatus.Unknown => _localization["LatestUnknown"],
        _ => UsesOfficialDownloadPage
            ? _localization["OpenOfficial"]
            : _localization["Update"]
    };

    public string SecondaryActionText =>
        Application.Status == UpdateStatus.UpdateAvailable
            ? _localization["Ignore"]
            : _localization["Restore"];

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public bool Matches(string searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return true;
        }

        return DisplayName.Contains(searchText, StringComparison.CurrentCultureIgnoreCase) ||
               Publisher.Contains(searchText, StringComparison.CurrentCultureIgnoreCase) ||
               CurrentVersion.Contains(searchText, StringComparison.CurrentCultureIgnoreCase) ||
               LatestVersion.Contains(searchText, StringComparison.CurrentCultureIgnoreCase);
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(SourceKind));
        OnPropertyChanged(nameof(CurrentVersion));
        OnPropertyChanged(nameof(LatestVersion));
        OnPropertyChanged(nameof(CurrentLabel));
        OnPropertyChanged(nameof(LatestLabel));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(SourceText));
        OnPropertyChanged(nameof(CanUpdate));
        OnPropertyChanged(nameof(CanIgnore));
        OnPropertyChanged(nameof(CanRestore));
        OnPropertyChanged(nameof(UsesOfficialDownloadPage));
        OnPropertyChanged(nameof(PrimaryActionText));
        OnPropertyChanged(nameof(SecondaryActionText));
    }
}

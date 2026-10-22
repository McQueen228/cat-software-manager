using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using CatSoftwareManager.Models;
using CatSoftwareManager.ViewModels;

namespace CatSoftwareManager.Views;

public partial class UpdatePromptWindow : Window
{
    private readonly MainViewModel _mainViewModel;
    private readonly ObservableCollection<AppRowViewModel> _items;
    private bool _working;

    public UpdatePromptWindow(MainViewModel mainViewModel, IReadOnlyList<AppRowViewModel> updates)
    {
        InitializeComponent();
        _mainViewModel = mainViewModel;
        _items = new ObservableCollection<AppRowViewModel>(updates);
        DataContext = this;
        UpdateItems.ItemsSource = _items;
        PromptTitle = mainViewModel.Localization["PromptTitle"];
        PromptSummary = mainViewModel.Localization.Format("PromptSummary", _items.Count);
        ConfirmSelectedText = mainViewModel.Localization["ConfirmSelected"];
        CancelText = mainViewModel.Localization["Cancel"];
        SkipSelectedText = mainViewModel.Localization["SkipSelected"];
        IgnoreSelectedText = mainViewModel.Localization["IgnoreSelected"];
    }

    public string PromptTitle { get; }
    public string PromptSummary { get; }
    public string ConfirmSelectedText { get; }
    public string CancelText { get; }
    public string SkipSelectedText { get; }
    public string IgnoreSelectedText { get; }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private async void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        if (_working)
        {
            return;
        }

        var selected = _items.Where(item => item.IsSelected).ToArray();
        if (selected.Length == 0)
        {
            MessageBox.Show(
                _mainViewModel.Localization["NoSelected"],
                PromptTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        _working = true;
        SetControlsEnabled(false);
        var progress = new Progress<string>(text => ProgressText.Text = text);
        IReadOnlyList<UpdateOutcome> outcomes;
        try
        {
            outcomes = await _mainViewModel.UpdateRowsAsync(selected, progress);
        }
        finally
        {
            _working = false;
        }

        var successCount = outcomes.Count(outcome => outcome.Success);
        var failedCount = outcomes.Count(outcome => !outcome.Success && !outcome.PageOpened);
        var pageCount = outcomes.Count(outcome => outcome.PageOpened);
        var summary = _mainViewModel.Localization.Format(
            "UpdateSummaryDone",
            successCount,
            failedCount);
        if (pageCount > 0)
        {
            summary += Environment.NewLine + _mainViewModel.Localization["NoUpdateChannel"];
        }

        MessageBox.Show(
            summary,
            PromptTitle,
            MessageBoxButton.OK,
            failedCount > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);

        if (outcomes.Any(outcome => outcome.RestartRequired))
        {
            var restart = MessageBox.Show(
                _mainViewModel.Localization["RestartPrompt"],
                PromptTitle,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (restart == MessageBoxResult.Yes)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "shutdown",
                    Arguments = "/r /t 15",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            }
        }

        Close();
    }

    private void SkipButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _items.Where(item => item.IsSelected).ToArray())
        {
            _mainViewModel.Skip(item);
        }

        Close();
    }

    private void IgnoreButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _items.Where(item => item.IsSelected).ToArray())
        {
            _mainViewModel.Ignore(item);
            _items.Remove(item);
        }

        if (_items.Count == 0)
        {
            Close();
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_working)
        {
            Close();
        }
    }

    private void SelectionChanged(object sender, RoutedEventArgs e)
    {
        ConfirmButton.IsEnabled = _items.Any(item => item.IsSelected);
    }

    private void SetControlsEnabled(bool enabled)
    {
        ConfirmButton.IsEnabled = enabled && _items.Any(item => item.IsSelected);
        IgnoreButton.IsEnabled = enabled;
        SkipButton.IsEnabled = enabled;
    }
}

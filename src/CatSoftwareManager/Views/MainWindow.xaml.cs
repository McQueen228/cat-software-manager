using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CatSoftwareManager.Services;
using CatSoftwareManager.ViewModels;
using WinForms = System.Windows.Forms;

namespace CatSoftwareManager.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly SettingsService _settings;
    private readonly bool _autoHideEnabled =
        !string.Equals(
            Environment.GetEnvironmentVariable("CATSM_NO_AUTOHIDE"),
            "1",
            StringComparison.Ordinal);
    private bool _allowAutoHide = true;
    private bool _hasBeenPositioned;

    public MainWindow(MainViewModel viewModel, SettingsService settings)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _settings = settings;
        DataContext = viewModel;
        Loaded += (_, _) => ApplyFlyoutPosition();
    }

    public void ShowFlyout()
    {
        Trace("ShowFlyout enter");
        _allowAutoHide = true;
        if (!IsVisible)
        {
            Trace("Calling Show");
            Show();
            Trace($"After Show visible={IsVisible} hwnd={new System.Windows.Interop.WindowInteropHelper(this).Handle}");
        }

        ApplyFlyoutPosition();
        Trace($"Positioned left={Left} top={Top} actual={ActualWidth}x{ActualHeight}");
        Activate();
        Focus();
        Trace("ShowFlyout exit");
    }

    public void HideFlyout()
    {
        _allowAutoHide = false;
        Hide();
        _allowAutoHide = true;
    }

    public void ToggleFlyout()
    {
        if (IsVisible && IsActive)
        {
            HideFlyout();
        }
        else
        {
            ShowFlyout();
        }
    }

    public void TemporarilyDisableAutoHide()
    {
        _allowAutoHide = false;
    }

    public void EnableAutoHide()
    {
        _allowAutoHide = true;
    }

    public void ShowSettings()
    {
        SettingsPanel.Visibility = Visibility.Visible;
    }

    private void ApplyFlyoutPosition()
    {
        var screen = WinForms.Screen.FromPoint(WinForms.Cursor.Position);
        var area = screen.WorkingArea;
        var dpi = VisualTreeHelper.GetDpi(this);
        var width = ActualWidth > 0 ? ActualWidth : Width;
        var height = ActualHeight > 0 ? ActualHeight : Height;
        Left = area.Right / dpi.DpiScaleX - width - 12;
        Top = area.Bottom / dpi.DpiScaleY - height - 12;
        _hasBeenPositioned = true;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
            _hasBeenPositioned = true;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => HideFlyout();

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        SettingsPanel.Visibility = SettingsPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        if (_autoHideEnabled && _allowAutoHide && _hasBeenPositioned)
        {
            Hide();
            _allowAutoHide = true;
        }
    }

    private void OpenLogsButton_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_settings.LogDirectory);
        Process.Start(new ProcessStartInfo
        {
            FileName = _settings.LogDirectory,
            UseShellExecute = true
        });
    }

    private async void CheckManagerUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        await ManagerUpdateService.CheckFromUiAsync(this, _viewModel);
    }

    private static void Trace(string message)
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
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} MainWindow: {message}{Environment.NewLine}");
        }
        catch
        {
            // Diagnostic tracing is optional.
        }
    }
}

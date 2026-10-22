using System.Drawing;
using System.Windows;
using CatSoftwareManager.ViewModels;
using CatSoftwareManager.Views;
using WinForms = System.Windows.Forms;

namespace CatSoftwareManager.Services;

public sealed class TrayService : IDisposable
{
    private readonly MainWindow _mainWindow;
    private readonly MainViewModel _viewModel;
    private readonly LocalizationService _localization;
    private readonly Action _exitAction;
    private readonly WinForms.NotifyIcon _notifyIcon;
    private readonly WinForms.ContextMenuStrip _menu;
    private readonly WinForms.ToolStripMenuItem _openItem;
    private readonly WinForms.ToolStripMenuItem _checkItem;
    private readonly WinForms.ToolStripMenuItem _settingsItem;
    private readonly WinForms.ToolStripMenuItem _exitItem;

    public TrayService(
        MainWindow mainWindow,
        MainViewModel viewModel,
        LocalizationService localization,
        Action exitAction)
    {
        _mainWindow = mainWindow;
        _viewModel = viewModel;
        _localization = localization;
        _exitAction = exitAction;
        _menu = new WinForms.ContextMenuStrip
        {
            ShowImageMargin = false,
            Font = new Font("Microsoft YaHei UI", 9F)
        };
        _openItem = new WinForms.ToolStripMenuItem();
        _checkItem = new WinForms.ToolStripMenuItem();
        _settingsItem = new WinForms.ToolStripMenuItem();
        _exitItem = new WinForms.ToolStripMenuItem();
        _openItem.Click += (_, _) => _mainWindow.ShowFlyout();
        _checkItem.Click += async (_, _) =>
        {
            _mainWindow.ShowFlyout();
            await _viewModel.ScanAsync(automatic: false);
        };
        _settingsItem.Click += (_, _) =>
        {
            _mainWindow.ShowFlyout();
            _mainWindow.ShowSettings();
        };
        _exitItem.Click += (_, _) => _exitAction();
        _menu.Items.AddRange([_openItem, _checkItem, _settingsItem, new WinForms.ToolStripSeparator(), _exitItem]);

        _notifyIcon = new WinForms.NotifyIcon
        {
            Icon = LoadIcon(),
            ContextMenuStrip = _menu,
            Visible = true
        };
        _notifyIcon.MouseClick += (_, args) =>
        {
            if (args.Button == WinForms.MouseButtons.Left)
            {
                _mainWindow.ToggleFlyout();
            }
        };
        _notifyIcon.DoubleClick += (_, _) => _mainWindow.ShowFlyout();
        _localization.LanguageChanged += (_, _) => UpdateMenuText();
        UpdateMenuText();
    }

    public void ShowUpdatePopup(IReadOnlyList<AppRowViewModel> updates)
    {
        _notifyIcon.BalloonTipTitle = _localization["PromptTitle"];
        _notifyIcon.BalloonTipText = _localization.Format("UpdateAvailableCount", updates.Count);
        _notifyIcon.BalloonTipIcon = WinForms.ToolTipIcon.Info;
        _notifyIcon.ShowBalloonTip(4500);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
    }

    private void UpdateMenuText()
    {
        _notifyIcon.Text = _localization["AppTitle"];
        _openItem.Text = _localization["MenuOpen"];
        _checkItem.Text = _localization["MenuCheckNow"];
        _settingsItem.Text = _localization["MenuSettings"];
        _exitItem.Text = _localization["MenuExit"];
    }

    private static Icon LoadIcon()
    {
        try
        {
            var resource = Application.GetResourceStream(
                new Uri("pack://application:,,,/Assets/cat.ico"));
            if (resource is not null)
            {
                using var icon = new Icon(resource.Stream);
                return (Icon)icon.Clone();
            }
        }
        catch
        {
            // Fall back to the generic application icon below.
        }

        return (Icon)SystemIcons.Application.Clone();
    }
}

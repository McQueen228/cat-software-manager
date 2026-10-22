using System.Windows;

namespace CatSoftwareManager.Views;

public partial class ConfirmDialogWindow : Window
{
    private ConfirmDialogWindow(
        string title,
        string message,
        string primaryText,
        string secondaryText)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        PrimaryButton.Content = primaryText;
        SecondaryButton.Content = secondaryText;
    }

    public static bool ShowRetry(string title, string message, string retryText, string cancelText)
    {
        var dialog = new ConfirmDialogWindow(title, message, retryText, cancelText);
        var activeWindow = Application.Current.Windows
            .OfType<Window>()
            .FirstOrDefault(window => window.IsActive);
        if (activeWindow is not null && activeWindow != dialog)
        {
            dialog.Owner = activeWindow;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        return dialog.ShowDialog() == true;
    }

    private void PrimaryButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void SecondaryButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

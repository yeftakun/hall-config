using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;

namespace HallConfig.App.Views;

public partial class AboutWindow : Window
{
    private static AboutWindow? _instance;

    public static void ShowWindow(Window? owner = null)
    {
        if (_instance != null)
        {
            HallConfig.Core.Logger.Info("App", "Bringing existing About window to front");
            if (_instance.WindowState == WindowState.Minimized)
                _instance.WindowState = WindowState.Normal;
            _instance.Activate();
        }
        else
        {
            HallConfig.Core.Logger.Info("App", "Opening new About window");
            _instance = new AboutWindow();
            if (owner != null)
                _instance.Owner = owner;
            _instance.Closed += (s, e) => _instance = null;
            _instance.Show();
        }
    }

    public AboutWindow()
    {
        InitializeComponent();
        
        var lastResult = HallConfig.Core.UpdateChecker.LastCheckResult;
        if (lastResult != null && !lastResult.IsError && lastResult.HasUpdate)
        {
            UpdateUIWithResult(lastResult);
        }
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch { }
        e.Handled = true;
    }

    private string? _releaseUrl;

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_releaseUrl))
        {
            // If already checked and has update, button acts as link
            try
            {
                Process.Start(new ProcessStartInfo(_releaseUrl) { UseShellExecute = true });
            }
            catch { }
            return;
        }

        CheckUpdateButton.IsEnabled = false;
        CheckUpdateButton.Content = "Checking...";
        UpdateStatusText.Visibility = Visibility.Collapsed;

        var result = await HallConfig.Core.UpdateChecker.CheckForUpdatesAsync();
        UpdateUIWithResult(result);
    }

    private void UpdateUIWithResult(HallConfig.Core.UpdateCheckResult result)
    {
        if (result.IsError)
        {
            CheckUpdateButton.Content = "Check for Update";
            CheckUpdateButton.IsEnabled = true;
            UpdateStatusText.Text = "Failed to check for updates.";
            UpdateStatusText.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F85149"));
            UpdateStatusText.Visibility = Visibility.Visible;
        }
        else if (result.HasUpdate)
        {
            _releaseUrl = result.ReleaseUrl;
            CheckUpdateButton.Content = $"Update Available: v{result.LatestVersion}";
            CheckUpdateButton.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#58A6FF"));
            CheckUpdateButton.IsEnabled = true;
            UpdateStatusText.Text = $"Current version: v{result.CurrentVersion}";
            UpdateStatusText.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#8B949E"));
            UpdateStatusText.Visibility = Visibility.Visible;
        }
        else
        {
            CheckUpdateButton.Content = "Check for Update";
            CheckUpdateButton.IsEnabled = true;
            UpdateStatusText.Text = $"You're up to date (v{result.CurrentVersion})";
            UpdateStatusText.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#3FB950"));
            UpdateStatusText.Visibility = Visibility.Visible;
        }
    }

    private void OpenLogsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string logDir = HallConfig.Core.Logger.LogDirectory;
            if (System.IO.Directory.Exists(logDir))
            {
                Process.Start(new ProcessStartInfo(logDir) { UseShellExecute = true });
            }
        }
        catch { }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

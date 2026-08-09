using System;
using System.Linq;
using System.Reflection;
using System.Windows;
using HallConfig.Core;
using WpfApplication = System.Windows.Application;

namespace HallConfig.App;

public partial class App : WpfApplication
{
    private MainWindow? _mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            Logger.Error("App", "AppDomain UnhandledException", args.ExceptionObject as Exception);
        };

        DispatcherUnhandledException += (s, args) =>
        {
            Logger.Error("App", "Dispatcher UnhandledException", args.Exception);
            args.Handled = false;
        };

        string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown";
        Logger.Info("App", $"HallConfig v{version} starting...");

        bool startMinimized = e.Args.Any(a => 
            a.Equals("--minimized", StringComparison.OrdinalIgnoreCase) || 
            a.Equals("/minimized", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("--autostart", StringComparison.OrdinalIgnoreCase));

        _mainWindow = new MainWindow();
        MainWindow = _mainWindow;

        if (startMinimized || _mainWindow.ViewModel.AppConfig.StartMinimized)
        {
            // Auto-start pipeline when launched in background / startup
            if (!_mainWindow.ViewModel.IsPipelineRunning)
            {
                _mainWindow.ViewModel.TogglePipelineCommand.Execute(null);
            }
            _mainWindow.TrayService.ShowNotification("HallConfig", "Berjalan di latar belakang (System Tray). Pipeline aktif.");
        }
        else
        {
            _mainWindow.Show();
        }

        if (_mainWindow.ViewModel.AppConfig.AutoCheckUpdates)
        {
            _ = CheckForUpdatesInBackground();
        }
    }

    private async System.Threading.Tasks.Task CheckForUpdatesInBackground()
    {
        try
        {
            var result = await UpdateChecker.CheckForUpdatesAsync();
            if (!result.IsError && result.HasUpdate && _mainWindow != null)
            {
                _mainWindow.TrayService.ShowNotification("Update Available", 
                    $"HallConfig v{result.LatestVersion} is now available!\nClick About to download.", 
                    System.Windows.Forms.ToolTipIcon.Info);
            }
        }
        catch { }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        Logger.Info("App", "HallConfig shutting down.");
        base.OnExit(e);
    }
}

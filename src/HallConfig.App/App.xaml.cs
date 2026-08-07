using System;
using System.Linq;
using System.Windows;
using WpfApplication = System.Windows.Application;

namespace HallConfig.App;

public partial class App : WpfApplication
{
    private MainWindow? _mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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
    }
}

using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Forms;
using HallConfig.App.ViewModels;
using HallConfig.App.Views;
using HallConfig.Core.Models;
using HallConfig.Core.Services;
using Application = System.Windows.Application;

namespace HallConfig.App.Services;

public class TrayService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly MainViewModel _viewModel;
    private readonly Window _mainWindow;
    private readonly ToolStripMenuItem _pipelineToggleItem;
    private readonly ToolStripMenuItem _runOnStartupItem;
    private readonly ToolStripMenuItem _startMinimizedItem;
    private readonly ToolStripMenuItem _minimizeOnCloseItem;
    private bool _disposed;

    public TrayService(MainViewModel viewModel, Window mainWindow)
    {
        _viewModel = viewModel;
        _mainWindow = mainWindow;

        _notifyIcon = new NotifyIcon
        {
            Text = "HallConfig - Controller Signal Processor",
            Visible = true
        };

        // Load application icon
        try
        {
            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "icon.ico");
            if (!File.Exists(iconPath))
            {
                iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "app.ico");
            }

            if (File.Exists(iconPath))
            {
                _notifyIcon.Icon = new Icon(iconPath);
            }
            else
            {
                // Fallback to system application icon
                _notifyIcon.Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? "") ?? SystemIcons.Application;
            }
        }
        catch
        {
            _notifyIcon.Icon = SystemIcons.Application;
        }

        // Context Menu
        var contextMenu = new ContextMenuStrip();

        var openItem = new ToolStripMenuItem("Open HallConfig")
        {
            Font = new Font(contextMenu.Font, System.Drawing.FontStyle.Bold)
        };
        openItem.Click += (s, e) => ShowMainWindow();
        contextMenu.Items.Add(openItem);

        _pipelineToggleItem = new ToolStripMenuItem("Start Pipeline");
        _pipelineToggleItem.Click += (s, e) => _viewModel.TogglePipelineCommand.Execute(null);
        contextMenu.Items.Add(_pipelineToggleItem);

        contextMenu.Items.Add(new ToolStripSeparator());

        _runOnStartupItem = new ToolStripMenuItem("Run on Windows Startup")
        {
            CheckOnClick = true,
            Checked = StartupHelper.IsRunOnStartupEnabled()
        };
        _runOnStartupItem.Click += OnRunOnStartupToggled;
        contextMenu.Items.Add(_runOnStartupItem);

        _startMinimizedItem = new ToolStripMenuItem("Start Minimized to Tray")
        {
            CheckOnClick = true,
            Checked = _viewModel.AppConfig.StartMinimized
        };
        _startMinimizedItem.Click += OnStartMinimizedToggled;
        contextMenu.Items.Add(_startMinimizedItem);

        _minimizeOnCloseItem = new ToolStripMenuItem("Minimize to Tray on Close (X)")
        {
            CheckOnClick = true,
            Checked = _viewModel.AppConfig.MinimizeToTrayOnClose
        };
        _minimizeOnCloseItem.Click += OnMinimizeOnCloseToggled;
        contextMenu.Items.Add(_minimizeOnCloseItem);

        contextMenu.Items.Add(new ToolStripSeparator());

        var aboutItem = new ToolStripMenuItem("About HallConfig...");
        aboutItem.Click += (s, e) => ShowAboutWindow();
        contextMenu.Items.Add(aboutItem);

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (s, e) => ExitApplication();
        contextMenu.Items.Add(exitItem);

        _notifyIcon.ContextMenuStrip = contextMenu;
        _notifyIcon.DoubleClick += (s, e) => ShowMainWindow();

        // Subscribe to view model changes to update menu / tooltip
        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsPipelineRunning) ||
                e.PropertyName == nameof(MainViewModel.PipelineRateText))
            {
                UpdateTrayStatus();
            }
        };

        UpdateTrayStatus();
    }

    private void UpdateTrayStatus()
    {
        if (_pipelineToggleItem != null)
        {
            _pipelineToggleItem.Text = _viewModel.IsPipelineRunning ? "⏹ Stop Pipeline" : "▶ Start Pipeline";
        }

        string tooltip = _viewModel.IsPipelineRunning
            ? $"HallConfig: Active ({_viewModel.PipelineRateText})"
            : "HallConfig: Stopped";

        if (tooltip.Length >= 64) tooltip = tooltip.Substring(0, 63);
        _notifyIcon.Text = tooltip;
    }

    public void ShowMainWindow()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            _mainWindow.Show();
            if (_mainWindow.WindowState == WindowState.Minimized)
            {
                _mainWindow.WindowState = WindowState.Normal;
            }
            _mainWindow.Activate();
            _mainWindow.Focus();
        });
    }

    public void ShowAboutWindow()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var about = new AboutWindow
            {
                Owner = _mainWindow.IsVisible ? _mainWindow : null
            };
            about.ShowDialog();
        });
    }

    private void OnRunOnStartupToggled(object? sender, EventArgs e)
    {
        bool enable = _runOnStartupItem.Checked;
        _viewModel.AppConfig.RunOnStartup = enable;
        StartupHelper.SetRunOnStartup(enable, _viewModel.AppConfig.StartMinimized);
        _viewModel.SaveConfigCommand.Execute(null);
    }

    private void OnStartMinimizedToggled(object? sender, EventArgs e)
    {
        _viewModel.AppConfig.StartMinimized = _startMinimizedItem.Checked;
        if (_viewModel.AppConfig.RunOnStartup)
        {
            StartupHelper.SetRunOnStartup(true, _viewModel.AppConfig.StartMinimized);
        }
        _viewModel.SaveConfigCommand.Execute(null);
    }

    private void OnMinimizeOnCloseToggled(object? sender, EventArgs e)
    {
        _viewModel.AppConfig.MinimizeToTrayOnClose = _minimizeOnCloseItem.Checked;
        _viewModel.SaveConfigCommand.Execute(null);
    }

    public void ShowNotification(string title, string message, ToolTipIcon icon = ToolTipIcon.Info)
    {
        _notifyIcon.ShowBalloonTip(3000, title, message, icon);
    }

    public void ExitApplication()
    {
        _notifyIcon.Visible = false;
        Application.Current.Dispatcher.Invoke(() =>
        {
            _viewModel.Cleanup();
            Application.Current.Shutdown();
        });
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}

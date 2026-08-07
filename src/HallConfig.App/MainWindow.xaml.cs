using System;
using System.ComponentModel;
using System.Windows;
using HallConfig.App.Services;
using HallConfig.App.ViewModels;
using HallConfig.App.Views;

namespace HallConfig.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly TrayService _trayService;
    private bool _isExplicitExit;

    public MainViewModel ViewModel => _viewModel;
    public TrayService TrayService => _trayService;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new MainViewModel();
        DataContext = _viewModel;

        _trayService = new TrayService(_viewModel, this);

        StateChanged += OnWindowStateChanged;
        Closing += OnWindowClosing;
        Closed += OnWindowClosed;
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && _viewModel.AppConfig.MinimizeToTrayOnClose)
        {
            Hide();
        }
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (!_isExplicitExit && _viewModel.AppConfig.MinimizeToTrayOnClose)
        {
            e.Cancel = true;
            Hide();
            _trayService.ShowNotification("HallConfig", "Aplikasi disembunyikan ke System Tray. Double-click icon tray untuk membuka kembali.");
        }
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        _trayService.Dispose();
        _viewModel.Dispose();
    }

    private void AboutButton_Click(object sender, RoutedEventArgs e)
    {
        _trayService.ShowAboutWindow();
    }

    public void ForceClose()
    {
        _isExplicitExit = true;
        Close();
    }
}
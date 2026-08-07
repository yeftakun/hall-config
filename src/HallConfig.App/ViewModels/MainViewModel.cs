using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Threading;
using HallConfig.Core;
using HallConfig.Core.Models;

namespace HallConfig.App.ViewModels;

/// <summary>
/// Main ViewModel. Pure of WPF visual types.
/// Supports 4 independent Axis Cards (RT, LT, LX, LY) with real-time RAW and OUTPUT meters.
/// Clicking any axis card selects that axis for tuning & vJoy output.
/// </summary>
public class MainViewModel : INotifyPropertyChanged, IDisposable
{
    // ─── Infrastructure ───────────────────────────────────────────────────────
    private readonly ConfigStore _configStore;
    private AppConfig _appConfig;
    private readonly PipelineEngine _engine;
    private readonly DispatcherTimer _uiTimer;
    private bool _disposed;

    // ── Latest pipeline values for all 4 axes (written on worker thread via volatile) ──
    private volatile float _rtRaw, _rtProc;
    private volatile bool  _rtActive;

    private volatile float _ltRaw, _ltProc;
    private volatile bool  _ltActive;

    private volatile float _lxRaw, _lxProc;
    private volatile bool  _lxActive;

    private volatile float _lyRaw, _lyProc;
    private volatile bool  _lyActive;

    // ── Rate measurement via engine snapshot ──
    private long _lastIterSnapshot;
    private int  _displayedRate;
    private DateTime _rateResetTime = DateTime.UtcNow;

    // ─── Axis Cards ───────────────────────────────────────────────────────────
    public AxisCardViewModel RtCard { get; }
    public AxisCardViewModel LtCard { get; }
    public AxisCardViewModel LxCard { get; }
    public AxisCardViewModel LyCard { get; }
    public IReadOnlyList<AxisCardViewModel> AllCards { get; }

    // ─── Constructor ──────────────────────────────────────────────────────────
    public MainViewModel()
    {
        _configStore = new ConfigStore();
        _appConfig   = _configStore.Load();
        _engine      = new PipelineEngine(_appConfig);

        // Axis cards setup
        LxCard = new AxisCardViewModel("LeftStickX",   "Left Stick X",  "LX", "Target: vJoy Axle 1 (X) • Steering", SelectAxis);
        RtCard = new AxisCardViewModel("RightTrigger", "Right Trigger", "RT", "Target: vJoy Axle 2 (Y) • Throttle", SelectAxis);
        LtCard = new AxisCardViewModel("LeftTrigger",  "Left Trigger",  "LT", "Target: vJoy Axle 3 (Z) • Brake", SelectAxis);
        LyCard = new AxisCardViewModel("LeftStickY",   "Left Stick Y",  "LY", "Target: vJoy Axle 4 (Rx) • Pitch", SelectAxis);
        AllCards = new[] { LxCard, RtCard, LtCard, LyCard };

        // Initialize cards config summaries
        RefreshAllCardsConfigSummary();

        // StatusMessage: very rare, non-blocking BeginInvoke is fine
        _engine.StatusMessage           += msg => AppDispatcher.BeginInvoke(() => StatusMessage = msg);
        // MultiAxisSampleProcessed: HOT PATH on worker thread – only update volatile fields
        _engine.MultiAxisSampleProcessed += OnMultiAxisSampleProcessed_WorkerThread;

        _selectedAxisSource = _appConfig.AxisSource;
        UpdateCardSelection();
        SyncUiFromCurrentAxisConfig();

        // Commands
        TogglePipelineCommand = new RelayCommand(TogglePipeline);
        SaveConfigCommand     = new RelayCommand(SaveConfig);
        LoadConfigCommand     = new RelayCommand(LoadConfig);

        // UI refresh timer — runs on UI thread at 60 Hz
        _uiTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(1000.0 / 60)
        };
        _uiTimer.Tick += OnUiTimerTick;
        _uiTimer.Start();
    }

    private static Dispatcher AppDispatcher =>
        System.Windows.Application.Current.Dispatcher;

    // ─── Hot-path: runs on pipeline WORKER thread at ~250 Hz ──────────────────
    private void OnMultiAxisSampleProcessed_WorkerThread(MultiAxisSample sample)
    {
        _rtRaw    = sample.RightTrigger.RawValue;
        _rtProc   = sample.RightTrigger.ProcessedValue;
        _rtActive = sample.RightTrigger.IsActive;

        _ltRaw    = sample.LeftTrigger.RawValue;
        _ltProc   = sample.LeftTrigger.ProcessedValue;
        _ltActive = sample.LeftTrigger.IsActive;

        _lxRaw    = sample.LeftStickX.RawValue;
        _lxProc   = sample.LeftStickX.ProcessedValue;
        _lxActive = sample.LeftStickX.IsActive;

        _lyRaw    = sample.LeftStickY.RawValue;
        _lyProc   = sample.LeftStickY.ProcessedValue;
        _lyActive = sample.LeftStickY.IsActive;
    }

    // ─── UI timer: runs on UI thread at 60 Hz ─────────────────────────────────
    private void OnUiTimerTick(object? sender, EventArgs e)
    {
        // 1. Rate from engine's Interlocked counter
        var   now     = DateTime.UtcNow;
        double elapsed = (now - _rateResetTime).TotalSeconds;
        if (elapsed >= 1.0)
        {
            long snap   = _engine.TakeIterationSnapshot();
            long delta  = snap - _lastIterSnapshot;
            _lastIterSnapshot = snap;
            _displayedRate    = (int)Math.Round(delta / elapsed);
            _rateResetTime    = now;
            OnPropertyChanged(nameof(PipelineRateText));
        }

        // 2. Push volatile readings into Axis Cards
        bool running = IsPipelineRunning;

        RtCard.RawValue       = _rtRaw;
        RtCard.ProcessedValue = _rtProc;
        RtCard.IsActive       = _rtActive && running;

        LtCard.RawValue       = _ltRaw;
        LtCard.ProcessedValue = _ltProc;
        LtCard.IsActive       = _ltActive && running;

        LxCard.RawValue       = _lxRaw;
        LxCard.ProcessedValue = _lxProc;
        LxCard.IsActive       = _lxActive && running;

        LyCard.RawValue       = _lyRaw;
        LyCard.ProcessedValue = _lyProc;
        LyCard.IsActive       = _lyActive && running;

        // 3. Controller status
        bool connected = _engine.Reader.IsGamepadConnected;
        if (connected != IsControllerConnected)
        {
            IsControllerConnected = connected;
            ControllerStatus = connected ? "PAD#0: Connected" : "No Controller Detected";
        }
    }

    // ─── Axis Selection ───────────────────────────────────────────────────────
    public void SelectAxis(string axisKey)
    {
        if (string.IsNullOrWhiteSpace(axisKey)) return;
        SelectedAxisSource = axisKey;
    }

    private void UpdateCardSelection()
    {
        foreach (var card in AllCards)
        {
            card.IsSelected = string.Equals(card.Key, _selectedAxisSource, StringComparison.OrdinalIgnoreCase);
        }
        OnPropertyChanged(nameof(SelectedAxisTitle));
        OnPropertyChanged(nameof(SelectedAxisSubtitle));
    }

    private void RefreshAllCardsConfigSummary()
    {
        foreach (var card in AllCards)
        {
            var cfg = _appConfig.GetAxisConfig(card.Key);
            card.UpdateConfigSummary(cfg);
        }
    }

    public AppConfig AppConfig => _appConfig;

    public void Cleanup()
    {
        Dispose();
    }

    // ─── Bound properties ─────────────────────────────────────────────────────

    private bool _isPipelineRunning;
    public bool IsPipelineRunning
    {
        get => _isPipelineRunning;
        private set
        {
            if (SetField(ref _isPipelineRunning, value))
            {
                OnPropertyChanged(nameof(PipelineButtonText));
                OnPropertyChanged(nameof(PipelineRateText));
            }
        }
    }

    public string PipelineButtonText => IsPipelineRunning ? "⏹  Stop Pipeline" : "▶  Start Pipeline";

    private bool _isControllerConnected;
    public bool IsControllerConnected
    {
        get => _isControllerConnected;
        private set => SetField(ref _isControllerConnected, value);
    }

    private string _controllerStatus = "Checking Controller...";
    public string ControllerStatus
    {
        get => _controllerStatus;
        private set => SetField(ref _controllerStatus, value);
    }

    public string PipelineRateText => IsPipelineRunning ? $"{_displayedRate} Hz" : "— Hz";

    // ── Config-bound properties ──────────────────────────────────────────────

    private string _selectedAxisSource = "RightTrigger";
    public string SelectedAxisSource
    {
        get => _selectedAxisSource;
        set
        {
            if (SetField(ref _selectedAxisSource, value))
            {
                _appConfig.AxisSource = value;
                UpdateCardSelection();
                SyncUiFromCurrentAxisConfig();
                _engine.UpdateConfig(_appConfig);
            }
        }
    }

    public string SelectedAxisTitle => _selectedAxisSource switch
    {
        "RightTrigger" => "Right Trigger (RT)",
        "LeftTrigger"  => "Left Trigger (LT)",
        "LeftStickX"   => "Left Stick X (LX)",
        "LeftStickY"   => "Left Stick Y (LY)",
        _              => _selectedAxisSource
    };

    public string SelectedAxisSubtitle => _selectedAxisSource switch
    {
        "RightTrigger" => "Routing -> vJoy Axle 2 (Y) [Throttle]",
        "LeftTrigger"  => "Routing -> vJoy Axle 3 (Z) [Brake]",
        "LeftStickX"   => "Routing -> vJoy Axle 1 (X) [Steering]",
        "LeftStickY"   => "Routing -> vJoy Axle 4 (Rx) [Pitch]",
        _              => "Routing -> vJoy Output"
    };

    private bool _smoothingEnabled;
    public bool SmoothingEnabled
    {
        get => _smoothingEnabled;
        set
        {
            if (SetField(ref _smoothingEnabled, value))
            {
                CurrentAxisConfig.Smoothing.Enabled = value;
                _engine.UpdateConfig(_appConfig);
                CurrentSelectedCard?.UpdateConfigSummary(CurrentAxisConfig);
            }
        }
    }

    private double _alpha;
    public double Alpha
    {
        get => _alpha;
        set
        {
            if (SetField(ref _alpha, value))
            {
                CurrentAxisConfig.Smoothing.Alpha = (float)value;
                _engine.UpdateConfig(_appConfig);
                CurrentSelectedCard?.UpdateConfigSummary(CurrentAxisConfig);
            }
        }
    }

    private bool _hysteresisEnabled;
    public bool HysteresisEnabled
    {
        get => _hysteresisEnabled;
        set
        {
            if (SetField(ref _hysteresisEnabled, value))
            {
                CurrentAxisConfig.Hysteresis.Enabled = value;
                _engine.UpdateConfig(_appConfig);
                CurrentSelectedCard?.UpdateConfigSummary(CurrentAxisConfig);
            }
        }
    }

    private double _thresholdUp;
    public double ThresholdUp
    {
        get => _thresholdUp;
        set
        {
            double clamped = Math.Clamp(value, _thresholdDown + 0.01, 0.99);
            if (SetField(ref _thresholdUp, clamped))
            {
                CurrentAxisConfig.Hysteresis.ThresholdUp = (float)clamped;
                _engine.UpdateConfig(_appConfig);
                CurrentSelectedCard?.UpdateConfigSummary(CurrentAxisConfig);
            }
        }
    }

    private double _thresholdDown;
    public double ThresholdDown
    {
        get => _thresholdDown;
        set
        {
            double clamped = Math.Clamp(value, 0.01, _thresholdUp - 0.01);
            if (SetField(ref _thresholdDown, clamped))
            {
                CurrentAxisConfig.Hysteresis.ThresholdDown = (float)clamped;
                _engine.UpdateConfig(_appConfig);
                CurrentSelectedCard?.UpdateConfigSummary(CurrentAxisConfig);
            }
        }
    }

    private string _statusMessage = "Ready — select an axis card to tune, then Start.";
    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    // Commands
    public RelayCommand TogglePipelineCommand { get; }
    public RelayCommand SaveConfigCommand     { get; }
    public RelayCommand LoadConfigCommand     { get; }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private AxisProcessorConfig CurrentAxisConfig =>
        _appConfig.GetAxisConfig(_selectedAxisSource);

    private AxisCardViewModel? CurrentSelectedCard => _selectedAxisSource switch
    {
        "RightTrigger" => RtCard,
        "LeftTrigger"  => LtCard,
        "LeftStickX"   => LxCard,
        "LeftStickY"   => LyCard,
        _              => null
    };

    private void SyncUiFromCurrentAxisConfig()
    {
        var cfg = _appConfig.GetAxisConfig(_selectedAxisSource);

        _smoothingEnabled   = cfg.Smoothing.Enabled;
        _alpha              = cfg.Smoothing.Alpha;
        _hysteresisEnabled  = cfg.Hysteresis.Enabled;
        _thresholdUp        = cfg.Hysteresis.ThresholdUp;
        _thresholdDown      = cfg.Hysteresis.ThresholdDown;

        OnPropertyChanged(nameof(SmoothingEnabled));
        OnPropertyChanged(nameof(Alpha));
        OnPropertyChanged(nameof(HysteresisEnabled));
        OnPropertyChanged(nameof(ThresholdUp));
        OnPropertyChanged(nameof(ThresholdDown));
    }

    // ─── Command handlers ─────────────────────────────────────────────────────

    private void TogglePipeline()
    {
        if (IsPipelineRunning)
        {
            try
            {
                _engine.Stop();
                IsPipelineRunning = false;
                StatusMessage = "Pipeline stopped.";
            }
            catch (Exception ex) { StatusMessage = $"Stop error: {ex.Message}"; }
        }
        else
        {
            try
            {
                _lastIterSnapshot = _engine.TakeIterationSnapshot();
                _rateResetTime    = DateTime.UtcNow;

                bool started = _engine.Start();
                IsPipelineRunning = started;
                StatusMessage = started ? "Pipeline active — sending to vJoy." : "Failed to start — check vJoy.";
            }
            catch (Exception ex) { StatusMessage = $"Start error: {ex.Message}"; }
        }
    }

    private void SaveConfig()
    {
        try
        {
            _configStore.Save(_appConfig);
            RefreshAllCardsConfigSummary();
            StatusMessage = $"Saved to {System.IO.Path.GetFileName(_configStore.FilePath)}";
        }
        catch (Exception ex) { StatusMessage = $"Save error: {ex.Message}"; }
    }

    private void LoadConfig()
    {
        try
        {
            bool wasRunning = IsPipelineRunning;
            if (wasRunning) TogglePipeline();

            _appConfig = _configStore.Load();
            _engine.UpdateConfig(_appConfig);
            _selectedAxisSource = _appConfig.AxisSource;
            UpdateCardSelection();
            SyncUiFromCurrentAxisConfig();
            RefreshAllCardsConfigSummary();

            if (wasRunning) TogglePipeline();
            StatusMessage = "Config loaded.";
        }
        catch (Exception ex) { StatusMessage = $"Load error: {ex.Message}"; }
    }

    // ─── INotifyPropertyChanged ───────────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    // ─── IDisposable ──────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (!_disposed)
        {
            _uiTimer.Stop();
            _engine.MultiAxisSampleProcessed -= OnMultiAxisSampleProcessed_WorkerThread;
            _engine.Dispose();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}

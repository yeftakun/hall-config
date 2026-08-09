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
/// Supports dual output mode: Native XInput Virtual Xbox 360 (ViGEm) and DirectInput (vJoy).
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
        LxCard = new AxisCardViewModel("LeftStickX",   "Left Stick X",  "LX", "", SelectAxis);
        RtCard = new AxisCardViewModel("RightTrigger", "Right Trigger", "RT", "", SelectAxis);
        LtCard = new AxisCardViewModel("LeftTrigger",  "Left Trigger",  "LT", "", SelectAxis);
        LyCard = new AxisCardViewModel("LeftStickY",   "Left Stick Y",  "LY", "", SelectAxis);
        AllCards = new[] { LxCard, RtCard, LtCard, LyCard };

        RefreshCardSubtitles();
        RefreshAllCardsConfigSummary();

        // StatusMessage: very rare, non-blocking BeginInvoke is fine
        _engine.StatusMessage           += msg => AppDispatcher.BeginInvoke(() => StatusMessage = msg);
        // MultiAxisSampleProcessed: HOT PATH on worker thread – only update volatile fields
        _engine.MultiAxisSampleProcessed += OnMultiAxisSampleProcessed_WorkerThread;

        _selectedAxisSource = _appConfig.AxisSource;
        UpdateCardSelection();
        SyncUiFromCurrentAxisConfig();

        // Commands
        TogglePipelineCommand   = new RelayCommand(TogglePipeline);
        SaveConfigCommand       = new RelayCommand(SaveConfig);
        LoadConfigCommand       = new RelayCommand(LoadConfig);
        SetXbox360ModeCommand   = new RelayCommand(() => OutputMode = "Xbox360");
        SetVJoyModeCommand      = new RelayCommand(() => OutputMode = "vJoy");

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
            IsVibrationSupported = connected && XInputHelper.SupportsVibration(_appConfig.DeviceIndex);
        }
    }

    // ─── Axis Selection & Subtitles ───────────────────────────────────────────
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
        OnPropertyChanged(nameof(ShowRTVibrationToggle));
        OnPropertyChanged(nameof(ShowLTVibrationToggle));
    }

    private void RefreshCardSubtitles()
    {
        bool isXbox = IsXbox360Mode;
        LxCard.Subtitle = isXbox ? "Target: Virtual Xbox LX • Steering" : "Target: vJoy Axle 1 (X) • Steering";
        RtCard.Subtitle = isXbox ? "Target: Virtual Xbox RT • Throttle" : "Target: vJoy Axle 2 (Y) • Throttle";
        LtCard.Subtitle = isXbox ? "Target: Virtual Xbox LT • Brake"    : "Target: vJoy Axle 3 (Z) • Brake";
        LyCard.Subtitle = isXbox ? "Target: Virtual Xbox LY • Pitch"    : "Target: vJoy Axle 4 (Rx) • Pitch";

        OnPropertyChanged(nameof(SelectedAxisSubtitle));
        OnPropertyChanged(nameof(TargetOutputBadgeText));
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

    private bool _isVibrationSupported;
    public bool IsVibrationSupported
    {
        get => _isVibrationSupported;
        private set => SetField(ref _isVibrationSupported, value);
    }

    public string PipelineRateText => IsPipelineRunning ? $"{_displayedRate} Hz" : "— Hz";

    // ─── Output Mode Selection ───────────────────────────────────────────────

    public string OutputMode
    {
        get => _appConfig.OutputMode ?? "Xbox360";
        set
        {
            if (!string.Equals(_appConfig.OutputMode, value, StringComparison.OrdinalIgnoreCase))
            {
                _appConfig.OutputMode = value;
                _engine.UpdateConfig(_appConfig);
                RefreshCardSubtitles();
                OnPropertyChanged(nameof(OutputMode));
                OnPropertyChanged(nameof(IsXbox360Mode));
                OnPropertyChanged(nameof(IsVJoyMode));
                OnPropertyChanged(nameof(OutputModeDescription));
                OnPropertyChanged(nameof(TargetOutputBadgeText));
            }
        }
    }

    public bool IsXbox360Mode
    {
        get => !string.Equals(_appConfig.OutputMode, "vJoy", StringComparison.OrdinalIgnoreCase);
        set
        {
            if (value) OutputMode = "Xbox360";
        }
    }

    public bool IsVJoyMode
    {
        get => string.Equals(_appConfig.OutputMode, "vJoy", StringComparison.OrdinalIgnoreCase);
        set
        {
            if (value) OutputMode = "vJoy";
        }
    }

    public string OutputModeDescription => IsXbox360Mode
        ? "Native XInput Virtual Controller (ViGEm) — Ideal for Assetto Corsa & modern games"
        : "Generic DirectInput Device (vJoy) — Classic compatibility mode";

    public string TargetOutputBadgeText => IsXbox360Mode ? "🎯 TARGET XBOX 360" : "🎯 TARGET vJOY";

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
        "RightTrigger" => IsXbox360Mode ? "Routing -> Virtual Xbox RT [Throttle]" : "Routing -> vJoy Axle 2 (Y) [Throttle]",
        "LeftTrigger"  => IsXbox360Mode ? "Routing -> Virtual Xbox LT [Brake]"    : "Routing -> vJoy Axle 3 (Z) [Brake]",
        "LeftStickX"   => IsXbox360Mode ? "Routing -> Virtual Xbox LX [Steering]" : "Routing -> vJoy Axle 1 (X) [Steering]",
        "LeftStickY"   => IsXbox360Mode ? "Routing -> Virtual Xbox LY [Pitch]"    : "Routing -> vJoy Axle 4 (Rx) [Pitch]",
        _              => IsXbox360Mode ? "Routing -> Virtual Xbox 360"           : "Routing -> vJoy Output"
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

    private string _statusMessage = "Ready — select an axis card to tune, choose output mode, then Start.";
    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    // Commands
    public RelayCommand TogglePipelineCommand { get; }
    public RelayCommand SaveConfigCommand     { get; }
    public RelayCommand LoadConfigCommand     { get; }
    public RelayCommand SetXbox360ModeCommand { get; }
    public RelayCommand SetVJoyModeCommand    { get; }

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
                bool isXbox = IsXbox360Mode;
                StatusMessage = started
                    ? (isXbox ? "Pipeline active — sending to Virtual Xbox 360 (ViGEm)." : "Pipeline active — sending to vJoy.")
                    : (isXbox ? "Failed to start — check ViGEmBus driver." : "Failed to start — check vJoy device.");
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
            RefreshCardSubtitles();
            SyncUiFromCurrentAxisConfig();
            RefreshAllCardsConfigSummary();
            OnPropertyChanged(nameof(OutputMode));
            OnPropertyChanged(nameof(IsXbox360Mode));
            OnPropertyChanged(nameof(IsVJoyMode));
            OnPropertyChanged(nameof(OutputModeDescription));

            if (wasRunning) TogglePipeline();
            StatusMessage = "Config loaded.";
            
            OnPropertyChanged(nameof(RTVibrationEnabled));
            OnPropertyChanged(nameof(LTVibrationEnabled));
            OnPropertyChanged(nameof(MaxVibrationPercent));
            OnPropertyChanged(nameof(RTVibrationMotorIndex));
            OnPropertyChanged(nameof(LTVibrationMotorIndex));
            OnPropertyChanged(nameof(RTVibrationModeIndex));
            OnPropertyChanged(nameof(LTVibrationModeIndex));
        }
        catch (Exception ex) { StatusMessage = $"Load error: {ex.Message}"; }
    }

    // ─── Vibration Config Properties ──────────────────────────────────────────

    public bool ShowRTVibrationToggle => string.Equals(_selectedAxisSource, "RightTrigger", StringComparison.OrdinalIgnoreCase);
    public bool ShowLTVibrationToggle => string.Equals(_selectedAxisSource, "LeftTrigger", StringComparison.OrdinalIgnoreCase);

    public bool RTVibrationEnabled
    {
        get => _appConfig.RTVibrationEnabled;
        set
        {
            if (_appConfig.RTVibrationEnabled != value)
            {
                _appConfig.RTVibrationEnabled = value;
                OnPropertyChanged();
            }
        }
    }

    public bool LTVibrationEnabled
    {
        get => _appConfig.LTVibrationEnabled;
        set
        {
            if (_appConfig.LTVibrationEnabled != value)
            {
                _appConfig.LTVibrationEnabled = value;
                OnPropertyChanged();
            }
        }
    }

    public double MaxVibrationPercent
    {
        get => _appConfig.MaxVibrationPercent;
        set
        {
            int clamped = (int)Math.Clamp(value, 0, 100);
            if (_appConfig.MaxVibrationPercent != clamped)
            {
                _appConfig.MaxVibrationPercent = clamped;
                OnPropertyChanged();
            }
        }
    }

    public int RTVibrationMotorIndex
    {
        get => _appConfig.RTVibrationMotor == "LowFreq" ? 0 : 1;
        set
        {
            string newMotor = value == 0 ? "LowFreq" : "HighFreq";
            if (_appConfig.RTVibrationMotor != newMotor)
            {
                _appConfig.RTVibrationMotor = newMotor;
                OnPropertyChanged();
            }
        }
    }

    public int LTVibrationMotorIndex
    {
        get => _appConfig.LTVibrationMotor == "LowFreq" ? 0 : 1;
        set
        {
            string newMotor = value == 0 ? "LowFreq" : "HighFreq";
            if (_appConfig.LTVibrationMotor != newMotor)
            {
                _appConfig.LTVibrationMotor = newMotor;
                OnPropertyChanged();
            }
        }
    }

    public int RTVibrationModeIndex
    {
        get => _appConfig.RTVibrationMode == "Static" ? 1 : 0;
        set
        {
            string newMode = value == 1 ? "Static" : "Proportional";
            if (_appConfig.RTVibrationMode != newMode)
            {
                _appConfig.RTVibrationMode = newMode;
                OnPropertyChanged();
            }
        }
    }

    public int LTVibrationModeIndex
    {
        get => _appConfig.LTVibrationMode == "Static" ? 1 : 0;
        set
        {
            string newMode = value == 1 ? "Static" : "Proportional";
            if (_appConfig.LTVibrationMode != newMode)
            {
                _appConfig.LTVibrationMode = newMode;
                OnPropertyChanged();
            }
        }
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

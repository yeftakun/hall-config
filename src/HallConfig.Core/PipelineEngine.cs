using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using HallConfig.Core.Models;

namespace HallConfig.Core;

public class PipelineEngine : IDisposable
{
    private readonly InputReader _inputReader;
    // Per-axis signal processors so each axis has independent _smoothed / _active state.
    private readonly ConcurrentDictionary<string, SignalProcessor> _processors = new();
    private IOutputDevice _outputDevice;
    private AppConfig _config;
    private ProcessorConfig _processorConfig;

    private Thread? _workerThread;
    private volatile bool _isRunning;
    private bool _disposed;

    // Internal rate counter — incremented by the worker, read atomically by any thread.
    private long _workerIterationCount;

    public event Action<AxisSample>? SampleProcessed;
    public event Action<MultiAxisSample>? MultiAxisSampleProcessed;
    public event Action<string>? StatusMessage;

    public bool IsRunning => _isRunning;
    public AppConfig Config => _config;
    /// <summary>Returns the SignalProcessor for the current AxisSource (for diagnostics).</summary>
    public SignalProcessor Processor => GetOrCreateProcessor(_config.AxisSource);
    public InputReader Reader => _inputReader;
    public IOutputDevice Output => _outputDevice;

    /// <summary>
    /// Atomically reads and returns the total number of worker loop iterations since Start().
    /// Use the delta between two calls (1 second apart) to compute the actual pipeline Hz.
    /// </summary>
    public long TakeIterationSnapshot() => Interlocked.Read(ref _workerIterationCount);

    public PipelineEngine(
        AppConfig? config = null,
        InputReader? inputReader = null,
        SignalProcessor? signalProcessor = null,
        IOutputDevice? outputDevice = null)
    {
        _config = config ?? new AppConfig();
        _processorConfig = _config.ToProcessorConfig();
        _inputReader = inputReader ?? new InputReader();
        // Pre-seed the starting axis processor (signalProcessor param kept for test injection)
        if (signalProcessor != null)
            _processors[_config.AxisSource] = signalProcessor;

        _outputDevice = outputDevice ?? CreateOutputDevice(_config);
    }

    public static IOutputDevice CreateOutputDevice(AppConfig config)
    {
        if (string.Equals(config.OutputMode, "vJoy", StringComparison.OrdinalIgnoreCase))
        {
            var axisType = VJoyOutput.ParseAxis(config.VJoyAxis);
            return new VJoyOutput(config.VJoyDeviceId, axisType);
        }
        else
        {
            return new Xbox360Output();
        }
    }

    private SignalProcessor GetOrCreateProcessor(string axisSource) =>
        _processors.GetOrAdd(axisSource, _ => new SignalProcessor());

    public void UpdateConfig(AppConfig newConfig)
    {
        ArgumentNullException.ThrowIfNull(newConfig);
        _config = newConfig;
        _processorConfig = newConfig.ToProcessorConfig();

        bool wasAcquired = _outputDevice.IsAcquired;
        bool needRecreate = false;

        if (_outputDevice is Xbox360Output && string.Equals(newConfig.OutputMode, "vJoy", StringComparison.OrdinalIgnoreCase))
        {
            needRecreate = true;
        }
        else if (_outputDevice is VJoyOutput vjoy)
        {
            if (!string.Equals(newConfig.OutputMode, "vJoy", StringComparison.OrdinalIgnoreCase))
            {
                needRecreate = true;
            }
            else if (vjoy.DeviceId != newConfig.VJoyDeviceId)
            {
                needRecreate = true;
            }
        }

        if (needRecreate)
        {
            _outputDevice.Dispose();
            _outputDevice = CreateOutputDevice(newConfig);
            if (wasAcquired || _isRunning)
            {
                _outputDevice.Acquire();
            }
        }
    }

    public bool Start()
    {
        if (_isRunning) return true;

        if (!_outputDevice.IsAcquired)
        {
            if (!_outputDevice.Acquire())
            {
                if (_outputDevice is Xbox360Output)
                    StatusMessage?.Invoke("[WARNING] Failed to initialize Virtual Xbox 360 controller. Ensure ViGEmBus driver is running.");
                else
                    StatusMessage?.Invoke("[WARNING] Failed to acquire vJoy device. Ensure vJoy is installed.");
            }
        }

        Interlocked.Exchange(ref _workerIterationCount, 0);
        _isRunning = true;
        _workerThread = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name         = "HallConfig.PipelineWorker",
            Priority     = ThreadPriority.AboveNormal
        };
        _workerThread.Start();
        return true;
    }

    public void Stop()
    {
        if (!_isRunning)
        {
            return;
        }

        _isRunning = false;
        if (_workerThread != null && _workerThread.IsAlive)
        {
            _workerThread.Join(500);
            _workerThread = null;
        }

        if (_outputDevice.IsAcquired)
        {
            _outputDevice.ResetToCenter();
        }
    }

    private void WorkerLoop()
    {
        var sw = Stopwatch.StartNew();
        long lastTick = sw.ElapsedTicks;
        double stopwatchFrequency = Stopwatch.Frequency;

        long loopIteration = 0;
        long lastLoopIteration = 0;
        long lastHeartbeatMs = 0;

        // Separate per-category error timestamps to prevent one error suppressing another
        long lastReadErrorMs = -5000;
        long lastOutputErrorMs = -5000;
        long lastSampleErrorMs = -5000;
        long lastUnhandledMs = -5000;

        Logger.Info("Pipeline", $"Started. OutputMode={_config.OutputMode}, TargetRate={_config.PollingHz}Hz");

        while (_isRunning)
        {
            double targetIntervalTicks = (double)stopwatchFrequency / Math.Max(1, _config.PollingHz);

            try
            {
                loopIteration++;
                Interlocked.Increment(ref _workerIterationCount);

                // --- Read all gamepad inputs with 1 single XInput syscall ---
                float rawRT = 0f, rawLT = 0f, rawLX = 0.5f, rawLY = 0.5f;
                short rawRX = 0, rawRY = 0;
                ushort rawButtons = 0;
                try
                {
                    _inputReader.ReadAllInputs(_config.DeviceIndex, out rawRT, out rawLT, out rawLX, out rawLY, out rawRX, out rawRY, out rawButtons);
                }
                catch (Exception ex)
                {
                    long now = sw.ElapsedMilliseconds;
                    if (now - lastReadErrorMs > 1000)
                    {
                        lastReadErrorMs = now;
                        Logger.Error("WorkerLoop", "Exception in ReadAllInputs", ex);
                    }
                }

                // --- Process all 4 axes with their independent SignalProcessor and AxisProcessorConfig ---
                var procRT = GetOrCreateProcessor("RightTrigger");
                var procLT = GetOrCreateProcessor("LeftTrigger");
                var procLX = GetOrCreateProcessor("LeftStickX");
                var procLY = GetOrCreateProcessor("LeftStickY");

                float processedRT = procRT.Process(rawRT, _config.GetAxisConfig("RightTrigger").ToProcessorConfig());
                float processedLT = procLT.Process(rawLT, _config.GetAxisConfig("LeftTrigger").ToProcessorConfig());
                float processedLX = procLX.Process(rawLX, _config.GetAxisConfig("LeftStickX").ToProcessorConfig());
                float processedLY = procLY.Process(rawLY, _config.GetAxisConfig("LeftStickY").ToProcessorConfig());

                // Selected axis values for UI tuning HUD
                string axisSource = _config.AxisSource;
                float selectedRaw = axisSource switch
                {
                    "LeftTrigger" or "LT" => rawLT,
                    "LeftStickX"  or "LX" => rawLX,
                    "LeftStickY"  or "LY" => rawLY,
                    _                     => rawRT
                };
                float selectedProcessed = axisSource switch
                {
                    "LeftTrigger" or "LT" => processedLT,
                    "LeftStickX"  or "LX" => processedLX,
                    "LeftStickY"  or "LY" => processedLY,
                    _                     => processedRT
                };
                bool selectedIsActive = axisSource switch
                {
                    "LeftTrigger" or "LT" => procLT.IsActive,
                    "LeftStickX"  or "LX" => procLX.IsActive,
                    "LeftStickY"  or "LY" => procLY.IsActive,
                    _                     => procRT.IsActive
                };

                // --- Write all axes, right stick, and buttons to active Output Device in a single atomic report ---
                if (_outputDevice.IsAcquired)
                {
                    try
                    {
                        _outputDevice.UpdateFullState(processedLX, processedLY, processedLT, processedRT, rawRX, rawRY, rawButtons);
                    }
                    catch (Exception ex)
                    {
                        long now = sw.ElapsedMilliseconds;
                        if (now - lastOutputErrorMs > 1000)
                        {
                            lastOutputErrorMs = now;
                            Logger.Error("WorkerLoop", "Exception in UpdateFullState", ex);
                        }
                    }
                } 

                // --- Propagate sample to subscribers ---
                var nowUtc = DateTime.UtcNow;
                var sampleRT = new AxisSample(nowUtc, rawRT, processedRT, procRT.IsActive);
                var sampleLT = new AxisSample(nowUtc, rawLT, processedLT, procLT.IsActive);
                var sampleLX = new AxisSample(nowUtc, rawLX, processedLX, procLX.IsActive);
                var sampleLY = new AxisSample(nowUtc, rawLY, processedLY, procLY.IsActive);

                var multiSample = new MultiAxisSample(nowUtc, sampleRT, sampleLT, sampleLX, sampleLY, axisSource);
                var selectedSample = new AxisSample(nowUtc, selectedRaw, selectedProcessed, selectedIsActive);

                try
                {
                    MultiAxisSampleProcessed?.Invoke(multiSample);
                    SampleProcessed?.Invoke(selectedSample);
                }
                catch (Exception ex)
                {
                    long now = sw.ElapsedMilliseconds;
                    if (now - lastSampleErrorMs > 1000)
                    {
                        lastSampleErrorMs = now;
                        Logger.Error("WorkerLoop", "Exception in SampleProcessed event", ex);
                    }
                }

                // --- 30s Pipeline Rate Monitor to log file ---
                long nowMs = sw.ElapsedMilliseconds;
                if (nowMs - lastHeartbeatMs >= 30000)
                {
                    long elapsedMs = nowMs - lastHeartbeatMs;
                    lastHeartbeatMs = nowMs;
                    
                    long iterationsDiff = loopIteration - lastLoopIteration;
                    lastLoopIteration = loopIteration;

                    double hz = (iterationsDiff * 1000.0) / Math.Max(1, elapsedMs);
                    Logger.Info("RateMonitor", $"Current rate: {hz:F1}Hz (target {_config.PollingHz}Hz). Device={_outputDevice.Name}, Acquired={_outputDevice.IsAcquired}, GamepadConn={_inputReader.IsGamepadConnected}");
                }

                // --- Precise timing ---
                long currentTick = sw.ElapsedTicks;
                long elapsedTicks = currentTick - lastTick;
                long remainingTicks = (long)targetIntervalTicks - elapsedTicks;

                if (remainingTicks > 0)
                {
                    double remainingMs = (remainingTicks * 1000.0) / stopwatchFrequency;
                    if (remainingMs > 1.5)
                    {
                        Thread.Sleep((int)(remainingMs - 1.0));
                    }

                    while (sw.ElapsedTicks - lastTick < targetIntervalTicks)
                    {
                        Thread.SpinWait(10);
                    }
                }

                lastTick = sw.ElapsedTicks;
            }
            catch (Exception ex)
            {
                long now = sw.ElapsedMilliseconds;
                if (now - lastUnhandledMs > 1000)
                {
                    lastUnhandledMs = now;
                    Logger.Error("WorkerLoop", "Unhandled EXCEPTION", ex);
                }
                Thread.Sleep(10);
            }
        }

        Logger.Info("Pipeline", $"Stopped. Total iterations: {loopIteration}");
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Stop();
            _outputDevice.Dispose();
            _inputReader.Dispose();

            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}

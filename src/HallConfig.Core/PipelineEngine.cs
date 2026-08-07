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
    private VJoyOutput _vjoyOutput;
    private AppConfig _config;
    private ProcessorConfig _processorConfig;

    private Thread? _workerThread;
    private volatile bool _isRunning;
    private bool _disposed;

    // Internal rate counter — incremented by the worker, read atomically by any thread.
    private long _workerIterationCount;

    private string? _debugLogPath;
    private StreamWriter? _debugLog;
    private readonly object _logLock = new();

    public event Action<AxisSample>? SampleProcessed;
    public event Action<MultiAxisSample>? MultiAxisSampleProcessed;
    public event Action<string>? StatusMessage;

    public bool IsRunning => _isRunning;
    public AppConfig Config => _config;
    /// <summary>Returns the SignalProcessor for the current AxisSource (for diagnostics).</summary>
    public SignalProcessor Processor => GetOrCreateProcessor(_config.AxisSource);
    public InputReader Reader => _inputReader;
    public VJoyOutput Output => _vjoyOutput;
    public string? DebugLogPath => _debugLogPath;

    /// <summary>
    /// Atomically reads and returns the total number of worker loop iterations since Start().
    /// Use the delta between two calls (1 second apart) to compute the actual pipeline Hz.
    /// </summary>
    public long TakeIterationSnapshot() => Interlocked.Read(ref _workerIterationCount);

    public PipelineEngine(
        AppConfig? config = null,
        InputReader? inputReader = null,
        SignalProcessor? signalProcessor = null,
        VJoyOutput? vjoyOutput = null)
    {
        _config = config ?? new AppConfig();
        _processorConfig = _config.ToProcessorConfig();
        _inputReader = inputReader ?? new InputReader();
        // Pre-seed the starting axis processor (signalProcessor param kept for test injection)
        if (signalProcessor != null)
            _processors[_config.AxisSource] = signalProcessor;

        var axisType = VJoyOutput.ParseAxis(_config.VJoyAxis);
        _vjoyOutput = vjoyOutput ?? new VJoyOutput(_config.VJoyDeviceId, axisType);
    }

    private SignalProcessor GetOrCreateProcessor(string axisSource) =>
        _processors.GetOrAdd(axisSource, _ => new SignalProcessor());

    public void EnableDebugLog(string? path = null)
    {
        try
        {
            _debugLogPath = path ?? Path.Combine(
                AppContext.BaseDirectory,
                $"hallconfig-debug-{DateTime.Now:yyyyMMdd-HHmmss}.log");

            _debugLog = new StreamWriter(_debugLogPath, append: false) { AutoFlush = true };
            _debugLog.WriteLine($"=== HallConfig Debug Log Started: {DateTime.Now:O} ===");
            _debugLog.WriteLine($"    PID: {Environment.ProcessId}, Thread: {Environment.CurrentManagedThreadId}");
            _debugLog.WriteLine();
        }
        catch
        {
            _debugLog = null;
        }
    }

    private void WriteLog(string message)
    {
        lock (_logLock)
        {
            try
            {
                _debugLog?.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
            }
            catch { }
        }
    }

    public void UpdateConfig(AppConfig newConfig)
    {
        ArgumentNullException.ThrowIfNull(newConfig);
        _config = newConfig;
        _processorConfig = newConfig.ToProcessorConfig();

        var newAxisType = VJoyOutput.ParseAxis(newConfig.VJoyAxis);
        if (_vjoyOutput.DeviceId != newConfig.VJoyDeviceId || _vjoyOutput.Axis != newAxisType)
        {
            bool wasAcquired = _vjoyOutput.IsAcquired;
            _vjoyOutput.Dispose();
            _vjoyOutput = new VJoyOutput(newConfig.VJoyDeviceId, newAxisType);
            if (wasAcquired || _isRunning)
            {
                _vjoyOutput.Acquire();
            }
        }
    }

    public bool Start()
    {
        if (_isRunning) return true;

        if (!_vjoyOutput.IsAcquired)
        {
            if (!_vjoyOutput.Acquire())
                StatusMessage?.Invoke($"[WARNING] Failed to acquire vJoy Device #{_vjoyOutput.DeviceId}. Ensure vJoy is installed.");
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
        }        if (_vjoyOutput.IsAcquired)
        {
            _vjoyOutput.SetAxisValue(VJoyAxisType.X, 0.5f);
            _vjoyOutput.SetAxisValue(VJoyAxisType.Y, 0.0f);
            _vjoyOutput.SetAxisValue(VJoyAxisType.Z, 0.0f);
            _vjoyOutput.SetAxisValue(VJoyAxisType.RX, 0.5f);
        }
    }

    private void WorkerLoop()
    {
        var sw = Stopwatch.StartNew();
        long lastTick = sw.ElapsedTicks;
        double stopwatchFrequency = Stopwatch.Frequency;

        long loopIteration = 0;
        long lastHeartbeatMs = 0;

        // Separate per-category error timestamps to prevent one error suppressing another
        long lastReadErrorMs = -5000;
        long lastVJoyErrorMs = -5000;
        long lastSampleErrorMs = -5000;
        long lastUnhandledMs = -5000;

        WriteLog($"WorkerLoop starting. ThreadId={Environment.CurrentManagedThreadId}, " +
                 $"TargetHz={_config.PollingHz}, InitialAxisSource={_config.AxisSource}");

        while (_isRunning)
        {
            double targetIntervalTicks = (double)stopwatchFrequency / Math.Max(1, _config.PollingHz);

            try
            {
                loopIteration++;
                Interlocked.Increment(ref _workerIterationCount);

                // --- Read all 4 axes with 1 single XInput syscall ---
                float rawRT = 0f, rawLT = 0f, rawLX = 0.5f, rawLY = 0.5f;
                try
                {
                    _inputReader.ReadAllAxes(_config.DeviceIndex, out rawRT, out rawLT, out rawLX, out rawLY);
                }
                catch (Exception ex)
                {
                    long now = sw.ElapsedMilliseconds;
                    if (now - lastReadErrorMs > 1000)
                    {
                        lastReadErrorMs = now;
                        WriteLog($"[EXCEPTION ReadAllAxes] {ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}");
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

                // --- Write all axes to vJoy with requested mapping ---
                // Axle 1 (X)  : Joystick L horizontal (LeftStickX)
                // Axle 2 (Y)  : Right Trigger (RT)
                // Axle 3 (Z)  : Left Trigger (LT)
                // Axle 4 (RX) : Joystick L vertical (LeftStickY)
                if (_vjoyOutput.IsAcquired)
                {
                    try
                    {
                        _vjoyOutput.SetAxisValue(VJoyAxisType.X, processedLX);
                        _vjoyOutput.SetAxisValue(VJoyAxisType.Y, processedRT);
                        _vjoyOutput.SetAxisValue(VJoyAxisType.Z, processedLT);
                        _vjoyOutput.SetAxisValue(VJoyAxisType.RX, processedLY);
                    }
                    catch (Exception ex)
                    {
                        long now = sw.ElapsedMilliseconds;
                        if (now - lastVJoyErrorMs > 1000)
                        {
                            lastVJoyErrorMs = now;
                            WriteLog($"[EXCEPTION SetAxisValue] {ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}");
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
                        WriteLog($"[EXCEPTION SampleProcessed] {ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}");
                    }
                }

                // --- Heartbeat to log file only (never StatusMessage to avoid Console race) ---
                long nowMs = sw.ElapsedMilliseconds;
                if (nowMs - lastHeartbeatMs >= 1000)
                {
                    lastHeartbeatMs = nowMs;
                    WriteLog($"[HEARTBEAT] Iter={loopIteration}, Hz={_config.PollingHz}, Raw={selectedRaw:F4}, Out={selectedProcessed:F4}, " +
                             $"Axis={axisSource}, ProcessorCount={_processors.Count}, " +
                             $"vJoyAcq={_vjoyOutput.IsAcquired}, GamepadConn={_inputReader.IsGamepadConnected}");
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
                    WriteLog($"[UNHANDLED EXCEPTION in WorkerLoop] {ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}");
                }
                Thread.Sleep(10);
            }
        }

        WriteLog($"WorkerLoop stopped. Total iterations: {loopIteration}");
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Stop();
            _vjoyOutput.Dispose();
            _inputReader.Dispose();

            lock (_logLock)
            {
                try
                {
                    _debugLog?.WriteLine($"\n=== HallConfig Debug Log Ended: {DateTime.Now:O} ===");
                    _debugLog?.Dispose();
                    _debugLog = null;
                }
                catch { }
            }

            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}

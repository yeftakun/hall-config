using System;
using System.Collections.Generic;
using HallConfig.Core.Models;

namespace HallConfig.Core.Models;

public class AppConfig
{
    // ─── Base fields (unchanged, backward compat) ──────────────────────────────
    public int DeviceIndex { get; set; } = 0;
    public string AxisSource { get; set; } = "RightTrigger";
    public string OutputMode { get; set; } = "Xbox360"; // "Xbox360" or "vJoy"
    public uint VJoyDeviceId { get; set; } = 1;
    public string VJoyAxis { get; set; } = "X";
    public int PollingHz { get; set; } = 250;

    // ─── Startup & Window Options ─────────────────────────────────────────────
    public bool RunOnStartup { get; set; } = false;
    public bool StartMinimized { get; set; } = false;
    public bool MinimizeToTrayOnClose { get; set; } = true;

    /// <summary>
    /// Global/legacy smoothing config. Used as default when an axis doesn't have an
    /// entry in AxisConfigs yet, and kept for DevConsole hotkey backward compat.
    /// </summary>
    public SmoothingConfig Smoothing { get; set; } = new();

    /// <summary>
    /// Global/legacy hysteresis config. Same usage as Smoothing above.
    /// </summary>
    public HysteresisConfig Hysteresis { get; set; } = new();

    // ─── Per-axis configs (new) ────────────────────────────────────────────────
    /// <summary>
    /// Independent config per axis source. Keys: "RightTrigger", "LeftTrigger",
    /// "LeftStickX", "LeftStickY". If a key is missing, GetAxisConfig falls back
    /// to the global Smoothing/Hysteresis defaults.
    /// </summary>
    public Dictionary<string, AxisProcessorConfig> AxisConfigs { get; set; } = new();

    // ─── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Return the per-axis config for <paramref name="axisSource"/>, or a copy of
    /// the global defaults if the axis hasn't been customised yet.
    /// Does NOT modify AxisConfigs (safe to call from any thread).
    /// </summary>
    public AxisProcessorConfig GetAxisConfig(string axisSource)
    {
        if (AxisConfigs.TryGetValue(axisSource, out var cfg))
            return cfg;

        // Fall back to global defaults (clone so callers can't mutate the source)
        return new AxisProcessorConfig
        {
            Smoothing  = new SmoothingConfig  { Enabled = Smoothing.Enabled,  Alpha = Smoothing.Alpha },
            Hysteresis = new HysteresisConfig
            {
                Enabled       = Hysteresis.Enabled,
                ThresholdUp   = Hysteresis.ThresholdUp,
                ThresholdDown = Hysteresis.ThresholdDown
            }
        };
    }

    /// <summary>
    /// Returns the ProcessorConfig for the currently active AxisSource.
    /// Checks per-axis config first, then falls back to global Smoothing/Hysteresis.
    /// </summary>
    public ProcessorConfig ToProcessorConfig()
    {
        if (AxisConfigs.TryGetValue(AxisSource, out var axisConfig))
            return axisConfig.ToProcessorConfig();

        // Backward-compat fallback — used by DevConsole & tests that set
        // Smoothing/Hysteresis directly without touching AxisConfigs.
        return new ProcessorConfig
        {
            Smoothing = new SmoothingConfig
            {
                Enabled = Smoothing.Enabled,
                Alpha   = Smoothing.Alpha
            },
            Hysteresis = new HysteresisConfig
            {
                Enabled       = Hysteresis.Enabled,
                ThresholdUp   = Hysteresis.ThresholdUp,
                ThresholdDown = Hysteresis.ThresholdDown
            }
        };
    }
}

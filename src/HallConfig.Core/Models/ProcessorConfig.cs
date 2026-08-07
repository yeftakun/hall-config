namespace HallConfig.Core.Models;

public class SmoothingConfig
{
    public bool Enabled { get; set; } = true;
    public float Alpha { get; set; } = 0.4f;
}

public class HysteresisConfig
{
    public bool Enabled { get; set; } = true;
    public float ThresholdUp { get; set; } = 0.27f;
    public float ThresholdDown { get; set; } = 0.23f;
}

public class ProcessorConfig
{
    public SmoothingConfig Smoothing { get; set; } = new();
    public HysteresisConfig Hysteresis { get; set; } = new();
}

/// <summary>Independent smoothing + hysteresis config for a single axis source.</summary>
public class AxisProcessorConfig
{
    public SmoothingConfig Smoothing { get; set; } = new();
    public HysteresisConfig Hysteresis { get; set; } = new();

    public ProcessorConfig ToProcessorConfig() => new()
    {
        Smoothing  = new SmoothingConfig  { Enabled = Smoothing.Enabled,  Alpha = Smoothing.Alpha },
        Hysteresis = new HysteresisConfig
        {
            Enabled       = Hysteresis.Enabled,
            ThresholdUp   = Hysteresis.ThresholdUp,
            ThresholdDown = Hysteresis.ThresholdDown
        }
    };

    /// <summary>Deep-copy this config.</summary>
    public AxisProcessorConfig Clone() => new()
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

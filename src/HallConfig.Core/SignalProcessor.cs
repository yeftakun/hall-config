using System;
using HallConfig.Core.Models;

namespace HallConfig.Core;

public class SignalProcessor
{
    private float _smoothed;
    private bool _active;

    public bool IsActive => _active;
    public float SmoothedValue => _smoothed;

    public SignalProcessor(float initialSmoothed = 0f, bool initialActive = false)
    {
        _smoothed = initialSmoothed;
        _active = initialActive;
    }

    public void Reset(float initialSmoothed = 0f, bool initialActive = false)
    {
        _smoothed = initialSmoothed;
        _active = initialActive;
    }

    public float Process(float raw, ProcessorConfig cfg)
    {
        ArgumentNullException.ThrowIfNull(cfg);

        float value = raw;

        if (cfg.Smoothing?.Enabled == true)
        {
            float alpha = Math.Clamp(cfg.Smoothing.Alpha, 0f, 1f);
            _smoothed = (alpha * value) + ((1f - alpha) * _smoothed);
            value = _smoothed;
        }
        else
        {
            _smoothed = value;
        }

        if (cfg.Hysteresis?.Enabled == true)
        {
            float up = cfg.Hysteresis.ThresholdUp;
            float down = cfg.Hysteresis.ThresholdDown;

            if (value > up)
            {
                _active = true;
            }
            else if (value < down)
            {
                _active = false;
            }

            if (!_active)
            {
                return 0f;
            }

            float denominator = 1f - down;
            if (denominator <= 0f)
            {
                return 0f;
            }

            value = (value - down) / denominator;
        }

        return Math.Clamp(value, 0f, 1f);
    }
}

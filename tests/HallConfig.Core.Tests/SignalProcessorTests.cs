using System;
using HallConfig.Core;
using HallConfig.Core.Models;
using Xunit;

namespace HallConfig.Core.Tests;

public class SignalProcessorTests
{
    [Fact]
    public void Passthrough_WhenBothDisabled_OutputEqualsInput()
    {
        var processor = new SignalProcessor();
        var config = new ProcessorConfig
        {
            Smoothing = new SmoothingConfig { Enabled = false },
            Hysteresis = new HysteresisConfig { Enabled = false }
        };

        float[] testInputs = [0.0f, 0.05f, 0.15f, 0.27f, 0.50f, 0.85f, 1.0f];

        foreach (var input in testInputs)
        {
            float output = processor.Process(input, config);
            Assert.Equal(input, output, precision: 5);
        }
    }

    [Fact]
    public void Hysteresis_TransitionCycle_ActivatesAndDeactivatesCorrectly()
    {
        var processor = new SignalProcessor();
        var config = new ProcessorConfig
        {
            Smoothing = new SmoothingConfig { Enabled = false },
            Hysteresis = new HysteresisConfig
            {
                Enabled = true,
                ThresholdUp = 0.27f,
                ThresholdDown = 0.23f
            }
        };

        // 1. Initial sub-threshold jitter -> output must be 0
        Assert.Equal(0f, processor.Process(0.10f, config));
        Assert.False(processor.IsActive);

        Assert.Equal(0f, processor.Process(0.20f, config));
        Assert.False(processor.IsActive);

        Assert.Equal(0f, processor.Process(0.25f, config));
        Assert.False(processor.IsActive);

        // 2. Crosses above ThresholdUp (0.27) -> Activates and remaps linearly
        float out30 = processor.Process(0.30f, config);
        Assert.True(processor.IsActive);
        // Expected: (0.30 - 0.23) / (1.0 - 0.23) = 0.07 / 0.77 ≈ 0.090909
        float expected30 = (0.30f - 0.23f) / (1.0f - 0.23f);
        Assert.Equal(expected30, out30, precision: 4);

        // 3. Full press 1.0 -> output must be 1.0
        float out100 = processor.Process(1.0f, config);
        Assert.True(processor.IsActive);
        Assert.Equal(1.0f, out100, precision: 4);

        // 4. Releases down into the hysteresis band (0.25 is between 0.23 and 0.27)
        // Since it was already active, it MUST stay active!
        float out25 = processor.Process(0.25f, config);
        Assert.True(processor.IsActive);
        float expected25 = (0.25f - 0.23f) / (1.0f - 0.23f); // 0.02 / 0.77 ≈ 0.025974
        Assert.Equal(expected25, out25, precision: 4);

        // 5. Drops below ThresholdDown (0.22 < 0.23) -> Deactivates and output becomes 0
        float out22 = processor.Process(0.22f, config);
        Assert.False(processor.IsActive);
        Assert.Equal(0f, out22);

        // 6. Rises back to 0.25 (inside band, but starting from inactive state) -> MUST stay inactive (0)
        float out25Again = processor.Process(0.25f, config);
        Assert.False(processor.IsActive);
        Assert.Equal(0f, out25Again);
    }

    [Fact]
    public void Hysteresis_ExactThresholdBoundaries_BehavePredictably()
    {
        var processor = new SignalProcessor();
        var config = new ProcessorConfig
        {
            Smoothing = new SmoothingConfig { Enabled = false },
            Hysteresis = new HysteresisConfig
            {
                Enabled = true,
                ThresholdUp = 0.27f,
                ThresholdDown = 0.23f
            }
        };

        // When inactive, exactly at ThresholdUp (0.27) -> remains inactive (requires strictly > ThresholdUp)
        float outExactUp = processor.Process(0.27f, config);
        Assert.False(processor.IsActive);
        Assert.Equal(0f, outExactUp);

        // Slightly above -> activates
        float outJustAbove = processor.Process(0.2701f, config);
        Assert.True(processor.IsActive);
        Assert.True(outJustAbove > 0f);

        // When active, drops to exactly ThresholdDown (0.23) -> remains active (requires strictly < ThresholdDown to deactivate)
        float outExactDown = processor.Process(0.23f, config);
        Assert.True(processor.IsActive);
        Assert.Equal(0f, outExactDown, precision: 5); // (0.23 - 0.23)/0.77 = 0.0

        // Drops slightly below ThresholdDown (0.2299) -> deactivates
        float outJustBelow = processor.Process(0.2299f, config);
        Assert.False(processor.IsActive);
        Assert.Equal(0f, outJustBelow);
    }

    [Fact]
    public void Smoothing_StepInput_ConvergesGradually()
    {
        var processor = new SignalProcessor();
        var config = new ProcessorConfig
        {
            Smoothing = new SmoothingConfig
            {
                Enabled = true,
                Alpha = 0.4f
            },
            Hysteresis = new HysteresisConfig { Enabled = false }
        };

        // Step input: 0.0 -> 1.0
        // Step 1: 0.4 * 1.0 + 0.6 * 0.0 = 0.400
        float step1 = processor.Process(1.0f, config);
        Assert.Equal(0.400f, step1, precision: 3);

        // Step 2: 0.4 * 1.0 + 0.6 * 0.400 = 0.640
        float step2 = processor.Process(1.0f, config);
        Assert.Equal(0.640f, step2, precision: 3);

        // Step 3: 0.4 * 1.0 + 0.6 * 0.640 = 0.784
        float step3 = processor.Process(1.0f, config);
        Assert.Equal(0.784f, step3, precision: 3);

        // Step 4: 0.4 * 1.0 + 0.6 * 0.784 = 0.8704
        float step4 = processor.Process(1.0f, config);
        Assert.Equal(0.8704f, step4, precision: 3);

        // Verify output is monotonically increasing toward 1.0 without instantaneous jump
        Assert.True(step1 < step2);
        Assert.True(step2 < step3);
        Assert.True(step3 < step4);
        Assert.True(step4 < 1.0f);

        // After multiple iterations, converges near 1.0
        float finalStep = 0f;
        for (int i = 0; i < 20; i++)
        {
            finalStep = processor.Process(1.0f, config);
        }
        Assert.Equal(1.0f, finalStep, precision: 3);
    }

    [Fact]
    public void Combined_SmoothingAndHysteresis_FiltersShortSpikes()
    {
        var processor = new SignalProcessor();
        var config = new ProcessorConfig
        {
            Smoothing = new SmoothingConfig { Enabled = true, Alpha = 0.3f },
            Hysteresis = new HysteresisConfig
            {
                Enabled = true,
                ThresholdUp = 0.27f,
                ThresholdDown = 0.23f
            }
        };

        // A single 1-frame glitch pulse to 0.35
        // Smoothed value becomes 0.3 * 0.35 + 0.7 * 0 = 0.105 (< 0.27)
        float spikeOut = processor.Process(0.35f, config);
        Assert.False(processor.IsActive);
        Assert.Equal(0f, spikeOut);

        // Input immediately goes back to 0.0 -> smoothed becomes 0.0735
        float returnOut = processor.Process(0.0f, config);
        Assert.False(processor.IsActive);
        Assert.Equal(0f, returnOut);
    }

    [Fact]
    public void Reset_ClearsInternalState()
    {
        var processor = new SignalProcessor();
        var config = new ProcessorConfig
        {
            Smoothing = new SmoothingConfig { Enabled = true, Alpha = 0.5f },
            Hysteresis = new HysteresisConfig { Enabled = true, ThresholdUp = 0.27f, ThresholdDown = 0.23f }
        };

        // Make it active with high smoothed value
        processor.Process(1.0f, config);
        Assert.True(processor.IsActive);
        Assert.True(processor.SmoothedValue > 0f);

        // Reset
        processor.Reset();
        Assert.False(processor.IsActive);
        Assert.Equal(0f, processor.SmoothedValue);
    }
}

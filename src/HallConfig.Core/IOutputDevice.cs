using System;

namespace HallConfig.Core;

/// <summary>
/// Unified abstraction for gamepad output devices (e.g. vJoy DirectInput or ViGEm XInput virtual Xbox 360).
/// </summary>
public interface IOutputDevice : IDisposable
{
    /// <summary>Human-readable name of the output device (e.g. "Virtual Xbox 360", "vJoy Device #1").</summary>
    string Name { get; }

    /// <summary>Indicates whether the output device is successfully acquired and ready for input reports.</summary>
    bool IsAcquired { get; }

    /// <summary>Acquires or connects the virtual output device.</summary>
    bool Acquire();

    /// <summary>Relinquishes or disconnects the virtual output device.</summary>
    void Relinquish();

    /// <summary>
    /// Sets a single normalized axis value (0.0 to 1.0) by axis name.
    /// </summary>
    bool SetAxisValue(string axisName, float normalizedValue);

    /// <summary>
    /// Updates all 4 primary axes simultaneously in a single report.
    /// <paramref name="lx"/>: Left stick horizontal (0.0 - 1.0, 0.5 center).
    /// <paramref name="ly"/>: Left stick vertical (0.0 - 1.0, 0.5 center).
    /// <paramref name="lt"/>: Left trigger (0.0 - 1.0, 0.0 unpressed).
    /// <paramref name="rt"/>: Right trigger (0.0 - 1.0, 0.0 unpressed).
    /// </summary>
    bool UpdateAllAxes(float lx, float ly, float lt, float rt);

    /// <summary>
    /// Updates all controller inputs in a single atomic report:
    /// - 4 primary conditioned axes (LX, LY, LT, RT)
    /// - Raw passthrough right stick (rawRx, rawRy in native short -32768..32767)
    /// - Raw passthrough buttons bitmask (XInput ushort bitmask: A, B, X, Y, LB, RB, Start, Back, LS, RS, D-Pad)
    /// </summary>
    bool UpdateFullState(float lx, float ly, float lt, float rt, short rawRx, short rawRy, ushort buttons);

    /// <summary>
    /// Resets all axes to neutral rest positions (triggers to 0, sticks to neutral center).
    /// </summary>
    void ResetToCenter();
}

using System;

namespace HallConfig.Core.Models;

/// <summary>
/// Snapshot data sample untuk 4 axis utama yang diproses secara simultan.
/// </summary>
public readonly record struct MultiAxisSample(
    DateTime Timestamp,
    AxisSample RightTrigger,
    AxisSample LeftTrigger,
    AxisSample LeftStickX,
    AxisSample LeftStickY,
    string SelectedAxisSource
);

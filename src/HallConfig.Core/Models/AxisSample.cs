using System;

namespace HallConfig.Core.Models;

public readonly record struct AxisSample(
    DateTime Timestamp,
    float RawValue,
    float ProcessedValue,
    bool IsActive
);

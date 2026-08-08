using System;
using System.Collections.Generic;
using vJoyInterfaceWrap;

namespace HallConfig.Core;

public enum VJoyAxisType : uint
{
    X = HID_USAGES.HID_USAGE_X,       // Axle 1
    Y = HID_USAGES.HID_USAGE_Y,       // Axle 2
    Z = HID_USAGES.HID_USAGE_Z,       // Axle 3
    RX = HID_USAGES.HID_USAGE_RX,     // Axle 4 (X-Rotation)
    RY = HID_USAGES.HID_USAGE_RY,     // Axle 5 (Y-Rotation)
    RZ = HID_USAGES.HID_USAGE_RZ,     // Axle 6 (Z-Rotation)
    SL0 = HID_USAGES.HID_USAGE_SL0,   // Axle 7 (Slider 0)
    SL1 = HID_USAGES.HID_USAGE_SL1    // Axle 8 (Slider 1)
}

public class VJoyOutput : IOutputDevice
{
    private readonly vJoy _joystick;
    private readonly uint _deviceId;
    private readonly VJoyAxisType _axisType;
    private readonly HID_USAGES _axisUsage;
    private readonly Dictionary<VJoyAxisType, (long Min, long Max)> _axisRanges = new();
    private bool _isAcquired;
    private long _axisMin = 1;
    private long _axisMax = 32768;
    private bool _disposed;

    public string Name => $"vJoy Device #{_deviceId}";
    public uint DeviceId => _deviceId;
    public VJoyAxisType Axis => _axisType;
    public bool IsAcquired => _isAcquired;
    public long AxisMin => _axisMin;
    public long AxisMax => _axisMax;

    public VJoyOutput(uint deviceId = 1, VJoyAxisType axis = VJoyAxisType.X)
    {
        _deviceId = deviceId;
        _axisType = axis;
        _axisUsage = (HID_USAGES)axis;
        _joystick = new vJoy();
    }

    public bool IsVJoyEnabled()
    {
        try
        {
            return _joystick.vJoyEnabled();
        }
        catch
        {
            return false;
        }
    }

    public bool Acquire()
    {
        if (!IsVJoyEnabled())
        {
            return false;
        }

        var status = _joystick.GetVJDStatus(_deviceId);
        if (status == VjdStat.VJD_STAT_OWN)
        {
            _isAcquired = true;
        }
        else if (status == VjdStat.VJD_STAT_FREE)
        {
            _isAcquired = _joystick.AcquireVJD(_deviceId);
        }
        else
        {
            _isAcquired = false;
            return false;
        }

        if (_isAcquired)
        {
            // Cache min/max for all standard axes
            foreach (VJoyAxisType axis in (VJoyAxisType[])Enum.GetValues(typeof(VJoyAxisType)))
            {
                long min = 0, max = 0;
                var usage = (HID_USAGES)axis;
                _joystick.GetVJDAxisMin(_deviceId, usage, ref min);
                _joystick.GetVJDAxisMax(_deviceId, usage, ref max);

                if (max > min)
                {
                    _axisRanges[axis] = (min, max);
                }
                else
                {
                    _axisRanges[axis] = (1, 32768);
                }
            }

            if (_axisRanges.TryGetValue(_axisType, out var range))
            {
                _axisMin = range.Min;
                _axisMax = range.Max;
            }
        }

        return _isAcquired;
    }

    public bool SetValue(float normalizedValue)
    {
        return SetAxisValue(_axisType, normalizedValue);
    }

    public bool SetAxisValue(string axisName, float normalizedValue)
    {
        return SetAxisValue(ParseAxis(axisName), normalizedValue);
    }

    public bool UpdateAllAxes(float lx, float ly, float lt, float rt)
    {
        if (!_isAcquired) return false;

        bool ok = true;
        // Axle 1 (X)  : Left Stick X (Steering)
        ok &= SetAxisValue(VJoyAxisType.X, lx);
        // Axle 2 (Y)  : Right Trigger (Throttle)
        ok &= SetAxisValue(VJoyAxisType.Y, rt);
        // Axle 3 (Z)  : Left Trigger (Brake)
        ok &= SetAxisValue(VJoyAxisType.Z, lt);
        // Axle 4 (RX) : Left Stick Y (Pitch)
        ok &= SetAxisValue(VJoyAxisType.RX, ly);
        return ok;
    }

    public void ResetToCenter()
    {
        if (!_isAcquired) return;
        SetAxisValue(VJoyAxisType.X, 0.5f);
        SetAxisValue(VJoyAxisType.Y, 0.0f);
        SetAxisValue(VJoyAxisType.Z, 0.0f);
        SetAxisValue(VJoyAxisType.RX, 0.5f);
    }

    public bool SetAxisValue(VJoyAxisType axis, float normalizedValue)
    {
        if (!_isAcquired)
        {
            return false;
        }

        try
        {
            if (!_axisRanges.TryGetValue(axis, out var range))
            {
                range = (1, 32768);
            }

            float clamped = Math.Clamp(normalizedValue, 0f, 1f);
            int targetValue = (int)Math.Round(range.Min + ((range.Max - range.Min) * clamped));

            return _joystick.SetAxis(targetValue, _deviceId, (HID_USAGES)axis);
        }
        catch
        {
            return false;
        }
    }

    public static VJoyAxisType ParseAxis(string axisName)
    {
        return (axisName?.Trim().ToUpperInvariant()) switch
        {
            "X" or "AXIS1" or "AXLE1" => VJoyAxisType.X,
            "Y" or "AXIS2" or "AXLE2" => VJoyAxisType.Y,
            "Z" or "AXIS3" or "AXLE3" => VJoyAxisType.Z,
            "RX" or "AXIS4" or "AXLE4" => VJoyAxisType.RX,
            "RY" or "AXIS5" or "AXLE5" => VJoyAxisType.RY,
            "RZ" or "AXIS6" or "AXLE6" => VJoyAxisType.RZ,
            "SL0" or "SLIDER0" or "AXIS7" or "AXLE7" => VJoyAxisType.SL0,
            "SL1" or "SLIDER1" or "AXIS8" or "AXLE8" => VJoyAxisType.SL1,
            _ => VJoyAxisType.X
        };
    }

    public void Relinquish()
    {
        if (_isAcquired)
        {
            try
            {
                _joystick.RelinquishVJD(_deviceId);
            }
            catch { }
            _isAcquired = false;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Relinquish();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}

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
    private int _buttonCount;
    private int _discPovCount;
    private int _contPovCount;
    private bool _disposed;

    public string Name => $"vJoy Device #{_deviceId}";
    public uint DeviceId => _deviceId;
    public VJoyAxisType Axis => _axisType;
    public bool IsAcquired => _isAcquired;
    public long AxisMin => _axisMin;
    public long AxisMax => _axisMax;
    public int ButtonCount => _buttonCount;
    public int DiscPovCount => _discPovCount;
    public int ContPovCount => _contPovCount;

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
            Logger.Warn("VJoyOutput", "vJoy driver is not enabled or not installed.");
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
            if (!_isAcquired)
            {
                Logger.Error("VJoyOutput", $"Failed to acquire vJoy Device #{_deviceId}. Status was FREE but acquire rejected.");
            }
        }
        else
        {
            Logger.Warn("VJoyOutput", $"Cannot acquire vJoy Device #{_deviceId}. Status: {status}");
            _isAcquired = false;
            return false;
        }

        if (_isAcquired)
        {
            Logger.Info("VJoyOutput", $"Acquired vJoy Device #{_deviceId}");
            // Cache button and POV capabilities
            try
            {
                _buttonCount = _joystick.GetVJDButtonNumber(_deviceId);
                _discPovCount = _joystick.GetVJDDiscPovNumber(_deviceId);
                _contPovCount = _joystick.GetVJDContPovNumber(_deviceId);
            }
            catch
            {
                _buttonCount = 0;
                _discPovCount = 0;
                _contPovCount = 0;
            }

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
        return UpdateFullState(lx, ly, lt, rt, 0, 0, 0);
    }

    public bool UpdateFullState(float lx, float ly, float lt, float rt, short rawRx, short rawRy, ushort buttons)
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

        // Right Stick: Axle 5 (RY) & Axle 6 (RZ) passthrough
        float normRx = Math.Clamp((rawRx + 32768f) / 65535f, 0f, 1f);
        float normRy = Math.Clamp((rawRy + 32768f) / 65535f, 0f, 1f);
        if (_axisRanges.ContainsKey(VJoyAxisType.RY))
            ok &= SetAxisValue(VJoyAxisType.RY, normRx);
        if (_axisRanges.ContainsKey(VJoyAxisType.RZ))
            ok &= SetAxisValue(VJoyAxisType.RZ, normRy);

        // Buttons passthrough (Button 1..10)
        if (_buttonCount > 0)
        {
            if (_buttonCount >= 1) _joystick.SetBtn((buttons & 0x1000) != 0, _deviceId, 1);  // A
            if (_buttonCount >= 2) _joystick.SetBtn((buttons & 0x2000) != 0, _deviceId, 2);  // B
            if (_buttonCount >= 3) _joystick.SetBtn((buttons & 0x4000) != 0, _deviceId, 3);  // X
            if (_buttonCount >= 4) _joystick.SetBtn((buttons & 0x8000) != 0, _deviceId, 4);  // Y
            if (_buttonCount >= 5) _joystick.SetBtn((buttons & 0x0100) != 0, _deviceId, 5);  // LB
            if (_buttonCount >= 6) _joystick.SetBtn((buttons & 0x0200) != 0, _deviceId, 6);  // RB
            if (_buttonCount >= 7) _joystick.SetBtn((buttons & 0x0020) != 0, _deviceId, 7);  // Back
            if (_buttonCount >= 8) _joystick.SetBtn((buttons & 0x0010) != 0, _deviceId, 8);  // Start
            if (_buttonCount >= 9) _joystick.SetBtn((buttons & 0x0040) != 0, _deviceId, 9);  // LS Click
            if (_buttonCount >= 10) _joystick.SetBtn((buttons & 0x0080) != 0, _deviceId, 10); // RS Click
        }

        // POV / D-Pad passthrough
        if (_discPovCount > 0)
        {
            int pov = -1;
            if ((buttons & 0x0001) != 0) pov = 0;      // Up
            else if ((buttons & 0x0008) != 0) pov = 1; // Right
            else if ((buttons & 0x0002) != 0) pov = 2; // Down
            else if ((buttons & 0x0004) != 0) pov = 3; // Left

            _joystick.SetDiscPov(pov, _deviceId, 1);
        }
        else if (_contPovCount > 0)
        {
            int contVal = -1; // Neutral
            bool up = (buttons & 0x0001) != 0;
            bool down = (buttons & 0x0002) != 0;
            bool left = (buttons & 0x0004) != 0;
            bool right = (buttons & 0x0008) != 0;

            if (up && right) contVal = 4500;
            else if (down && right) contVal = 13500;
            else if (down && left) contVal = 22500;
            else if (up && left) contVal = 31500;
            else if (up) contVal = 0;
            else if (right) contVal = 9000;
            else if (down) contVal = 18000;
            else if (left) contVal = 27000;

            _joystick.SetContPov(contVal, _deviceId, 1);
        }

        return ok;
    }

    public void ResetToCenter()
    {
        if (!_isAcquired) return;
        SetAxisValue(VJoyAxisType.X, 0.5f);
        SetAxisValue(VJoyAxisType.Y, 0.0f);
        SetAxisValue(VJoyAxisType.Z, 0.0f);
        SetAxisValue(VJoyAxisType.RX, 0.5f);
        if (_axisRanges.ContainsKey(VJoyAxisType.RY)) SetAxisValue(VJoyAxisType.RY, 0.5f);
        if (_axisRanges.ContainsKey(VJoyAxisType.RZ)) SetAxisValue(VJoyAxisType.RZ, 0.5f);

        if (_buttonCount > 0)
        {
            for (uint i = 1; i <= Math.Min((uint)_buttonCount, 10); i++)
            {
                _joystick.SetBtn(false, _deviceId, i);
            }
        }
        if (_discPovCount > 0) _joystick.SetDiscPov(-1, _deviceId, 1);
        else if (_contPovCount > 0) _joystick.SetContPov(-1, _deviceId, 1);
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
                Logger.Info("VJoyOutput", $"Released vJoy Device #{_deviceId}");
            }
            catch (Exception ex)
            {
                Logger.Error("VJoyOutput", $"Error relinquishing vJoy Device #{_deviceId}", ex);
            }
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

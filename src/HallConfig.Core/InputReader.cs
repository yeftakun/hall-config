using System;

namespace HallConfig.Core;

public class InputReader : IDisposable
{
    public event Action<int>? GamepadCountChanged;

    private int _lastConnectedCount = -1;

    public int ConnectedGamepadsCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < 4; i++)
            {
                if (XInputHelper.IsConnected(i))
                    count++;
            }
            return count;
        }
    }

    public bool IsGamepadConnected => XInputHelper.IsConnected(0);

    public float ReadAxis(int deviceIndex = 0, string? axisSource = null)
    {
        if (!XInputHelper.GetState(deviceIndex, out var state))
        {
            int nowCount = ConnectedGamepadsCount;
            if (nowCount != _lastConnectedCount)
            {
                _lastConnectedCount = nowCount;
                GamepadCountChanged?.Invoke(nowCount);
            }
            return 0f;
        }

        if (_lastConnectedCount != 1)
        {
            _lastConnectedCount = 1;
            GamepadCountChanged?.Invoke(1);
        }

        string src = axisSource?.Trim().ToUpperInvariant() ?? "RT";

        return src switch
        {
            "LEFTTRIGGER"  or "LT" => state.Gamepad.LeftTrigger / 255f,
            "RIGHTTRIGGER" or "RT" => state.Gamepad.RightTrigger / 255f,

            // Stick: mapped to 0..1 (center is 0.50)
            "LEFTSTICKX"  or "LX" => Math.Clamp((state.Gamepad.ThumbLX + 32768f) / 65535f, 0f, 1f),
            "LEFTSTICKY"  or "LY" => Math.Clamp((state.Gamepad.ThumbLY + 32768f) / 65535f, 0f, 1f),
            "RIGHTSTICKX" or "RX" => Math.Clamp((state.Gamepad.ThumbRX + 32768f) / 65535f, 0f, 1f),
            "RIGHTSTICKY" or "RY" => Math.Clamp((state.Gamepad.ThumbRY + 32768f) / 65535f, 0f, 1f),

            _ => state.Gamepad.RightTrigger / 255f
        };
    }

    /// <summary>Alias kompatibilitas untuk ReadAxis.</summary>
    public float ReadRawTrigger(int deviceIndex = 0, string axisSource = "RightTrigger") =>
        ReadAxis(deviceIndex, axisSource);

    /// <summary>Baca semua 4 axis utama sekaligus secara efisien (1 syscall XInput).</summary>
    public bool ReadAllAxes(int deviceIndex, out float rt, out float lt, out float lx, out float ly)
    {
        if (!XInputHelper.GetState(deviceIndex, out var state))
        {
            int nowCount = ConnectedGamepadsCount;
            if (nowCount != _lastConnectedCount)
            {
                _lastConnectedCount = nowCount;
                GamepadCountChanged?.Invoke(nowCount);
            }
            rt = lt = 0f;
            lx = ly = 0.5f;
            return false;
        }

        if (_lastConnectedCount != 1)
        {
            _lastConnectedCount = 1;
            GamepadCountChanged?.Invoke(1);
        }

        rt = state.Gamepad.RightTrigger / 255f;
        lt = state.Gamepad.LeftTrigger / 255f;
        lx = Math.Clamp((state.Gamepad.ThumbLX + 32768f) / 65535f, 0f, 1f);
        ly = Math.Clamp((state.Gamepad.ThumbLY + 32768f) / 65535f, 0f, 1f);
        return true;
    }

    /// <summary>Baca semua axis sekaligus.</summary>
    public XInputHelper.XInputState? GetFullReading(int deviceIndex = 0)
    {
        if (XInputHelper.GetState(deviceIndex, out var state))
            return state;
        return null;
    }

    public void Dispose()
    {
        // No unmanaged resources in XInput P/Invoke
    }
}

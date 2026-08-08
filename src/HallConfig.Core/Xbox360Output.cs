using System;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace HallConfig.Core;

/// <summary>
/// Virtual Xbox 360 controller output using ViGEmBus driver.
/// Provides native XInput device emulation with precise 0-255 trigger and -32768..32767 thumbstick mapping.
/// </summary>
public class Xbox360Output : IOutputDevice
{
    private ViGEmClient? _client;
    private IXbox360Controller? _controller;
    private bool _isAcquired;
    private bool _disposed;
    private readonly object _lock = new();

    public string Name => "Virtual Xbox 360 Controller (ViGEm)";
    public bool IsAcquired => _isAcquired;
    public IXbox360Controller? Controller => _controller;

    public bool Acquire()
    {
        lock (_lock)
        {
            if (_isAcquired) return true;

            try
            {
                _client = new ViGEmClient();
                _controller = _client.CreateXbox360Controller();
                _controller.Connect();
                _isAcquired = true;
                ResetToCenter();
                return true;
            }
            catch
            {
                Relinquish();
                return false;
            }
        }
    }

    public void Relinquish()
    {
        lock (_lock)
        {
            if (_controller != null)
            {
                try
                {
                    ResetToCenter();
                    _controller.Disconnect();
                }
                catch { }
                _controller = null;
            }

            if (_client != null)
            {
                try
                {
                    _client.Dispose();
                }
                catch { }
                _client = null;
            }

            _isAcquired = false;
        }
    }

    public bool SetAxisValue(string axisName, float normalizedValue)
    {
        if (!_isAcquired || _controller == null) return false;

        try
        {
            float clamped = Math.Clamp(normalizedValue, 0f, 1f);
            string src = axisName?.Trim().ToUpperInvariant() ?? "RT";

            switch (src)
            {
                case "LEFTTRIGGER" or "LT":
                    byte ltVal = (byte)Math.Clamp((int)Math.Round(clamped * 255f), 0, 255);
                    _controller.SetSliderValue(Xbox360Slider.LeftTrigger, ltVal);
                    break;

                case "RIGHTTRIGGER" or "RT":
                    byte rtVal = (byte)Math.Clamp((int)Math.Round(clamped * 255f), 0, 255);
                    _controller.SetSliderValue(Xbox360Slider.RightTrigger, rtVal);
                    break;

                case "LEFTSTICKX" or "LX" or "X":
                    short lxVal = (short)Math.Clamp((int)Math.Round((clamped * 65535f) - 32768f), -32768, 32767);
                    _controller.SetAxisValue(Xbox360Axis.LeftThumbX, lxVal);
                    break;

                case "LEFTSTICKY" or "LY" or "Y":
                    short lyVal = (short)Math.Clamp((int)Math.Round((clamped * 65535f) - 32768f), -32768, 32767);
                    _controller.SetAxisValue(Xbox360Axis.LeftThumbY, lyVal);
                    break;

                case "RIGHTSTICKX" or "RX":
                    short rxVal = (short)Math.Clamp((int)Math.Round((clamped * 65535f) - 32768f), -32768, 32767);
                    _controller.SetAxisValue(Xbox360Axis.RightThumbX, rxVal);
                    break;

                case "RIGHTSTICKY" or "RY":
                    short ryVal = (short)Math.Clamp((int)Math.Round((clamped * 65535f) - 32768f), -32768, 32767);
                    _controller.SetAxisValue(Xbox360Axis.RightThumbY, ryVal);
                    break;

                default:
                    return false;
            }

            _controller.SubmitReport();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool UpdateAllAxes(float lx, float ly, float lt, float rt)
    {
        return UpdateFullState(lx, ly, lt, rt, 0, 0, 0);
    }

    public bool UpdateFullState(float lx, float ly, float lt, float rt, short rawRx, short rawRy, ushort buttons)
    {
        if (!_isAcquired || _controller == null) return false;

        try
        {
            // Triggers: 0.0 -> 0, 1.0 -> 255
            byte ltVal = (byte)Math.Clamp((int)Math.Round(lt * 255f), 0, 255);
            byte rtVal = (byte)Math.Clamp((int)Math.Round(rt * 255f), 0, 255);

            // Left Stick: 0.0 -> -32768, 0.5 -> 0, 1.0 -> 32767
            short lxVal = (short)Math.Clamp((int)Math.Round((lx * 65535f) - 32768f), -32768, 32767);
            short lyVal = (short)Math.Clamp((int)Math.Round((ly * 65535f) - 32768f), -32768, 32767);

            _controller.SetSliderValue(Xbox360Slider.LeftTrigger, ltVal);
            _controller.SetSliderValue(Xbox360Slider.RightTrigger, rtVal);
            _controller.SetAxisValue(Xbox360Axis.LeftThumbX, lxVal);
            _controller.SetAxisValue(Xbox360Axis.LeftThumbY, lyVal);

            // Right Stick: Passthrough raw short without SignalProcessor
            _controller.SetAxisValue(Xbox360Axis.RightThumbX, rawRx);
            _controller.SetAxisValue(Xbox360Axis.RightThumbY, rawRy);

            // Buttons & D-Pad: Direct passthrough of XInput bitmask
            _controller.SetButtonState(Xbox360Button.A, (buttons & 0x1000) != 0);
            _controller.SetButtonState(Xbox360Button.B, (buttons & 0x2000) != 0);
            _controller.SetButtonState(Xbox360Button.X, (buttons & 0x4000) != 0);
            _controller.SetButtonState(Xbox360Button.Y, (buttons & 0x8000) != 0);

            _controller.SetButtonState(Xbox360Button.LeftShoulder, (buttons & 0x0100) != 0);
            _controller.SetButtonState(Xbox360Button.RightShoulder, (buttons & 0x0200) != 0);

            _controller.SetButtonState(Xbox360Button.Start, (buttons & 0x0010) != 0);
            _controller.SetButtonState(Xbox360Button.Back, (buttons & 0x0020) != 0);

            _controller.SetButtonState(Xbox360Button.LeftThumb, (buttons & 0x0040) != 0);
            _controller.SetButtonState(Xbox360Button.RightThumb, (buttons & 0x0080) != 0);

            _controller.SetButtonState(Xbox360Button.Up, (buttons & 0x0001) != 0);
            _controller.SetButtonState(Xbox360Button.Down, (buttons & 0x0002) != 0);
            _controller.SetButtonState(Xbox360Button.Left, (buttons & 0x0004) != 0);
            _controller.SetButtonState(Xbox360Button.Right, (buttons & 0x0008) != 0);

            _controller.SubmitReport();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void ResetToCenter()
    {
        if (!_isAcquired || _controller == null) return;

        try
        {
            _controller.SetSliderValue(Xbox360Slider.LeftTrigger, 0);
            _controller.SetSliderValue(Xbox360Slider.RightTrigger, 0);
            _controller.SetAxisValue(Xbox360Axis.LeftThumbX, 0);
            _controller.SetAxisValue(Xbox360Axis.LeftThumbY, 0);
            _controller.SetAxisValue(Xbox360Axis.RightThumbX, 0);
            _controller.SetAxisValue(Xbox360Axis.RightThumbY, 0);

            // Clear all buttons
            _controller.SetButtonState(Xbox360Button.A, false);
            _controller.SetButtonState(Xbox360Button.B, false);
            _controller.SetButtonState(Xbox360Button.X, false);
            _controller.SetButtonState(Xbox360Button.Y, false);
            _controller.SetButtonState(Xbox360Button.LeftShoulder, false);
            _controller.SetButtonState(Xbox360Button.RightShoulder, false);
            _controller.SetButtonState(Xbox360Button.Start, false);
            _controller.SetButtonState(Xbox360Button.Back, false);
            _controller.SetButtonState(Xbox360Button.LeftThumb, false);
            _controller.SetButtonState(Xbox360Button.RightThumb, false);
            _controller.SetButtonState(Xbox360Button.Up, false);
            _controller.SetButtonState(Xbox360Button.Down, false);
            _controller.SetButtonState(Xbox360Button.Left, false);
            _controller.SetButtonState(Xbox360Button.Right, false);

            _controller.SubmitReport();
        }
        catch { }
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

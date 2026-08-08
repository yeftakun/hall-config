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
        if (!_isAcquired || _controller == null) return false;

        try
        {
            // Triggers: 0.0 -> 0, 1.0 -> 255
            byte ltVal = (byte)Math.Clamp((int)Math.Round(lt * 255f), 0, 255);
            byte rtVal = (byte)Math.Clamp((int)Math.Round(rt * 255f), 0, 255);

            // Thumbsticks: 0.0 -> -32768, 0.5 -> 0, 1.0 -> 32767
            short lxVal = (short)Math.Clamp((int)Math.Round((lx * 65535f) - 32768f), -32768, 32767);
            short lyVal = (short)Math.Clamp((int)Math.Round((ly * 65535f) - 32768f), -32768, 32767);

            _controller.SetSliderValue(Xbox360Slider.LeftTrigger, ltVal);
            _controller.SetSliderValue(Xbox360Slider.RightTrigger, rtVal);
            _controller.SetAxisValue(Xbox360Axis.LeftThumbX, lxVal);
            _controller.SetAxisValue(Xbox360Axis.LeftThumbY, lyVal);

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

using System;
using System.Runtime.InteropServices;

namespace HallConfig.Core;

/// <summary>
/// Simple P/Invoke wrapper untuk XInput (xinput1_4.dll).
/// Mendukung Xbox 360, Xbox One, dan controller XInput generik.
/// Tidak memerlukan NuGet tambahan – DLL sudah tersedia di semua Windows 8+.
/// </summary>
public static class XInputHelper
{
    private const string DllName = "xinput1_4.dll";

    [DllImport(DllName, EntryPoint = "XInputGetState", SetLastError = false)]
    private static extern int NativeGetState(int dwUserIndex, out XInputState pState);

    [DllImport(DllName, EntryPoint = "XInputSetState", SetLastError = false)]
    private static extern int NativeSetState(int dwUserIndex, ref XInputVibration pVibration);

    [DllImport(DllName, EntryPoint = "XInputGetCapabilities", SetLastError = false)]
    private static extern int NativeGetCapabilities(int dwUserIndex, int dwFlags, out XInputCapabilities pCapabilities);

    [StructLayout(LayoutKind.Sequential)]
    public struct XInputState
    {
        public uint PacketNumber;
        public XInputGamepad Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XInputGamepad
    {
        public ushort Buttons;
        public byte LeftTrigger;   // 0–255
        public byte RightTrigger;  // 0–255
        public short ThumbLX;      // -32768 – 32767
        public short ThumbLY;
        public short ThumbRX;
        public short ThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XInputVibration
    {
        public ushort wLeftMotorSpeed;
        public ushort wRightMotorSpeed;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XInputCapabilities
    {
        public byte Type;
        public byte SubType;
        public ushort Flags;
        public XInputGamepad Gamepad;
        public XInputVibration Vibration;
    }

    public const int MaxControllers = 4;

    /// <summary>Ambil state mentah XInput. Return false kalau controller tidak terhubung.</summary>
    public static bool GetState(int userIndex, out XInputState state)
    {
        try
        {
            int result = NativeGetState(userIndex, out state);
            return result == 0; // ERROR_SUCCESS
        }
        catch
        {
            state = default;
            return false;
        }
    }

    /// <summary>Trigger kiri ternormalisasi 0.0–1.0. Return 0 kalau tidak terhubung.</summary>
    public static float GetLeftTrigger(int userIndex)
    {
        if (!GetState(userIndex, out var state)) return 0f;
        return state.Gamepad.LeftTrigger / 255f;
    }

    /// <summary>Trigger kanan ternormalisasi 0.0–1.0. Return 0 kalau tidak terhubung.</summary>
    public static float GetRightTrigger(int userIndex)
    {
        if (!GetState(userIndex, out var state)) return 0f;
        return state.Gamepad.RightTrigger / 255f;
    }

    /// <summary>Left stick X ternormalisasi -1.0–1.0.</summary>
    public static float GetLeftStickX(int userIndex)
    {
        if (!GetState(userIndex, out var state)) return 0f;
        return state.Gamepad.ThumbLX / 32767f;
    }

    /// <summary>Left stick Y ternormalisasi -1.0–1.0 (positif = atas).</summary>
    public static float GetLeftStickY(int userIndex)
    {
        if (!GetState(userIndex, out var state)) return 0f;
        return state.Gamepad.ThumbLY / 32767f;
    }

    /// <summary>Cek apakah ada controller yang terhubung di salah satu slot 0–3.</summary>
    public static int FindFirstConnected()
    {
        for (int i = 0; i < MaxControllers; i++)
        {
            if (GetState(i, out _)) return i;
        }
        return -1;
    }

    /// <summary>Cek apakah slot controller tertentu terhubung.</summary>
    public static bool IsConnected(int userIndex)
    {
        return GetState(userIndex, out _);
    }

    /// <summary>Kirim feedback vibrasi ke controller (nilai 0.0 - 1.0)</summary>
    public static bool SetVibration(int userIndex, float leftMotor, float rightMotor)
    {
        try
        {
            var vib = new XInputVibration
            {
                wLeftMotorSpeed = (ushort)Math.Clamp(Math.Round(leftMotor * 65535f), 0, 65535),
                wRightMotorSpeed = (ushort)Math.Clamp(Math.Round(rightMotor * 65535f), 0, 65535)
            };
            int result = NativeSetState(userIndex, ref vib);
            return result == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Cek apakah controller fisik mendukung vibrasi</summary>
    public static bool SupportsVibration(int userIndex)
    {
        try
        {
            int result = NativeGetCapabilities(userIndex, 0, out var caps);
            if (result == 0)
            {
                // XINPUT_CAPS_FFB_SUPPORTED = 0x0002
                // Sebagian besar controller Xbox 360/One standar yang memiliki motor akan punya flag ini atau motor speed > 0.
                return (caps.Flags & 0x0002) != 0 || caps.Vibration.wLeftMotorSpeed > 0 || caps.Vibration.wRightMotorSpeed > 0;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }
}

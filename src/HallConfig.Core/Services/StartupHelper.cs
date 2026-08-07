using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace HallConfig.Core.Services;

public static class StartupHelper
{
    private const string RegistryRunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppKeyName = "HallConfig";

    public static bool IsRunOnStartupEnabled()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return false;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryRunPath, false);
            var value = key?.GetValue(AppKeyName) as string;
            return !string.IsNullOrEmpty(value);
        }
        catch
        {
            return false;
        }
    }

    public static bool SetRunOnStartup(bool enable, bool startMinimized = true)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return false;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryRunPath, true);
            if (key == null) return false;

            if (enable)
            {
                string? exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                {
                    exePath = Process.GetCurrentProcess().MainModule?.FileName;
                }

                if (string.IsNullOrEmpty(exePath)) return false;

                string command = startMinimized
                    ? $"\"{exePath}\" --minimized"
                    : $"\"{exePath}\"";

                key.SetValue(AppKeyName, command);
            }
            else
            {
                key.DeleteValue(AppKeyName, false);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}

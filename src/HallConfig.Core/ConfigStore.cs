using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using HallConfig.Core.Models;

namespace HallConfig.Core;

public class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>All axis sources for which we pre-populate per-axis configs.</summary>
    public static readonly IReadOnlyList<string> KnownAxisSources =
        new[] { "RightTrigger", "LeftTrigger", "LeftStickX", "LeftStickY" };

    public string FilePath { get; }

    public ConfigStore(string? filePath = null)
    {
        FilePath = filePath ?? GetDefaultConfigPath();
    }

    public static string GetDefaultConfigPath()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "HallConfig", "config.json");
    }

    public static AppConfig CreateDefault()
    {
        var cfg = new AppConfig
        {
            DeviceIndex  = 0,
            AxisSource   = "RightTrigger",
            VJoyDeviceId = 1,
            VJoyAxis     = "X",
            Smoothing    = new SmoothingConfig  { Enabled = true,  Alpha = 0.4f },
            Hysteresis   = new HysteresisConfig { Enabled = true,  ThresholdUp = 0.27f, ThresholdDown = 0.23f },
            PollingHz    = 250
        };
        EnsureAxisConfigsPopulated(cfg);
        return cfg;
    }

    public AppConfig Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                string json   = File.ReadAllText(FilePath);
                var    parsed = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
                if (parsed != null)
                {
                    // Migrate old format (no axisConfigs) and fill missing axes.
                    EnsureAxisConfigsPopulated(parsed);
                    return parsed;
                }
            }
        }
        catch
        {
            // fall through to default
        }

        var defaultConfig = CreateDefault();
        Save(defaultConfig);
        return defaultConfig;
    }

    public void Save(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        string? dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        string json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(FilePath, json);
    }

    // ─── Internal helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Ensures every known axis source has an entry in config.AxisConfigs.
    /// For axes that were missing (e.g., old format config), the global
    /// Smoothing/Hysteresis values are used as the starting template.
    /// </summary>
    private static void EnsureAxisConfigsPopulated(AppConfig cfg)
    {
        foreach (var axis in KnownAxisSources)
        {
            if (!cfg.AxisConfigs.ContainsKey(axis))
            {
                cfg.AxisConfigs[axis] = new AxisProcessorConfig
                {
                    Smoothing  = new SmoothingConfig
                    {
                        Enabled = cfg.Smoothing.Enabled,
                        Alpha   = cfg.Smoothing.Alpha
                    },
                    Hysteresis = new HysteresisConfig
                    {
                        Enabled       = cfg.Hysteresis.Enabled,
                        ThresholdUp   = cfg.Hysteresis.ThresholdUp,
                        ThresholdDown = cfg.Hysteresis.ThresholdDown
                    }
                };
            }
        }
    }
}

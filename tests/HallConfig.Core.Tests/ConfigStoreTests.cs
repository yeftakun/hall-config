using System;
using System.IO;
using HallConfig.Core;
using HallConfig.Core.Models;
using Xunit;

namespace HallConfig.Core.Tests;

public class ConfigStoreTests : IDisposable
{
    private readonly string _tempDirectory;

    public ConfigStoreTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "HallConfigTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, true);
            }
        }
        catch { }
    }

    [Fact]
    public void CreateDefault_HasExpectedValuesFromPlan()
    {
        var cfg = ConfigStore.CreateDefault();

        Assert.Equal(0, cfg.DeviceIndex);
        Assert.Equal("RightTrigger", cfg.AxisSource);
        Assert.Equal(1u, cfg.VJoyDeviceId);
        Assert.Equal("X", cfg.VJoyAxis);
        Assert.Equal(250, cfg.PollingHz);

        Assert.True(cfg.Smoothing.Enabled);
        Assert.Equal(0.4f, cfg.Smoothing.Alpha);

        Assert.True(cfg.Hysteresis.Enabled);
        Assert.Equal(0.27f, cfg.Hysteresis.ThresholdUp);
        Assert.Equal(0.23f, cfg.Hysteresis.ThresholdDown);
    }

    [Fact]
    public void Load_WhenFileDoesNotExist_CreatesDefaultConfigFileAndReturnsIt()
    {
        string filePath = Path.Combine(_tempDirectory, "config.json");
        var store = new ConfigStore(filePath);

        Assert.False(File.Exists(filePath));

        var loaded = store.Load();

        Assert.True(File.Exists(filePath));
        Assert.NotNull(loaded);
        Assert.Equal(0.4f, loaded.Smoothing.Alpha);
        Assert.Equal(0.27f, loaded.Hysteresis.ThresholdUp);
        Assert.Equal(250, loaded.PollingHz);
    }

    [Fact]
    public void Save_ThenLoad_PersistsCustomValuesCorrectly()
    {
        string filePath = Path.Combine(_tempDirectory, "custom_config.json");
        var store = new ConfigStore(filePath);

        var custom = new AppConfig
        {
            DeviceIndex = 1,
            AxisSource = "LeftTrigger",
            VJoyDeviceId = 2,
            VJoyAxis = "Y",
            PollingHz = 500,
            Smoothing = new SmoothingConfig
            {
                Enabled = false,
                Alpha = 0.85f
            },
            Hysteresis = new HysteresisConfig
            {
                Enabled = true,
                ThresholdUp = 0.30f,
                ThresholdDown = 0.15f
            }
        };

        store.Save(custom);

        var loaded = store.Load();

        Assert.NotNull(loaded);
        Assert.Equal(1, loaded.DeviceIndex);
        Assert.Equal("LeftTrigger", loaded.AxisSource);
        Assert.Equal(2u, loaded.VJoyDeviceId);
        Assert.Equal("Y", loaded.VJoyAxis);
        Assert.Equal(500, loaded.PollingHz);
        Assert.False(loaded.Smoothing.Enabled);
        Assert.Equal(0.85f, loaded.Smoothing.Alpha);
        Assert.True(loaded.Hysteresis.Enabled);
        Assert.Equal(0.30f, loaded.Hysteresis.ThresholdUp);
        Assert.Equal(0.15f, loaded.Hysteresis.ThresholdDown);
    }

    [Fact]
    public void AppConfig_ToProcessorConfig_MapsPropertiesCorrectly()
    {
        var appConfig = new AppConfig
        {
            Smoothing  = new SmoothingConfig  { Enabled = true, Alpha = 0.6f },
            Hysteresis = new HysteresisConfig { Enabled = true, ThresholdUp = 0.25f, ThresholdDown = 0.20f }
        };

        var procConfig = appConfig.ToProcessorConfig();

        Assert.True(procConfig.Smoothing.Enabled);
        Assert.Equal(0.6f,  procConfig.Smoothing.Alpha);
        Assert.True(procConfig.Hysteresis.Enabled);
        Assert.Equal(0.25f, procConfig.Hysteresis.ThresholdUp);
        Assert.Equal(0.20f, procConfig.Hysteresis.ThresholdDown);
    }

    [Fact]
    public void CreateDefault_PopulatesAxisConfigsForAllKnownAxes()
    {
        var cfg = ConfigStore.CreateDefault();

        foreach (var axis in ConfigStore.KnownAxisSources)
        {
            Assert.True(cfg.AxisConfigs.ContainsKey(axis),
                $"AxisConfigs should contain key '{axis}' after CreateDefault()");
        }
    }

    [Fact]
    public void AxisConfigs_AreIndependentPerAxis_ChangingOneDoesNotAffectOther()
    {
        var cfg = ConfigStore.CreateDefault();

        // Customize RT
        cfg.AxisConfigs["RightTrigger"].Smoothing.Alpha = 0.9f;
        cfg.AxisConfigs["RightTrigger"].Hysteresis.ThresholdUp = 0.50f;

        // LT should still have default values
        var lt = cfg.AxisConfigs["LeftTrigger"];
        Assert.Equal(0.4f,  lt.Smoothing.Alpha,  precision: 3);
        Assert.Equal(0.27f, lt.Hysteresis.ThresholdUp, precision: 3);
    }

    [Fact]
    public void AxisConfigs_PersistAcrossSaveAndLoad()
    {
        string filePath = Path.Combine(_tempDirectory, "per_axis_config.json");
        var store = new ConfigStore(filePath);

        var cfg = ConfigStore.CreateDefault();
        cfg.AxisConfigs["LeftTrigger"].Smoothing.Alpha = 0.75f;
        cfg.AxisConfigs["LeftTrigger"].Hysteresis.ThresholdUp = 0.40f;
        cfg.AxisConfigs["LeftTrigger"].Hysteresis.ThresholdDown = 0.35f;

        store.Save(cfg);
        var loaded = store.Load();

        Assert.True(loaded.AxisConfigs.ContainsKey("LeftTrigger"));
        var lt = loaded.AxisConfigs["LeftTrigger"];
        Assert.Equal(0.75f, lt.Smoothing.Alpha, precision: 3);
        Assert.Equal(0.40f, lt.Hysteresis.ThresholdUp, precision: 3);
        Assert.Equal(0.35f, lt.Hysteresis.ThresholdDown, precision: 3);

        // Other axes should still have defaults
        Assert.Equal(0.4f, loaded.AxisConfigs["RightTrigger"].Smoothing.Alpha, precision: 3);
    }

    [Fact]
    public void Load_OldFormatWithoutAxisConfigs_MigratesGlobalConfigToAllAxes()
    {
        string filePath = Path.Combine(_tempDirectory, "old_format.json");
        // Write an old-format config without axisConfigs key
        File.WriteAllText(filePath, """
            {
              "deviceIndex": 0,
              "axisSource": "RightTrigger",
              "vjoyDeviceId": 1,
              "vjoyAxis": "X",
              "smoothing": { "enabled": true, "alpha": 0.55 },
              "hysteresis": { "enabled": false, "thresholdUp": 0.30, "thresholdDown": 0.25 },
              "pollingHz": 250
            }
            """);

        var store = new ConfigStore(filePath);
        var loaded = store.Load();

        // All known axes should be populated with the legacy values
        foreach (var axis in ConfigStore.KnownAxisSources)
        {
            Assert.True(loaded.AxisConfigs.ContainsKey(axis));
            Assert.Equal(0.55f, loaded.AxisConfigs[axis].Smoothing.Alpha, precision: 2);
            Assert.False(loaded.AxisConfigs[axis].Hysteresis.Enabled);
            Assert.Equal(0.30f, loaded.AxisConfigs[axis].Hysteresis.ThresholdUp, precision: 2);
        }
    }
}

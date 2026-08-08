using System;
using HallConfig.Core;
using HallConfig.Core.Models;
using Xunit;

namespace HallConfig.Core.Tests;

public class OutputDeviceTests
{
    [Fact]
    public void AppConfig_DefaultOutputMode_IsXbox360()
    {
        var config = new AppConfig();
        Assert.Equal("Xbox360", config.OutputMode);
    }

    [Theory]
    [InlineData("Xbox360", typeof(Xbox360Output))]
    [InlineData("xbox360", typeof(Xbox360Output))]
    [InlineData("vJoy", typeof(VJoyOutput))]
    [InlineData("VJOY", typeof(VJoyOutput))]
    public void PipelineEngine_CreateOutputDevice_SelectsCorrectType(string mode, Type expectedType)
    {
        var config = new AppConfig { OutputMode = mode };
        using var device = PipelineEngine.CreateOutputDevice(config);
        Assert.NotNull(device);
        Assert.IsType(expectedType, device);
    }

    [Fact]
    public void VJoyOutput_ImplementsIOutputDevice()
    {
        using var vjoy = new VJoyOutput(1, VJoyAxisType.X);
        Assert.IsAssignableFrom<IOutputDevice>(vjoy);
        Assert.Contains("vJoy Device #1", vjoy.Name);
    }

    [Fact]
    public void Xbox360Output_ImplementsIOutputDevice()
    {
        using var xbox = new Xbox360Output();
        Assert.IsAssignableFrom<IOutputDevice>(xbox);
        Assert.Contains("Xbox 360", xbox.Name);
    }

    [Fact]
    public void Xbox360Output_WhenNotAcquired_ReturnsFalseGracefully()
    {
        using var xbox = new Xbox360Output();
        // Without acquiring, SetAxisValue, UpdateAllAxes, and UpdateFullState should return false safely without throwing
        Assert.False(xbox.IsAcquired);
        Assert.False(xbox.SetAxisValue("RT", 0.75f));
        Assert.False(xbox.UpdateAllAxes(0.5f, 0.5f, 0.0f, 0.0f));
        Assert.False(xbox.UpdateFullState(0.5f, 0.5f, 0.0f, 0.0f, 1000, -1000, 0x1000));
    }

    [Fact]
    public void VJoyOutput_WhenNotAcquired_ReturnsFalseGracefully()
    {
        using var vjoy = new VJoyOutput(1, VJoyAxisType.X);
        Assert.False(vjoy.IsAcquired);
        Assert.False(vjoy.UpdateFullState(0.5f, 0.5f, 0.0f, 0.0f, 1000, -1000, 0x1000));
    }

    [Fact]
    public void InputReader_ReadAllInputs_HandlesDisconnectedController()
    {
        using var reader = new InputReader();
        // Slot 3 is almost certainly disconnected
        bool ok = reader.ReadAllInputs(3, out float rt, out float lt, out float lx, out float ly, out short rx, out short ry, out ushort buttons);
        if (!ok)
        {
            Assert.Equal(0f, rt);
            Assert.Equal(0f, lt);
            Assert.Equal(0.5f, lx);
            Assert.Equal(0.5f, ly);
            Assert.Equal(0, rx);
            Assert.Equal(0, ry);
            Assert.Equal(0, buttons);
        }
    }

    [Fact]
    public void ConfigStore_SavesAndLoads_OutputModeCorrectly()
    {
        var store = new ConfigStore("test_output_mode_config.json");
        try
        {
            var cfg = new AppConfig { OutputMode = "vJoy" };
            store.Save(cfg);

            var loaded = store.Load();
            Assert.Equal("vJoy", loaded.OutputMode);

            cfg.OutputMode = "Xbox360";
            store.Save(cfg);

            loaded = store.Load();
            Assert.Equal("Xbox360", loaded.OutputMode);
        }
        finally
        {
            if (System.IO.File.Exists(store.FilePath))
            {
                System.IO.File.Delete(store.FilePath);
            }
        }
    }
}

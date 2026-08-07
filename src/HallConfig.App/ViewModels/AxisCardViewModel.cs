using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using HallConfig.Core.Models;

namespace HallConfig.App.ViewModels;

public class AxisCardViewModel : INotifyPropertyChanged
{
    private readonly Action<string> _onSelected;

    public string Key { get; }
    public string DisplayName { get; }
    public string ShortName { get; }
    public string Subtitle { get; }

    public ICommand SelectCommand { get; }

    public AxisCardViewModel(string key, string displayName, string shortName, string subtitle, Action<string> onSelected)
    {
        Key = key;
        DisplayName = displayName;
        ShortName = shortName;
        Subtitle = subtitle;
        _onSelected = onSelected;
        SelectCommand = new RelayCommand(() => _onSelected(Key));
    }

    private float _rawValue;
    public float RawValue
    {
        get => _rawValue;
        set => SetField(ref _rawValue, value);
    }

    private float _processedValue;
    public float ProcessedValue
    {
        get => _processedValue;
        set => SetField(ref _processedValue, value);
    }

    private bool _isActive;
    public bool IsActive
    {
        get => _isActive;
        set => SetField(ref _isActive, value);
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    private string _configSummary = "";
    public string ConfigSummary
    {
        get => _configSummary;
        set => SetField(ref _configSummary, value);
    }

    public void UpdateConfigSummary(AxisProcessorConfig cfg)
    {
        string smoothText = cfg.Smoothing.Enabled ? $"Smooth α={cfg.Smoothing.Alpha:F2}" : "Smooth OFF";
        string hystText = cfg.Hysteresis.Enabled ? $"Hyst {cfg.Hysteresis.ThresholdDown:F2}–{cfg.Hysteresis.ThresholdUp:F2}" : "Hyst OFF";
        ConfigSummary = $"{smoothText}  •  {hystText}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (System.Collections.Generic.EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}

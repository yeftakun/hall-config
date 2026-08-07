using System;
using System.Windows.Input;

namespace HallConfig.App.ViewModels;

/// <summary>Simple ICommand implementation for ViewModel commands.</summary>
public class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => _execute();

    /// <summary>Manually trigger CanExecute re-evaluation.</summary>
    public void RaiseCanExecuteChanged() =>
        CommandManager.InvalidateRequerySuggested();
}

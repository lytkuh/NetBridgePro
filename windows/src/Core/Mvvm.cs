using System.ComponentModel;
using System.Windows.Input;

namespace NetBridge.Windows.Core;

/// <summary>Minimal INotifyPropertyChanged base (no external MVVM libs).</summary>
public abstract class ObservableBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Raise(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected void Set<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        Raise(name!);
    }
}

/// <summary>Simple synchronous command.</summary>
public class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute == null ? null : new Func<object?, bool>(_ => canExecute()))
    { }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => _execute(parameter);

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public static void Refresh() => CommandManager.InvalidateRequerySuggested();
}

/// <summary>Async command with busy flag and optional cancellation support.</summary>
public class AsyncRelayCommand : ICommand
{
    private readonly Func<object?, Task> _execute;
    private readonly Func<object?, bool>? _canExecute;
    private bool _busy;

    public AsyncRelayCommand(Func<object?, Task> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public bool IsBusy
    {
        get => _busy;
        private set { _busy = value; CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
    }

    public bool CanExecute(object? parameter) => !_busy && (_canExecute?.Invoke(parameter) ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        IsBusy = true;
        try { await _execute(parameter); }
        catch (Exception ex) { Logger.Error("Command", ex.Message); }
        finally { IsBusy = false; }
    }

    public event EventHandler? CanExecuteChanged;
}

using System.Windows.Input;
using System.Diagnostics;

namespace AtomBox.Desktop.ViewModels;

public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<object?, Task> _executeAsync;
    private readonly Predicate<object?>? _canExecute;
    private readonly bool _allowConcurrentExecutions;
    private bool _isRunning;

    public AsyncRelayCommand(
        Func<object?, Task> executeAsync,
        Predicate<object?>? canExecute = null,
        bool allowConcurrentExecutions = false)
    {
        _executeAsync = executeAsync;
        _canExecute = canExecute;
        _allowConcurrentExecutions = allowConcurrentExecutions;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        return (_allowConcurrentExecutions || !_isRunning) && (_canExecute?.Invoke(parameter) ?? true);
    }

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        try
        {
            if (!_allowConcurrentExecutions)
            {
                _isRunning = true;
                RaiseCanExecuteChanged();
            }

            await _executeAsync(parameter).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
        finally
        {
            if (!_allowConcurrentExecutions)
            {
                _isRunning = false;
                RaiseCanExecuteChanged();
            }
        }
    }

    public void RaiseCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}

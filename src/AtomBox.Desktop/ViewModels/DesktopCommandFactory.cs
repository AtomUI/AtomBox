using AtomUI.City.Mvvm;
using AtomUI.City.Presentation;
using CommunityToolkit.Mvvm.Input;

namespace AtomBox.Desktop.ViewModels;

public sealed class DesktopCommandFactory
{
    private readonly ICommandExceptionHandler _exceptionHandler;
    private readonly IUiCommandFactory _uiCommands;

    public DesktopCommandFactory(
        ICommandExceptionHandler exceptionHandler,
        IUiCommandFactory uiCommands)
    {
        ArgumentNullException.ThrowIfNull(exceptionHandler);
        ArgumentNullException.ThrowIfNull(uiCommands);

        _exceptionHandler = exceptionHandler;
        _uiCommands = uiCommands;
    }

    public IAsyncRelayCommand CreateAsync(
        IActivationScopeOwner owner,
        string commandName,
        Func<CancellationToken, Task> execute,
        Func<bool>? canExecute = null,
        CommandConcurrencyPolicy concurrencyPolicy = CommandConcurrencyPolicy.Reject)
    {
        return _uiCommands.CreateAsync(
            execute,
            canExecute,
            new CommandExecutionState(commandName, owner.GetType()),
            owner,
            concurrencyPolicy,
            _exceptionHandler);
    }

    public IAsyncRelayCommand<T> CreateAsync<T>(
        IActivationScopeOwner owner,
        string commandName,
        Func<T?, CancellationToken, Task> execute,
        Predicate<T?>? canExecute = null,
        CommandConcurrencyPolicy concurrencyPolicy = CommandConcurrencyPolicy.Reject,
        Func<T?, object?>? concurrencyKeySelector = null)
    {
        return _uiCommands.CreateAsync(
            execute,
            canExecute,
            new CommandExecutionState(commandName, owner.GetType()),
            owner,
            concurrencyPolicy,
            concurrencyKeySelector,
            _exceptionHandler);
    }

    public IRelayCommand Create(
        ViewModelBase owner,
        string commandName,
        Action execute,
        Func<bool>? canExecute = null)
    {
        return _uiCommands.Create(
            execute,
            canExecute,
            new CommandExecutionState(commandName, owner.GetType()));
    }

    public IRelayCommand<T> Create<T>(
        ViewModelBase owner,
        string commandName,
        Action<T?> execute,
        Predicate<T?>? canExecute = null)
    {
        return _uiCommands.Create(
            execute,
            canExecute,
            new CommandExecutionState(commandName, owner.GetType()));
    }
}

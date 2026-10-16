using AtomUI.City.Core.Diagnostics;
using AtomUI.City.Mvvm;
using CommunityToolkit.Mvvm.Input;
using CityUiDispatcher = AtomUI.City.Core.Threading.IUiDispatcher;

namespace AtomBox.Desktop.ViewModels;

public sealed class DesktopCommandFactory
{
    private readonly ICommandExceptionHandler _exceptionHandler;
    private readonly IHostDiagnostics? _diagnostics;
    private readonly CityUiDispatcher _dispatcher;

    public DesktopCommandFactory(
        ICommandExceptionHandler exceptionHandler,
        CityUiDispatcher dispatcher,
        IHostDiagnostics? diagnostics = null)
    {
        _exceptionHandler = exceptionHandler;
        _dispatcher = dispatcher;
        _diagnostics = diagnostics;
    }

    public IAsyncRelayCommand CreateAsync(
        ViewModelBase owner,
        string commandName,
        Func<CancellationToken, Task> execute,
        Func<bool>? canExecute = null,
        CommandConcurrencyPolicy concurrencyPolicy = CommandConcurrencyPolicy.Reject)
    {
        return CommandFactory.CreateAsync(
            execute,
            canExecute,
            new CommandExecutionState(commandName, owner.GetType()),
            owner,
            concurrencyPolicy,
            _exceptionHandler,
            _diagnostics,
            _dispatcher);
    }

    public IAsyncRelayCommand<T> CreateAsync<T>(
        ViewModelBase owner,
        string commandName,
        Func<T?, CancellationToken, Task> execute,
        Predicate<T?>? canExecute = null,
        CommandConcurrencyPolicy concurrencyPolicy = CommandConcurrencyPolicy.Reject,
        Func<T?, object?>? concurrencyKeySelector = null)
    {
        return CommandFactory.CreateAsync(
            execute,
            canExecute,
            new CommandExecutionState(commandName, owner.GetType()),
            owner,
            concurrencyPolicy,
            concurrencyKeySelector,
            _exceptionHandler,
            _diagnostics,
            _dispatcher);
    }

    public IRelayCommand Create(
        ViewModelBase owner,
        string commandName,
        Action execute,
        Func<bool>? canExecute = null)
    {
        return CommandFactory.Create(
            execute,
            canExecute,
            new CommandExecutionState(commandName, owner.GetType()),
            _diagnostics);
    }

    public IRelayCommand<T> Create<T>(
        ViewModelBase owner,
        string commandName,
        Action<T?> execute,
        Predicate<T?>? canExecute = null)
    {
        return CommandFactory.Create(
            execute,
            canExecute,
            new CommandExecutionState(commandName, owner.GetType()),
            _diagnostics);
    }
}

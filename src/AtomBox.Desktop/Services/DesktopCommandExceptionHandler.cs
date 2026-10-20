using System.Diagnostics;
using AtomUI.City.Mvvm;
using CityUiDispatcher = AtomUI.City.Core.Threading.IUiDispatcher;

namespace AtomBox.Desktop.Services;

public sealed class DesktopCommandExceptionHandler : ICommandExceptionHandler
{
    private readonly IMessageService _messages;
    private readonly CityUiDispatcher _dispatcher;

    public DesktopCommandExceptionHandler(
        IMessageService messages,
        CityUiDispatcher dispatcher)
    {
        _messages = messages;
        _dispatcher = dispatcher;
    }

    public ValueTask HandleAsync(
        CommandExceptionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        return _dispatcher.PostAsync(
            _ =>
            {
                Debug.WriteLine(context.Exception);
                _messages.Error(context.Exception.Message);
                return ValueTask.CompletedTask;
            },
            cancellationToken);
    }
}

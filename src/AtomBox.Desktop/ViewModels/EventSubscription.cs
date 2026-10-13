namespace AtomBox.Desktop.ViewModels;

internal sealed class EventSubscription : IDisposable
{
    private Action? _unsubscribe;

    private EventSubscription(Action unsubscribe)
    {
        _unsubscribe = unsubscribe;
    }

    public static EventSubscription Create(Action unsubscribe)
    {
        ArgumentNullException.ThrowIfNull(unsubscribe);
        return new EventSubscription(unsubscribe);
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _unsubscribe, null)?.Invoke();
    }
}

namespace AtomBox.Desktop.Services;

public interface IUiDispatcher
{
    void Post(Action action);
}

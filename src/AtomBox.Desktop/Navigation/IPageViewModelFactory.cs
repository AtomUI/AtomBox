namespace AtomBox.Desktop.Navigation;

public interface IPageViewModelFactory
{
    object Create(NavigationTarget target, object? parameter);
}

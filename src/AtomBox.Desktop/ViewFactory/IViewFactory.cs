using Avalonia.Controls;

namespace AtomBox.Desktop.ViewFactory;

public interface IViewFactory
{
    Control CreateView(object viewModel);
}

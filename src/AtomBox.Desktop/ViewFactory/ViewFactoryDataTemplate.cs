using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace AtomBox.Desktop.ViewFactory;

public sealed class ViewFactoryDataTemplate : IDataTemplate
{
    private readonly IViewFactory _viewFactory;

    public ViewFactoryDataTemplate(IViewFactory viewFactory)
    {
        _viewFactory = viewFactory;
    }

    public Control? Build(object? param)
    {
        return param is null ? null : _viewFactory.CreateView(param);
    }

    public bool Match(object? data)
    {
        return data is not null;
    }
}

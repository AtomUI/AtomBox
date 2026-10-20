using AtomBox.Desktop.ViewModels.Pages;
using AtomUI.City.Presentation;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AtomBox.Desktop.Views.Home;

[ViewFor(typeof(HomeViewModel))]
public sealed partial class HomeView : UserControl, IViewDataContextAware
{
    public HomeView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

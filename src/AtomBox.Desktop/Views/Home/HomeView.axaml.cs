using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AtomBox.Desktop.Views.Home;

public sealed partial class HomeView : UserControl
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

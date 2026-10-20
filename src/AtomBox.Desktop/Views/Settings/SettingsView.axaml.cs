using AtomBox.Desktop.ViewModels.Pages;
using AtomUI.City.Presentation;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AtomBox.Desktop.Views.Settings;

[ViewFor(typeof(SettingsViewModel))]
public sealed partial class SettingsView : UserControl, IViewDataContextAware
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

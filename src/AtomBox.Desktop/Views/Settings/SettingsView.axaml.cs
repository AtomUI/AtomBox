using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AtomBox.Desktop.Views.Settings;

public sealed partial class SettingsView : UserControl
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

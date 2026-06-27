using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace AtomBox.Desktop.Views.Accounts;

public sealed partial class AccountManagementView : UserControl
{
    private static readonly IBrush RowBackground = SolidColorBrush.Parse("#FFFFFF");
    private static readonly IBrush RowPointerOverBackground = SolidColorBrush.Parse("#EAF2FF");

    public AccountManagementView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void HandleAccountRowPointerEntered(object? sender, PointerEventArgs e)
    {
        SetAccountRowBackground(sender, RowPointerOverBackground);
    }

    private void HandleAccountRowPointerMoved(object? sender, PointerEventArgs e)
    {
        SetAccountRowBackground(sender, RowPointerOverBackground);
    }

    private void HandleAccountRowPointerExited(object? sender, PointerEventArgs e)
    {
        SetAccountRowBackground(sender, RowBackground);
    }

    private static void SetAccountRowBackground(object? sender, IBrush background)
    {
        if (sender is not Border border)
        {
            return;
        }

        border.Background = background;
        if (border.Child is Panel panel)
        {
            panel.Background = background;
        }
    }
}

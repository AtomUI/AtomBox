using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace AtomBox.Desktop.Views.Transfers;

public sealed partial class TransferQueueView : UserControl
{
    private static readonly IBrush RowBackground = SolidColorBrush.Parse("#FFFFFF");
    private static readonly IBrush RowPointerOverBackground = SolidColorBrush.Parse("#EAF2FF");

    public TransferQueueView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void HandleTransferRowPointerEntered(object? sender, PointerEventArgs e)
    {
        SetRowBackground(sender, RowPointerOverBackground);
    }

    private void HandleTransferRowPointerMoved(object? sender, PointerEventArgs e)
    {
        SetRowBackground(sender, RowPointerOverBackground);
    }

    private void HandleTransferRowPointerExited(object? sender, PointerEventArgs e)
    {
        SetRowBackground(sender, RowBackground);
    }

    private static void SetRowBackground(object? sender, IBrush background)
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

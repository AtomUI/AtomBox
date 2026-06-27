using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Controls.Primitives;
using AtomBox.Desktop.ViewModels.Pages;
using AtomToolTip = AtomUI.Desktop.Controls.ToolTip;

namespace AtomBox.Desktop.Views.Transfers;

public sealed partial class TransferHistoryView : UserControl
{
    private const int PathTooltipDisplayLengthThreshold = 6;
    private static readonly IBrush RowBackground = SolidColorBrush.Parse("#FFFFFF");
    private static readonly IBrush RowPointerOverBackground = SolidColorBrush.Parse("#EAF2FF");

    public TransferHistoryView()
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

    private void HandlePathPointerEntered(object? sender, PointerEventArgs e)
    {
        ConfigurePathTooltip(sender);
    }

    private void HandlePathPointerMoved(object? sender, PointerEventArgs e)
    {
        ConfigurePathTooltip(sender);
    }

    private void HandlePathPointerExited(object? sender, PointerEventArgs e)
    {
        DisablePathTooltip(sender);
    }

    private static void DisablePathTooltip(object? sender)
    {
        if (sender is not Control control)
        {
            return;
        }

        AtomToolTip.RemoveToolTipOpeningHandler(control, HandlePathToolTipOpening);
        AtomToolTip.SetTip(control, null);
        AtomToolTip.SetServiceEnabled(control, false);
    }

    private static void ConfigurePathTooltip(object? sender)
    {
        if (sender is not Control control ||
            control.DataContext is not TransferHistoryRowViewModel row ||
            GetDisplayLength(row.TargetOrSource) <= PathTooltipDisplayLengthThreshold)
        {
            DisablePathTooltip(sender);
            return;
        }

        AtomToolTip.SetPlacement(control, PlacementMode.Top);
        AtomToolTip.SetMarginToAnchor(control, 6);
        AtomToolTip.SetShowDelay(control, 350);
        AtomToolTip.SetIsArrowVisible(control, true);
        AtomToolTip.SetServiceEnabled(control, true);
        AtomToolTip.RemoveToolTipOpeningHandler(control, HandlePathToolTipOpening);
        AtomToolTip.AddToolTipOpeningHandler(control, HandlePathToolTipOpening);
        AtomToolTip.SetTip(control, new AtomToolTip
        {
            Content = new AtomUI.Desktop.Controls.TextBlock
            {
                Text = row.TargetOrSource,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 420
            }
        });
    }

    private static void HandlePathToolTipOpening(object? sender, CancelRoutedEventArgs e)
    {
        if (sender is Control control)
        {
            AtomToolTip.SetIsArrowVisible(control, true);
            AtomToolTip.SetPlacement(control, PlacementMode.Top);
            AtomToolTip.SetMarginToAnchor(control, 6);
        }
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

    private static int GetDisplayLength(string text)
    {
        var length = 0;
        foreach (var character in text)
        {
            length += character > 127 ? 2 : 1;
        }

        return length;
    }
}

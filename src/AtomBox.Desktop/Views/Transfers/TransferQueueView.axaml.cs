using AtomBox.Desktop.ViewModels.Pages;
using AtomUI.City.Presentation;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AtomBox.Desktop.Views.Transfers;

[ViewFor(typeof(TransferQueueViewModel))]
public sealed partial class TransferQueueView : UserControl, IViewDataContextAware
{
    public TransferQueueView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

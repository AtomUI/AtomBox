using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using AtomBox.Desktop.ViewModels.Pages;
using AtomUI.Desktop.Controls;

namespace AtomBox.Desktop.Views.RemoteBrowser;

public sealed partial class RemoteBrowserView : UserControl
{
    private static readonly IBrush RowBackground = SolidColorBrush.Parse("#FFFFFF");
    private static readonly IBrush RowPointerOverBackground = SolidColorBrush.Parse("#EAF2FF");
    private Border? _contextMenuRow;

    public RemoteBrowserView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void HandleRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is AtomUI.Desktop.Controls.CheckBox ||
            DataContext is not RemoteBrowserViewModel viewModel ||
            sender is not Control { DataContext: RemoteListRowViewModel row })
        {
            return;
        }

        if (viewModel.OpenRowCommand.CanExecute(row))
        {
            viewModel.OpenRowCommand.Execute(row);
            e.Handled = true;
        }
    }

    private void HandleRemoteAccountRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not RemoteBrowserViewModel viewModel ||
            sender is not Control { DataContext: RemoteAccountChoiceViewModel account })
        {
            return;
        }

        if (viewModel.OpenAccountCommand.CanExecute(account))
        {
            viewModel.OpenAccountCommand.Execute(account);
            e.Handled = true;
        }
    }

    private void HandleRemoteRowPointerEntered(object? sender, PointerEventArgs e)
    {
        SetRemoteRowBackground(sender, RowPointerOverBackground);
    }

    private void HandleRemoteRowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border border)
        {
            return;
        }

        var point = e.GetCurrentPoint(border);
        if (!point.Properties.IsRightButtonPressed)
        {
            return;
        }

        SetContextMenuRow(border);
    }

    private void HandleRemoteRowPointerMoved(object? sender, PointerEventArgs e)
    {
        SetRemoteRowBackground(sender, RowPointerOverBackground);
    }

    private void HandleRemoteRowPointerExited(object? sender, PointerEventArgs e)
    {
        if (ReferenceEquals(sender, _contextMenuRow))
        {
            return;
        }

        SetRemoteRowBackground(sender, RowBackground);
    }

    private void HandleRemoteRowContextMenuClosed(object? sender, RoutedEventArgs e)
    {
        if (_contextMenuRow is null)
        {
            return;
        }

        SetRemoteRowBackground(_contextMenuRow, RowBackground);
        _contextMenuRow = null;
    }

    private void SetContextMenuRow(Border border)
    {
        if (_contextMenuRow is not null && !ReferenceEquals(_contextMenuRow, border))
        {
            SetRemoteRowBackground(_contextMenuRow, RowBackground);
        }

        _contextMenuRow = border;
        SetRemoteRowBackground(border, RowPointerOverBackground);
    }

    private static void SetRemoteRowBackground(object? sender, IBrush background)
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

    private async void HandleBreadcrumbNavigateRequest(object? sender, BreadcrumbNavigateEventArgs e)
    {
        if (DataContext is not RemoteBrowserViewModel viewModel)
        {
            return;
        }

        await viewModel.NavigateBreadcrumbAsync(e.BreadcrumbItem.NavigateContext).ConfigureAwait(true);
    }

    private async void HandleSearchButtonClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is RemoteBrowserViewModel viewModel)
        {
            await viewModel.ApplySearchAsync().ConfigureAwait(true);
        }

        e.Handled = true;
    }

    private async void HandleSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not RemoteBrowserViewModel viewModel)
        {
            return;
        }

        await viewModel.ApplySearchAsync().ConfigureAwait(true);
        e.Handled = true;
    }

    private async void HandleSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (DataContext is RemoteBrowserViewModel viewModel)
        {
            await viewModel.RestoreListWhenSearchClearedAsync().ConfigureAwait(true);
        }
    }

    private void HandleRefreshFloatButtonClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not RemoteBrowserViewModel viewModel ||
            !viewModel.RefreshCommand.CanExecute(null))
        {
            return;
        }

        viewModel.RefreshCommand.Execute(null);
        e.Handled = true;
    }

    private void HandleUploadFloatButtonClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not RemoteBrowserViewModel viewModel ||
            !viewModel.UploadCommand.CanExecute(null))
        {
            return;
        }

        viewModel.UploadCommand.Execute(null);
        e.Handled = true;
    }

    private void HandleCreateFolderFloatButtonClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not RemoteBrowserViewModel viewModel ||
            !viewModel.CreateFolderCommand.CanExecute(null))
        {
            return;
        }

        viewModel.CreateFolderCommand.Execute(null);
        e.Handled = true;
    }

    private void HandleCreateBucketFloatButtonClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not RemoteBrowserViewModel viewModel ||
            !viewModel.CreateBucketCommand.CanExecute(null))
        {
            return;
        }

        viewModel.CreateBucketCommand.Execute(null);
        e.Handled = true;
    }
}

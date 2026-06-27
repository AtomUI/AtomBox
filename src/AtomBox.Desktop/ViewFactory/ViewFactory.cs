using AtomBox.Desktop.ViewModels.Pages;
using AtomBox.Desktop.Views.Accounts;
using AtomBox.Desktop.Views.Home;
using AtomBox.Desktop.Views.RemoteBrowser;
using AtomBox.Desktop.Views.Settings;
using AtomBox.Desktop.Views.Transfers;
using Avalonia.Controls;

namespace AtomBox.Desktop.ViewFactory;

public sealed class ViewFactory : IViewFactory
{
    private readonly IReadOnlyDictionary<Type, Func<object, Control>> _registrations;

    public ViewFactory()
    {
        _registrations = new Dictionary<Type, Func<object, Control>>
        {
            [typeof(HomeViewModel)] = Create<HomeView>,
            [typeof(RemoteBrowserViewModel)] = Create<RemoteBrowserView>,
            [typeof(TransferQueueViewModel)] = Create<TransferQueueView>,
            [typeof(TransferHistoryViewModel)] = Create<TransferHistoryView>,
            [typeof(SettingsViewModel)] = Create<SettingsView>,
            [typeof(AccountManagementViewModel)] = Create<AccountManagementView>
        };
    }

    public Control CreateView(object viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        var viewModelType = viewModel.GetType();
        if (!_registrations.TryGetValue(viewModelType, out var factory))
        {
            throw new InvalidOperationException($"No view registered for ViewModel type '{viewModelType.FullName}'.");
        }

        return factory(viewModel);
    }

    private static Control Create<TView>(object viewModel)
        where TView : Control, new()
    {
        return new TView
        {
            DataContext = viewModel
        };
    }
}

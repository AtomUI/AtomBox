using AtomBox.Desktop.ViewModels.Pages;
using AtomUI.City.Routing;

namespace AtomBox.Desktop.Navigation;

internal static class AtomBoxRouteCatalog
{
    public const string PrimaryOutlet = "primary";
    public const string Home = "AtomBox.Home";
    public const string RemoteBrowser = "AtomBox.RemoteBrowser";
    public const string TransferQueue = "AtomBox.TransferQueue";
    public const string TransferHistory = "AtomBox.TransferHistory";
    public const string Settings = "AtomBox.Settings";
    public const string AccountManagement = "AtomBox.AccountManagement";

    public static IReadOnlyList<RouteDescriptor> Descriptors { get; } =
    [
        Create(Home, "home", typeof(HomeViewModel)),
        Create(RemoteBrowser, "remote", typeof(RemoteBrowserViewModel)),
        Create(TransferQueue, "transfers/queue", typeof(TransferQueueViewModel)),
        Create(TransferHistory, "transfers/history", typeof(TransferHistoryViewModel)),
        Create(Settings, "settings", typeof(SettingsViewModel)),
        Create(AccountManagement, "accounts", typeof(AccountManagementViewModel)),
    ];

    public static RouteReference<IReadOnlyDictionary<string, string>> GetReference(
        NavigationTarget target)
    {
        return new RouteReference<IReadOnlyDictionary<string, string>>(
            GetRouteId(target),
            static parameters => parameters);
    }

    public static string GetRouteId(NavigationTarget target)
    {
        return target switch
        {
            NavigationTarget.Home => Home,
            NavigationTarget.RemoteBrowser => RemoteBrowser,
            NavigationTarget.TransferQueue => TransferQueue,
            NavigationTarget.TransferHistory => TransferHistory,
            NavigationTarget.Settings => Settings,
            NavigationTarget.AccountManagement => AccountManagement,
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, "Unknown navigation target."),
        };
    }

    private static RouteDescriptor Create(string routeId, string template, Type viewModelType)
    {
        return new RouteDescriptor(
            routeId,
            RouteDefinitionKind.Route,
            template,
            new ViewModelTargetDescriptor(viewModelType),
            outletName: PrimaryOutlet);
    }
}

using AtomBox.Core.ValueObjects;

namespace AtomBox.Desktop.Navigation;

public static class NavigationMenuKeys
{
    public const string Home = "home";
    public const string RemoteStorage = "remote.storage";
    public const string RemoteObjectStorage = "remote.object-storage";
    public const string RemoteFileTransfer = "remote.file-transfer";
    public const string RemoteWebDav = "remote.webdav";
    public const string AddRemote = "remote.add";
    public const string RemoteAccountPrefix = "remote.account:";
    public const string TransferQueue = "transfer.queue";
    public const string TransferHistory = "transfer.history";
    public const string Settings = "settings.application";
    public const string AccountManagement = "settings.accounts";

    public static string RemoteAccount(StorageAccountId accountId)
    {
        return $"{RemoteAccountPrefix}{accountId}";
    }
}

using AtomBox.Core.ValueObjects;

namespace AtomBox.Desktop.Navigation;

public sealed record RemoteBrowserNavigationParameter(
    NavigationResourceGroup ResourceGroup,
    StorageAccountId? StorageAccountId = null,
    RemotePath? RemotePath = null) : NavigationParameter;

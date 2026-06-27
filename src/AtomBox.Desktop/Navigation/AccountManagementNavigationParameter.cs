using AtomBox.Core.ValueObjects;

namespace AtomBox.Desktop.Navigation;

public sealed record AccountManagementNavigationParameter(StorageAccountId? SelectedAccountId = null) : NavigationParameter;

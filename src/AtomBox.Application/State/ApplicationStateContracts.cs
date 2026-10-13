using AtomBox.Application.Accounts;
using AtomBox.Core.Errors;
using AtomBox.Core.Settings;
using AtomUI.City.State;

namespace AtomBox.Application.State;

public enum ApplicationProjectionStatus
{
    Uninitialized,
    Ready,
    Faulted,
}

public sealed record ApplicationProjectionFailure(
    StorageErrorCode Code,
    StorageErrorCategory Category,
    bool IsRetryable)
{
    internal static ApplicationProjectionFailure From(StorageError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new ApplicationProjectionFailure(error.Code, error.Category, error.IsRetryable);
    }
}

public sealed record AccountCatalogState
{
    private AccountCatalogState(
        ApplicationProjectionStatus status,
        IReadOnlyList<StorageAccountSummary> accounts,
        ApplicationProjectionFailure? failure)
    {
        Status = status;
        Accounts = Array.AsReadOnly(accounts.ToArray());
        Failure = failure;
    }

    public ApplicationProjectionStatus Status { get; }

    public IReadOnlyList<StorageAccountSummary> Accounts { get; }

    public ApplicationProjectionFailure? Failure { get; }

    public static AccountCatalogState Uninitialized { get; } = new(
        ApplicationProjectionStatus.Uninitialized,
        [],
        failure: null);

    internal static AccountCatalogState Ready(IReadOnlyList<StorageAccountSummary> accounts)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        return new AccountCatalogState(ApplicationProjectionStatus.Ready, accounts, failure: null);
    }

    internal static AccountCatalogState Faulted(StorageError error)
    {
        return new AccountCatalogState(
            ApplicationProjectionStatus.Faulted,
            [],
            ApplicationProjectionFailure.From(error));
    }
}

public sealed record ApplicationSettingsState
{
    private ApplicationSettingsState(
        ApplicationProjectionStatus status,
        ApplicationSettings? settings,
        ApplicationProjectionFailure? failure)
    {
        Status = status;
        Settings = settings;
        Failure = failure;
    }

    public ApplicationProjectionStatus Status { get; }

    public ApplicationSettings? Settings { get; }

    public ApplicationProjectionFailure? Failure { get; }

    public static ApplicationSettingsState Uninitialized { get; } = new(
        ApplicationProjectionStatus.Uninitialized,
        settings: null,
        failure: null);

    internal static ApplicationSettingsState Ready(ApplicationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new ApplicationSettingsState(ApplicationProjectionStatus.Ready, settings, failure: null);
    }

    internal static ApplicationSettingsState Faulted(StorageError error)
    {
        return new ApplicationSettingsState(
            ApplicationProjectionStatus.Faulted,
            settings: null,
            ApplicationProjectionFailure.From(error));
    }
}

public interface IAtomBoxApplicationState
{
    IReadOnlyState<AccountCatalogState> Accounts { get; }

    IReadOnlyState<ApplicationSettingsState> Settings { get; }
}

using AtomBox.Application.Accounts;
using AtomBox.Core.Accounts;
using AtomBox.Core.Errors;
using AtomBox.Core.Providers;
using AtomBox.Core.Results;
using AtomBox.Core.ValueObjects;

namespace AtomBox.Desktop.Dialogs;

public sealed record AccountDialogRequest(
    string Title,
    IReadOnlyList<ProviderDescriptor> Providers,
    StorageProviderCategory? PreferredCategory = null,
    StorageProviderId? PreferredProviderId = null,
    Func<AccountDialogResult, CancellationToken, Task<OperationResult<TestConnectionResult>>>? TestConnectionAsync = null,
    StorageAccountSummary? ExistingAccount = null);

public sealed record AccountDialogResult(
    StorageProviderCategory ProviderCategory,
    StorageProviderId ProviderId,
    string DisplayName,
    string? Endpoint,
    string? Region,
    IReadOnlyDictionary<string, string> ProviderConfig,
    IReadOnlyDictionary<string, string> CredentialValues);

public sealed record ConfirmDialogRequest(
    string Title,
    string Message,
    string ConfirmText = "Confirm",
    string CancelText = "Cancel");

public sealed record TextInputDialogRequest(
    string Title,
    string Message,
    string InitialValue = "",
    string Placeholder = "",
    string ConfirmText = "Confirm",
    string CancelText = "Cancel");

public sealed record ErrorDialogRequest(
    string Title,
    string Summary,
    StorageError? Error = null,
    IReadOnlyDictionary<string, string>? Details = null)
{
    public static ErrorDialogRequest FromError(
        string title,
        string fallbackSummary,
        StorageError? error,
        IReadOnlyDictionary<string, string>? details = null)
    {
        return new ErrorDialogRequest(
            title,
            fallbackSummary,
            error,
            details);
    }
}

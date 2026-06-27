using AtomBox.Application.Accounts;
using AtomBox.Core.Accounts;
using AtomBox.Core.Results;
using AtomBox.Core.ValueObjects;

namespace AtomBox.Desktop.Dialogs;

public interface IAccountDialogWorkflow
{
    event EventHandler? AccountsChanged;

    Task<OperationResult<StorageAccountSummary?>> AddAccountAsync(
        StorageProviderCategory? preferredCategory = null,
        StorageProviderId? preferredProviderId = null,
        CancellationToken cancellationToken = default);

    Task<OperationResult<StorageAccountSummary?>> EditAccountAsync(
        StorageAccountSummary account,
        CancellationToken cancellationToken = default);

    Task<OperationResult> DeleteAccountAsync(
        StorageAccountSummary account,
        CancellationToken cancellationToken = default);
}

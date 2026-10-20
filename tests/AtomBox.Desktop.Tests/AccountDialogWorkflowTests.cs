using System.ComponentModel;
using System.Globalization;
using AtomBox.Application.Accounts;
using AtomBox.Core.Accounts;
using AtomBox.Core.Providers;
using AtomBox.Core.Results;
using AtomBox.Core.ValueObjects;
using AtomBox.Desktop.Dialogs;
using AtomBox.Desktop.Localization;

namespace AtomBox.Desktop.Tests;

public sealed class AccountDialogWorkflowTests
{
    [Fact]
    public async Task CancelledDialogIsRepresentedOutsideOperationResult()
    {
        var workflow = new AccountDialogWorkflow(
            new CancelledDialogService(),
            new EmptyProviderRegistry(),
            accounts: null!,
            new TestLocalizedText());
        var account = new StorageAccountSummary(
            StorageAccountId.New(),
            StorageProviderCategory.ObjectStorage,
            StorageProviderId.From("test-provider"),
            "Test account",
            null,
            null,
            CredentialRef.From("test-credential"),
            new Dictionary<string, string>());

        Assert.Null(await workflow.AddAccountAsync());
        Assert.Null(await workflow.EditAccountAsync(account));
    }

    private sealed class CancelledDialogService : IDialogService
    {
        public Task<AccountDialogResult?> ShowAccountDialogAsync(AccountDialogRequest request) =>
            Task.FromResult<AccountDialogResult?>(null);

        public Task<bool> ConfirmAsync(ConfirmDialogRequest request) => throw new NotSupportedException();

        public Task<string?> ShowTextInputAsync(TextInputDialogRequest request) => throw new NotSupportedException();

        public Task ShowPreviewAsync(PreviewDialogRequest request) => throw new NotSupportedException();

        public Task ShowErrorDetailsAsync(ErrorDialogRequest request) => throw new NotSupportedException();
    }

    private sealed class EmptyProviderRegistry : IStorageProviderRegistry
    {
        public IReadOnlyList<ProviderDescriptor> GetAll() => [];

        public OperationResult<ProviderDescriptor> GetById(StorageProviderId providerId) =>
            throw new NotSupportedException();
    }

    private sealed class TestLocalizedText : IAtomBoxLocalizedText
    {
        public event PropertyChangedEventHandler? PropertyChanged
        {
            add { }
            remove { }
        }

        public event EventHandler? LanguageChanged
        {
            add { }
            remove { }
        }

        public CultureInfo CurrentCulture => CultureInfo.InvariantCulture;

        public long Revision => 0;

        public string GetString(string catalog, string key) => $"{catalog}.{key}";

        public string Format(string catalog, string key, params object[] arguments) =>
            $"{catalog}.{key}";
    }
}

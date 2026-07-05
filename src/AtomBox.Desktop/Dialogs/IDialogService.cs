namespace AtomBox.Desktop.Dialogs;

public interface IDialogService
{
    Task<AccountDialogResult?> ShowAccountDialogAsync(AccountDialogRequest request);

    Task<bool> ConfirmAsync(ConfirmDialogRequest request);

    Task<string?> ShowTextInputAsync(TextInputDialogRequest request);

    Task ShowPreviewAsync(PreviewDialogRequest request);

    Task ShowErrorDetailsAsync(ErrorDialogRequest request);
}

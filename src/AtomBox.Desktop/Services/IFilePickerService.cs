namespace AtomBox.Desktop.Services;

public interface IFilePickerService
{
    Task<IReadOnlyList<string>> PickFilesForUploadAsync();

    Task<string?> PickFolderAsync(string title, string? suggestedPath = null);
}

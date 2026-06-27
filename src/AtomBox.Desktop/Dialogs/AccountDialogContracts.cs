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
    string ConfirmText = "确认",
    string CancelText = "取消");

public sealed record TextInputDialogRequest(
    string Title,
    string Message,
    string InitialValue = "",
    string Placeholder = "",
    string ConfirmText = "确认",
    string CancelText = "取消");

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
        var mergedDetails = AddSuggestion(error, details);
        return new ErrorDialogRequest(
            title,
            error?.Message ?? fallbackSummary,
            error,
            mergedDetails);
    }

    private static IReadOnlyDictionary<string, string>? AddSuggestion(
        StorageError? error,
        IReadOnlyDictionary<string, string>? details)
    {
        var suggestion = GetSuggestion(error);
        if (string.IsNullOrWhiteSpace(suggestion))
        {
            return details;
        }

        var merged = details is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(details, StringComparer.Ordinal);
        merged["下一步建议"] = suggestion;
        return merged;
    }

    private static string? GetSuggestion(StorageError? error)
    {
        return error?.Category switch
        {
            StorageErrorCategory.Authentication => "检查 AccessKey、AccessKeySecret、Token 或临时凭据是否正确且未过期。",
            StorageErrorCategory.Authorization => "检查账号是否具备 bucket、路径或对象的访问权限；对象存储账号请检查 RAM policy。",
            StorageErrorCategory.Network => "检查 endpoint、网络连接、代理、防火墙或 DNS 解析是否正常。",
            StorageErrorCategory.NotFound => "检查 bucket、远程路径或对象是否存在；如果刚删除或移动过，请刷新当前目录。",
            StorageErrorCategory.Conflict => "检查是否存在同名文件、重复任务或当前任务状态是否允许该操作。",
            StorageErrorCategory.Provider => "查看 Provider 错误码和服务状态；如果标记为可重试，可以稍后重试。",
            StorageErrorCategory.Infrastructure => "检查本地配置目录、状态目录、凭据目录和磁盘权限。",
            StorageErrorCategory.Validation => "检查表单必填项、endpoint、bucket、路径或端口格式是否正确。",
            _ => null
        };
    }
}

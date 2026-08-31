using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using AtomBox.Core.Settings;
using AtomBox.Core.Transfers;
using AtomBox.Core.ValueObjects;
using AtomBox.Desktop.Localization;
using AtomBox.Desktop.ViewModels.Pages;
using AtomBox.Providers.Common;

namespace AtomBox.Desktop.Tests;

public sealed class TransferCancellationPresentationTests
{
    private static readonly ICommand Command = new TestCommand();
    private static readonly ILocalizationService Localization = new TestLocalizationService();

    [Theory]
    [MemberData(nameof(GetPreciseCancellationCases))]
    public void RunningTask_WithPreciseCancellationProvider_UsesRegularCancellation(
        StorageProviderId providerId,
        TransferDirection direction)
    {
        var row = CreateRow(providerId, direction, TransferStatus.Running);

        Assert.False(row.UsesBestEffortCancellation);
        Assert.Equal("Common.Cancel", row.Action);
    }

    [Theory]
    [MemberData(nameof(GetBestEffortCancellationCases))]
    public void RunningTask_WithBestEffortCancellationProvider_UsesTryCancellation(
        StorageProviderId providerId,
        TransferDirection direction)
    {
        var row = CreateRow(providerId, direction, TransferStatus.Running);

        Assert.True(row.UsesBestEffortCancellation);
        Assert.Equal("Transfer.TryCancel", row.Action);
    }

    [Fact]
    public void PendingTask_WithBestEffortCancellationProvider_RemainsDirectlyCancelable()
    {
        var row = CreateRow(
            StorageProviderRegistry.AliyunOssProviderId,
            TransferDirection.Upload,
            TransferStatus.Pending);

        Assert.True(row.UsesBestEffortCancellation);
        Assert.Equal("Common.Cancel", row.Action);
    }

    [Theory]
    [InlineData(true, "Transfer.AttemptingCancellation")]
    [InlineData(false, "Transfer.Canceling")]
    public void CancelingTask_UsesCapabilitySpecificStatus(bool bestEffort, string expected)
    {
        var providerId = bestEffort
            ? StorageProviderRegistry.BaiduBosProviderId
            : StorageProviderRegistry.S3CompatibleProviderId;

        var row = CreateRow(providerId, TransferDirection.Upload, TransferStatus.Canceling);

        Assert.Equal(expected, row.Progress);
    }

    public static IEnumerable<object[]> GetPreciseCancellationCases()
    {
        yield return [StorageProviderRegistry.S3CompatibleProviderId, TransferDirection.Upload];
        yield return [StorageProviderRegistry.S3CompatibleProviderId, TransferDirection.Download];
        yield return [StorageProviderRegistry.TencentCosProviderId, TransferDirection.Upload];
        yield return [StorageProviderRegistry.TencentCosProviderId, TransferDirection.Download];
        yield return [StorageProviderRegistry.QiniuKodoProviderId, TransferDirection.Download];
        yield return [StorageProviderRegistry.FtpProviderId, TransferDirection.Upload];
        yield return [StorageProviderRegistry.FtpProviderId, TransferDirection.Download];
        yield return [StorageProviderRegistry.SftpProviderId, TransferDirection.Upload];
        yield return [StorageProviderRegistry.SftpProviderId, TransferDirection.Download];
        yield return [StorageProviderRegistry.WebDavProviderId, TransferDirection.Upload];
        yield return [StorageProviderRegistry.WebDavProviderId, TransferDirection.Download];
    }

    public static IEnumerable<object[]> GetBestEffortCancellationCases()
    {
        yield return [StorageProviderRegistry.AliyunOssProviderId, TransferDirection.Upload];
        yield return [StorageProviderRegistry.AliyunOssProviderId, TransferDirection.Download];
        yield return [StorageProviderRegistry.BaiduBosProviderId, TransferDirection.Upload];
        yield return [StorageProviderRegistry.HuaweiObsProviderId, TransferDirection.Upload];
        yield return [StorageProviderRegistry.VolcengineTosProviderId, TransferDirection.Upload];
        yield return [StorageProviderRegistry.UpyunProviderId, TransferDirection.Upload];
        yield return [StorageProviderRegistry.QiniuKodoProviderId, TransferDirection.Upload];
        yield return [StorageProviderRegistry.JdCloudOssProviderId, TransferDirection.Upload];
        yield return [StorageProviderRegistry.QingStorProviderId, TransferDirection.Upload];
        yield return [new StorageProviderId("unknown-provider"), TransferDirection.Upload];
    }

    private static TransferTaskRowViewModel CreateRow(
        StorageProviderId providerId,
        TransferDirection direction,
        TransferStatus status)
    {
        var now = DateTimeOffset.UtcNow;
        var task = new TransferTask(
            TransferTaskId.New(),
            StorageAccountId.New(),
            direction,
            new LocalPath("C:/tests/demo.bin"),
            new RemotePath("bucket/demo.bin"),
            status,
            new TransferOptions(TransferOverwritePolicy.Overwrite),
            now,
            now);

        return TransferTaskRowViewModel.From(
            new TransferStateSnapshot(task, progress: null),
            providerId,
            Command,
            Command,
            Localization);
    }

    private sealed class TestCommand : ICommand
    {
        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
        }

        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }
    }

    private sealed class TestLocalizationService : ILocalizationService
    {
        public CultureInfo CurrentCulture => CultureInfo.InvariantCulture;

        public string CurrentPreference => "en-US";

        public long Revision => 0;

        public IReadOnlyList<SupportedLanguage> SupportedLanguages => [];

        public string GetString(string catalog, string key) => $"{catalog}.{key}";

        public string Format(string catalog, string key, params object[] arguments) => GetString(catalog, key);

        public Task SetLanguageAsync(string languageTag, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public event PropertyChangedEventHandler? PropertyChanged
        {
            add { }
            remove { }
        }

        public event EventHandler<LanguageChangedEventArgs>? LanguageChanged
        {
            add { }
            remove { }
        }
    }
}

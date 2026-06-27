using AtomBox.Application.Settings;
using AtomBox.Core.Errors;
using AtomBox.Core.Results;
using AtomBox.Core.Settings;

namespace AtomBox.Application.Tests;

public sealed class SettingsAppServiceContractTests
{
    private static readonly CancellationToken CT = CancellationToken.None;

    [Fact]
    public async Task GetAsync_ReturnsSettingsFromRepository()
    {
        var expected = new ApplicationSettings(5, TransferOverwritePolicy.Overwrite, false);
        var service = new SettingsAppService(new FixedSettingsRepository(expected));

        var result = await service.GetAsync(new GetApplicationSettingsRequest());

        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.GetValueOrThrow().Settings.DefaultConcurrency);
        Assert.Equal(TransferOverwritePolicy.Overwrite, result.GetValueOrThrow().Settings.DefaultOverwritePolicy);
        Assert.False(result.GetValueOrThrow().Settings.KeepCompletedTransfers);
    }

    [Fact]
    public async Task GetAsync_RepositoryFailure_PropagatesError()
    {
        var service = new SettingsAppService(new FailSettingsRepository());

        var result = await service.GetAsync(new GetApplicationSettingsRequest());

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Infrastructure, result.Error?.Category);
    }

    [Fact]
    public async Task UpdateAsync_SavesAndReturnsSettings()
    {
        var repo = new CaptureSettingsRepository();
        var service = new SettingsAppService(repo);
        var settings = new ApplicationSettings(7, TransferOverwritePolicy.Rename, true);

        var result = await service.UpdateAsync(new UpdateApplicationSettingsRequest(settings));

        Assert.True(result.IsSuccess);
        Assert.Equal(7, result.GetValueOrThrow().Settings.DefaultConcurrency);
        Assert.Equal(settings, repo.SavedSettings);
    }

    [Fact]
    public async Task UpdateAsync_RepositoryFailure_PropagatesError()
    {
        var settings = new ApplicationSettings(3, TransferOverwritePolicy.Ask, true);
        var service = new SettingsAppService(new FailSettingsRepository());

        var result = await service.UpdateAsync(new UpdateApplicationSettingsRequest(settings));

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task ResetAsync_ReturnsDefaultSettings()
    {
        var repo = new CaptureSettingsRepository();
        var service = new SettingsAppService(repo);

        var result = await service.ResetAsync(new ResetApplicationSettingsRequest());

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.GetValueOrThrow().Settings.DefaultConcurrency);
        Assert.Equal(TransferOverwritePolicy.Ask, result.GetValueOrThrow().Settings.DefaultOverwritePolicy);
        Assert.True(result.GetValueOrThrow().Settings.KeepCompletedTransfers);
    }

    [Fact]
    public async Task ResetAsync_RepositoryFailure_PropagatesError()
    {
        var service = new SettingsAppService(new FailSettingsRepository());

        var result = await service.ResetAsync(new ResetApplicationSettingsRequest());

        Assert.True(result.IsFailure);
    }

    private sealed class FixedSettingsRepository : IApplicationSettingsRepository
    {
        private readonly ApplicationSettings _settings;
        public FixedSettingsRepository(ApplicationSettings settings) { _settings = settings; }

        public Task<OperationResult<ApplicationSettings>> GetAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult<ApplicationSettings>.Success(_settings));

        public Task<OperationResult> SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Success());
    }

    private sealed class CaptureSettingsRepository : IApplicationSettingsRepository
    {
        public ApplicationSettings? SavedSettings { get; private set; }

        public Task<OperationResult<ApplicationSettings>> GetAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult<ApplicationSettings>.Success(
                SavedSettings ?? new ApplicationSettings(3, TransferOverwritePolicy.Ask, true)));

        public Task<OperationResult> SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken = default)
        {
            SavedSettings = settings;
            return Task.FromResult(OperationResult.Success());
        }
    }

    private sealed class FailSettingsRepository : IApplicationSettingsRepository
    {
        public Task<OperationResult<ApplicationSettings>> GetAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult<ApplicationSettings>.Failure(
                new StorageError(StorageErrorCode.InfrastructureUnavailable, "fail", StorageErrorCategory.Infrastructure)));

        public Task<OperationResult> SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Failure(
                new StorageError(StorageErrorCode.InfrastructureUnavailable, "fail", StorageErrorCategory.Infrastructure)));
    }
}

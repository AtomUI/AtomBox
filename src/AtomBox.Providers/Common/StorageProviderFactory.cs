using AtomBox.Core.Accounts;
using AtomBox.Core.Errors;
using AtomBox.Core.Providers;
using AtomBox.Core.Results;
using AtomBox.Core.ValueObjects;

namespace AtomBox.Providers.Common;

public sealed class StorageProviderFactory : IStorageProviderFactory
{
    private readonly IStorageProviderRegistry _registry;
    private readonly IReadOnlyDictionary<StorageProviderId, IStorageProviderCreator> _creators;

    public StorageProviderFactory(
        IStorageProviderRegistry registry,
        IEnumerable<IStorageProviderCreator> creators)
    {
        _registry = registry;
        ArgumentNullException.ThrowIfNull(creators);
        _creators = BuildCreatorMap(creators);
    }

    public async Task<OperationResult<IStorageProvider>> CreateAsync(
        StorageAccount account,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);

        if (account.ProviderId.IsEmpty)
        {
            return OperationResult<IStorageProvider>.Failure(
                StorageError.Validation("Storage provider id is required."));
        }

        var descriptorResult = _registry.GetById(account.ProviderId);
        if (descriptorResult.IsFailure)
        {
            return OperationResult<IStorageProvider>.Failure(descriptorResult.Error!);
        }

        var descriptor = descriptorResult.GetValueOrThrow();
        if (descriptor.Category != account.ProviderCategory)
        {
            return OperationResult<IStorageProvider>.Failure(
                StorageError.Validation("Storage account provider category does not match provider descriptor."));
        }

        var configValidation = ValidateKnownAccountConfig(account, descriptor);
        if (configValidation is not null)
        {
            return OperationResult<IStorageProvider>.Failure(configValidation);
        }

        if (!_creators.TryGetValue(account.ProviderId, out var creator))
        {
            return OperationResult<IStorageProvider>.Failure(
                StorageError.NotFound("Storage provider implementation was not registered."));
        }

        try
        {
            return await creator.CreateAsync(account, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            return OperationResult<IStorageProvider>.Failure(ProviderErrorMapper.FromException(exception));
        }
    }

    private static IReadOnlyDictionary<StorageProviderId, IStorageProviderCreator> BuildCreatorMap(
        IEnumerable<IStorageProviderCreator> creators)
    {
        var creatorArray = creators.ToArray();
        var duplicate = creatorArray
            .GroupBy(item => item.Descriptor.Id)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Duplicate storage provider creator: {duplicate.Key}");
        }

        return creatorArray.ToDictionary(item => item.Descriptor.Id);
    }

    private static StorageError? ValidateKnownAccountConfig(
        StorageAccount account,
        ProviderDescriptor descriptor)
    {
        foreach (var field in descriptor.ConfigFields.Where(field => field.IsRequired))
        {
            if (string.IsNullOrWhiteSpace(account.GetProviderConfigValue(field.Key)))
            {
                return StorageError.Validation($"Required provider config field is missing: {field.Key}.");
            }
        }

        return null;
    }
}

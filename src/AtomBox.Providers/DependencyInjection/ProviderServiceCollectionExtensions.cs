using AtomBox.Core.Providers;
using AtomBox.Providers.Common;
using AtomBox.Providers.FileTransfer.Ftp;
using AtomBox.Providers.FileTransfer.Sftp;
using AtomBox.Providers.FileTransfer.WebDav;
using AtomBox.Providers.NetDisk.AliyunDrive;
using AtomBox.Providers.NetDisk.BaiduNetDisk;
using AtomBox.Providers.ObjectStorage.AliyunOss;
using AtomBox.Providers.ObjectStorage.BaiduBos;
using AtomBox.Providers.ObjectStorage.HuaweiObs;
using AtomBox.Providers.ObjectStorage.JdCloudOss;
using AtomBox.Providers.ObjectStorage.QiniuKodo;
using AtomBox.Providers.ObjectStorage.QingStor;
using AtomBox.Providers.ObjectStorage.TencentCos;
using AtomBox.Providers.ObjectStorage.Upyun;
using AtomBox.Providers.ObjectStorage.VolcengineTos;
using Microsoft.Extensions.DependencyInjection;

namespace AtomBox.Providers.DependencyInjection;

public static class ProviderServiceCollectionExtensions
{
    public static IServiceCollection AddAtomBoxProviders(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var descriptors = StorageProviderRegistry.CreateDefaultDescriptors();

        services.AddSingleton<IStorageProviderRegistry>(new StorageProviderRegistry(descriptors));
        foreach (var descriptor in descriptors)
        {
            if (descriptor.Id == StorageProviderRegistry.AliyunOssProviderId)
            {
                services.AddSingleton<IStorageProviderCreator>(provider =>
                    new AliyunOssProviderCreator(
                        descriptor,
                        provider.GetRequiredService<ProviderCredentialResolver>()));
                continue;
            }

            if (descriptor.Id == StorageProviderRegistry.TencentCosProviderId)
            {
                services.AddSingleton<IStorageProviderCreator>(provider =>
                    new TencentCosProviderCreator(
                        descriptor,
                        provider.GetRequiredService<ProviderCredentialResolver>()));
                continue;
            }

            if (descriptor.Id == StorageProviderRegistry.QiniuKodoProviderId)
            {
                services.AddSingleton<IStorageProviderCreator>(provider =>
                    new QiniuKodoProviderCreator(
                        descriptor,
                        provider.GetRequiredService<ProviderCredentialResolver>()));
                continue;
            }

            if (descriptor.Id == StorageProviderRegistry.UpyunProviderId)
            {
                services.AddSingleton<IStorageProviderCreator>(provider =>
                    new UpyunProviderCreator(
                        descriptor,
                        provider.GetRequiredService<ProviderCredentialResolver>()));
                continue;
            }

            if (descriptor.Id == StorageProviderRegistry.HuaweiObsProviderId)
            {
                services.AddSingleton<IStorageProviderCreator>(provider =>
                    new HuaweiObsProviderCreator(
                        descriptor,
                        provider.GetRequiredService<ProviderCredentialResolver>()));
                continue;
            }

            if (descriptor.Id == StorageProviderRegistry.BaiduBosProviderId)
            {
                services.AddSingleton<IStorageProviderCreator>(provider =>
                    new BaiduBosProviderCreator(
                        descriptor,
                        provider.GetRequiredService<ProviderCredentialResolver>()));
                continue;
            }

            if (descriptor.Id == StorageProviderRegistry.JdCloudOssProviderId)
            {
                services.AddSingleton<IStorageProviderCreator>(provider =>
                    new JdCloudOssProviderCreator(
                        descriptor,
                        provider.GetRequiredService<ProviderCredentialResolver>()));
                continue;
            }

            if (descriptor.Id == StorageProviderRegistry.QingStorProviderId)
            {
                services.AddSingleton<IStorageProviderCreator>(provider =>
                    new QingStorProviderCreator(
                        descriptor,
                        provider.GetRequiredService<ProviderCredentialResolver>()));
                continue;
            }

            if (descriptor.Id == StorageProviderRegistry.VolcengineTosProviderId)
            {
                services.AddSingleton<IStorageProviderCreator>(provider =>
                    new VolcengineTosProviderCreator(
                        descriptor,
                        provider.GetRequiredService<ProviderCredentialResolver>()));
                continue;
            }

            if (descriptor.Id == StorageProviderRegistry.AliyunDriveProviderId)
            {
                services.AddSingleton<IStorageProviderCreator>(provider =>
                    new AliyunDriveProviderCreator(
                        descriptor,
                        provider.GetRequiredService<ProviderCredentialResolver>()));
                continue;
            }

            if (descriptor.Id == StorageProviderRegistry.BaiduNetDiskProviderId)
            {
                services.AddSingleton<IStorageProviderCreator>(provider =>
                    new BaiduNetDiskProviderCreator(
                        descriptor,
                        provider.GetRequiredService<ProviderCredentialResolver>()));
                continue;
            }

            if (descriptor.Id == StorageProviderRegistry.SftpProviderId)
            {
                services.AddSingleton<IStorageProviderCreator>(provider =>
                    new SftpStorageProviderCreator(
                        descriptor,
                        provider.GetRequiredService<ProviderCredentialResolver>()));
                continue;
            }

            if (descriptor.Id == StorageProviderRegistry.FtpProviderId)
            {
                services.AddSingleton<IStorageProviderCreator>(provider =>
                    new FtpStorageProviderCreator(
                        descriptor,
                        provider.GetRequiredService<ProviderCredentialResolver>()));
                continue;
            }

            if (descriptor.Id == StorageProviderRegistry.WebDavProviderId)
            {
                services.AddSingleton<IStorageProviderCreator>(provider =>
                    new WebDavStorageProviderCreator(
                        descriptor,
                        provider.GetRequiredService<ProviderCredentialResolver>()));
                continue;
            }

            services.AddSingleton<IStorageProviderCreator>(new FakeStorageProviderCreator(descriptor));
        }

        services.AddSingleton<ProviderCredentialResolver>();
        services.AddSingleton<IStorageProviderFactory, StorageProviderFactory>();

        return services;
    }
}

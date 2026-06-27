using System.Security.Cryptography;
using AtomBox.Infrastructure.Configuration;
using AtomBox.Infrastructure.Credentials;

namespace AtomBox.Infrastructure.Tests;

public sealed class CredentialProtectionServiceContractTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "AtomBox.Infra.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void ProtectAndUnprotect_Roundtrips()
    {
        var service = new CredentialProtectionService(new AtomBoxStoragePaths(_root));
        var original = new byte[] { 0x01, 0x02, 0x03, 0x04, 0xFF };

        var protectedPayload = service.Protect(original);
        var unprotected = service.Unprotect(protectedPayload);

        Assert.Equal(original, unprotected);
    }

    [Fact]
    public void Protect_EmptyPayload_Throws()
    {
        var service = new CredentialProtectionService(new AtomBoxStoragePaths(_root));
        Assert.Throws<ArgumentNullException>(() => service.Protect(null!));
    }

    [Fact]
    public void Unprotect_NullPayload_Throws()
    {
        var service = new CredentialProtectionService(new AtomBoxStoragePaths(_root));
        Assert.Throws<ArgumentException>(() => service.Unprotect(""));
    }

    [Fact]
    public void Unprotect_InvalidFormat_Throws()
    {
        var service = new CredentialProtectionService(new AtomBoxStoragePaths(_root));
        Assert.Throws<InvalidOperationException>(() => service.Unprotect("bad-format-data"));
    }

    [Fact]
    public void Unprotect_WrongPrefix_Throws()
    {
        var service = new CredentialProtectionService(new AtomBoxStoragePaths(_root));
        var ex = Assert.Throws<InvalidOperationException>(() => service.Unprotect("unknown-v1:" + Convert.ToBase64String(new byte[32])));
        Assert.Contains("not supported", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Protect_GeneratesUniqueOutputs()
    {
        var service = new CredentialProtectionService(new AtomBoxStoragePaths(_root));
        var payload = new byte[] { 0x01, 0x02, 0x03 };

        var result1 = service.Protect(payload);
        var result2 = service.Protect(payload);

        Assert.NotEqual(result1, result2);
    }

    [Fact]
    public void Protect_OutputStartsWithPrefix()
    {
        var service = new CredentialProtectionService(new AtomBoxStoragePaths(_root));
        var result = service.Protect(new byte[] { 0x00 });

        Assert.StartsWith("aesgcm-v1:", result, StringComparison.Ordinal);
    }

    [Fact]
    public void DifferentServiceInstances_WithSameKey_CanDecrypt()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var service1 = new CredentialProtectionService(paths);
        var original = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };

        var protectedPayload = service1.Protect(original);

        var service2 = new CredentialProtectionService(paths);
        var unprotected = service2.Unprotect(protectedPayload);

        Assert.Equal(original, unprotected);
    }

    [Fact]
    public void Protect_LargePayload_Succeeds()
    {
        var service = new CredentialProtectionService(new AtomBoxStoragePaths(_root));
        var large = new byte[1024 * 64];
        new Random(42).NextBytes(large);

        var protectedPayload = service.Protect(large);
        var unprotected = service.Unprotect(protectedPayload);

        Assert.Equal(large, unprotected);
    }

    [Fact]
    public void Unprotect_TamperedCiphertext_Throws()
    {
        var service = new CredentialProtectionService(new AtomBoxStoragePaths(_root));
        var protectedPayload = service.Protect(new byte[] { 0x01, 0x02, 0x03 });

        var chars = protectedPayload.ToCharArray();
        chars[^1] = chars[^1] == 'A' ? 'B' : 'A';
        var tampered = new string(chars);

        var ex = Record.Exception(() => service.Unprotect(tampered));
        Assert.NotNull(ex);
        Assert.True(ex is CryptographicException or FormatException, $"Unexpected exception type: {ex.GetType()}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}

using System.Security.Cryptography;
using AtomBox.Infrastructure.Configuration;

namespace AtomBox.Infrastructure.Credentials;

public sealed class CredentialProtectionService
{
    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const string PayloadPrefix = "aesgcm-v1:";

    private readonly AtomBoxStoragePaths _paths;
    private readonly object _keyGate = new();

    public CredentialProtectionService(AtomBoxStoragePaths paths)
    {
        _paths = paths;
    }

    public string Protect(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var key = GetOrCreateKey();
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[payload.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, payload, ciphertext, tag);

        var protectedPayload = new byte[NonceSize + TagSize + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, protectedPayload, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, protectedPayload, NonceSize, TagSize);
        Buffer.BlockCopy(ciphertext, 0, protectedPayload, NonceSize + TagSize, ciphertext.Length);

        return PayloadPrefix + Convert.ToBase64String(protectedPayload);
    }

    public byte[] Unprotect(string protectedPayload)
    {
        if (string.IsNullOrWhiteSpace(protectedPayload))
        {
            throw new ArgumentException("Protected payload cannot be empty.", nameof(protectedPayload));
        }

        if (!protectedPayload.StartsWith(PayloadPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Credential payload format is not supported.");
        }

        var raw = Convert.FromBase64String(protectedPayload[PayloadPrefix.Length..]);
        if (raw.Length < NonceSize + TagSize)
        {
            throw new InvalidOperationException("Credential payload format is invalid.");
        }

        var nonce = raw[..NonceSize];
        var tag = raw[NonceSize..(NonceSize + TagSize)];
        var ciphertext = raw[(NonceSize + TagSize)..];
        var plaintext = new byte[ciphertext.Length];

        var key = GetOrCreateKey();
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return plaintext;
    }

    private byte[] GetOrCreateKey()
    {
        lock (_keyGate)
        {
            if (File.Exists(_paths.CredentialKeyFile))
            {
                return File.ReadAllBytes(_paths.CredentialKeyFile);
            }

            var directory = Path.GetDirectoryName(_paths.CredentialKeyFile);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var key = RandomNumberGenerator.GetBytes(KeySize);
            File.WriteAllBytes(_paths.CredentialKeyFile, key);
            return key;
        }
    }
}

using AtomBox.Core.ValueObjects;

namespace AtomBox.Core.Credentials;

public sealed class CredentialMaterialLease : IAsyncDisposable
{
    private readonly CredentialLease _lease;

    public CredentialMaterialLease(CredentialLease lease, CredentialSecretMaterial material)
    {
        _lease = lease ?? throw new ArgumentNullException(nameof(lease));
        Material = material ?? throw new ArgumentNullException(nameof(material));
    }

    public CredentialRef CredentialRef => _lease.CredentialRef;

    public CredentialSecretMaterial Material { get; }

    public ValueTask DisposeAsync()
    {
        return _lease.DisposeAsync();
    }
}

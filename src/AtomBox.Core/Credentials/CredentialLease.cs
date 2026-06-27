using AtomBox.Core.ValueObjects;

namespace AtomBox.Core.Credentials;

public abstract class CredentialLease : IAsyncDisposable
{
    protected CredentialLease(CredentialRef credentialRef, string leaseId)
    {
        if (string.IsNullOrWhiteSpace(leaseId))
        {
            throw new ArgumentException("Credential lease id cannot be empty.", nameof(leaseId));
        }

        CredentialRef = credentialRef;
        LeaseId = leaseId;
    }

    public CredentialRef CredentialRef { get; }

    public string LeaseId { get; }

    public abstract ValueTask DisposeAsync();
}

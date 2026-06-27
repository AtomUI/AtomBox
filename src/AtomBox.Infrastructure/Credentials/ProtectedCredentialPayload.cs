namespace AtomBox.Infrastructure.Credentials;

public sealed record ProtectedCredentialPayload(
    string CredentialRef,
    string ProtectedPayload,
    bool PendingDelete,
    DateTimeOffset UpdatedAt);

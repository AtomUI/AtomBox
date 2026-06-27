using AtomBox.Core.ValueObjects;

namespace AtomBox.Core.RemoteItems;

public sealed record RemoteItem
{
    public RemoteItem(
        string name,
        RemotePath path,
        RemoteItemKind kind,
        long? size,
        DateTimeOffset? updatedAt,
        string? eTag = null,
        string? contentType = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Remote item name cannot be empty.", nameof(name));
        }

        if (size < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size), "Remote item size cannot be negative.");
        }

        Name = name.Trim();
        Path = path;
        Kind = kind;
        Size = size;
        UpdatedAt = updatedAt;
        ETag = eTag?.Trim();
        ContentType = contentType?.Trim();
    }

    public string Name { get; }

    public RemotePath Path { get; }

    public RemoteItemKind Kind { get; }

    public long? Size { get; }

    public DateTimeOffset? UpdatedAt { get; }

    public string? ETag { get; }

    public string? ContentType { get; }
}

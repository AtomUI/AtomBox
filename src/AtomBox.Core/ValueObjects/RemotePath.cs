using System.Text.Json.Serialization;

namespace AtomBox.Core.ValueObjects;

public readonly record struct RemotePath
{
    public static RemotePath Root { get; } = new(string.Empty, RemotePathKind.Root);

    [JsonConstructor]
    public RemotePath(string value, RemotePathKind kind = RemotePathKind.ObjectPath, char separator = '/')
    {
        if (separator == '\0')
        {
            throw new ArgumentException("Remote path separator cannot be null.", nameof(separator));
        }

        Separator = separator;
        Value = Normalize(value, separator);
        Kind = kind;
    }

    public string Value { get; }

    public RemotePathKind Kind { get; }

    public char Separator { get; }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    public bool IsRoot => IsEmpty || Value == Separator.ToString();

    public string Name
    {
        get
        {
            if (IsRoot)
            {
                return string.Empty;
            }

            var trimmed = (Value ?? string.Empty).TrimEnd(Separator);
            var index = trimmed.LastIndexOf(Separator);
            return index < 0 ? trimmed : trimmed[(index + 1)..];
        }
    }

    public static RemotePath From(string value, RemotePathKind kind = RemotePathKind.ObjectPath, char separator = '/')
    {
        return new RemotePath(value, kind, separator);
    }

    public RemotePath Combine(string childName, RemotePathKind kind = RemotePathKind.ObjectPath)
    {
        if (string.IsNullOrWhiteSpace(childName))
        {
            throw new ArgumentException("Child path cannot be empty.", nameof(childName));
        }

        var child = Normalize(childName, Separator).TrimStart(Separator);
        var current = IsRoot ? string.Empty : Value.TrimEnd(Separator);
        return new RemotePath(string.IsNullOrEmpty(current) ? child : $"{current}{Separator}{child}", kind, Separator);
    }

    public RemotePath? GetParent()
    {
        if (IsRoot)
        {
            return null;
        }

        var trimmed = (Value ?? string.Empty).TrimEnd(Separator);
        var index = trimmed.LastIndexOf(Separator);
        return index <= 0
            ? new RemotePath(string.Empty, RemotePathKind.Root, Separator)
            : new RemotePath(trimmed[..index], RemotePathKind.Folder, Separator);
    }

    public override string ToString()
    {
        return Value ?? string.Empty;
    }

    private static string Normalize(string? value, char separator)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().Replace('\\', separator);
        var duplicate = new string(separator, 2);
        while (normalized.Contains(duplicate, StringComparison.Ordinal))
        {
            normalized = normalized.Replace(duplicate, separator.ToString(), StringComparison.Ordinal);
        }

        return normalized.TrimStart(separator);
    }
}

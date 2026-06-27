using System.Text;
using AtomBox.Core.RemoteItems;

namespace AtomBox.Providers.Common;

internal static class ProviderPageCursor
{
    public static RemotePageCursor FromProviderToken(string providerId, string token)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            throw new ArgumentException("Provider id cannot be empty.", nameof(providerId));
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException("Provider page token cannot be empty.", nameof(token));
        }

        return new RemotePageCursor($"{providerId}:{Convert.ToBase64String(Encoding.UTF8.GetBytes(token.Trim()))}");
    }

    public static bool TryGetProviderToken(RemotePageCursor? cursor, string providerId, out string? token)
    {
        token = null;
        if (cursor is null || string.IsNullOrWhiteSpace(cursor.Value.Value))
        {
            return true;
        }

        var prefix = $"{providerId}:";
        if (!cursor.Value.Value.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            token = Encoding.UTF8.GetString(Convert.FromBase64String(cursor.Value.Value[prefix.Length..]));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

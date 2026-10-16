namespace NvProfileSwitcher;

internal static class UpdateCore
{
    internal static bool IsVersionNewer(string remote, string local)
    {
        var a = ParseVersionParts(remote);
        var b = ParseVersionParts(local);
        if (a is null || b is null) return false;

        var count = Math.Max(a.Length, b.Length);
        for (var i = 0; i < count; i++)
        {
            var av = i < a.Length ? a[i] : 0;
            var bv = i < b.Length ? b[i] : 0;
            if (av > bv) return true;
            if (av < bv) return false;
        }
        return false;
    }

    internal static string? ParseSha256(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var firstToken = value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (firstToken is null || firstToken.Length != 64 || !firstToken.All(Uri.IsHexDigit)) return null;
        return firstToken.ToUpperInvariant();
    }

    internal static int[]? ParseVersionParts(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return null;
        var value = version.Trim();
        if (value.Length > 0 && (value[0] == 'v' || value[0] == 'V')) value = value[1..];
        var suffix = value.IndexOfAny(['-', '+']);
        if (suffix >= 0) value = value[..suffix];
        if (value.Length == 0) return null;

        var pieces = value.Split('.');
        var result = new int[pieces.Length];
        for (var i = 0; i < pieces.Length; i++)
        {
            if (pieces[i].Length == 0 || !pieces[i].All(char.IsDigit) ||
                !int.TryParse(pieces[i], out result[i]))
                return null;
        }
        return result;
    }
}

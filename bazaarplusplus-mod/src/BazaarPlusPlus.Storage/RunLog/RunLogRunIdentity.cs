#nullable enable

namespace BazaarPlusPlus.Storage.RunLog;

public static class RunLogRunIdentity
{
    private const string CollisionSeparator = ":bpp:";

    public static string CreateCollisionId(string serverRunId)
    {
        if (string.IsNullOrWhiteSpace(serverRunId))
            throw new ArgumentException("Server run ID is required.", nameof(serverRunId));

        return $"{serverRunId}{CollisionSeparator}{Guid.NewGuid():N}";
    }

    public static bool MatchesServerRunId(string localRunId, string serverRunId)
    {
        if (string.IsNullOrWhiteSpace(localRunId) || string.IsNullOrWhiteSpace(serverRunId))
            return false;

        if (string.Equals(localRunId, serverRunId, StringComparison.Ordinal))
            return true;

        var collisionPrefix = serverRunId + CollisionSeparator;
        if (!localRunId.StartsWith(collisionPrefix, StringComparison.Ordinal))
            return false;

        return Guid.TryParseExact(localRunId[collisionPrefix.Length..], "N", out _);
    }
}

#nullable enable
namespace BazaarPlusPlus.Game.EventPreview;

public sealed class EncounterPreviewCacheIdentity : IEquatable<EncounterPreviewCacheIdentity>
{
    public EncounterPreviewCacheIdentity(
        string kind,
        string resource,
        string value,
        string gameBuild,
        string buildChannel
    )
    {
        Kind = kind ?? string.Empty;
        Resource = resource ?? string.Empty;
        Value = value ?? string.Empty;
        GameBuild = gameBuild ?? string.Empty;
        BuildChannel = buildChannel ?? string.Empty;
    }

    public string Kind { get; }

    public string Resource { get; }

    public string Value { get; }

    public string GameBuild { get; }

    public string BuildChannel { get; }

    public bool Equals(EncounterPreviewCacheIdentity? other) =>
        other != null
        && string.Equals(Kind, other.Kind, StringComparison.Ordinal)
        && string.Equals(Resource, other.Resource, StringComparison.Ordinal)
        && string.Equals(Value, other.Value, StringComparison.Ordinal)
        && string.Equals(GameBuild, other.GameBuild, StringComparison.Ordinal)
        && string.Equals(BuildChannel, other.BuildChannel, StringComparison.Ordinal);

    public override bool Equals(object? obj) =>
        obj is EncounterPreviewCacheIdentity other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            hash = hash * 31 + StringComparer.Ordinal.GetHashCode(Kind);
            hash = hash * 31 + StringComparer.Ordinal.GetHashCode(Resource);
            hash = hash * 31 + StringComparer.Ordinal.GetHashCode(Value);
            hash = hash * 31 + StringComparer.Ordinal.GetHashCode(GameBuild);
            hash = hash * 31 + StringComparer.Ordinal.GetHashCode(BuildChannel);
            return hash;
        }
    }
}

#nullable enable
namespace BazaarPlusPlus.ModApi.Bundle;

internal sealed class BundleManifestV6
{
    public string BundleId { get; set; } = string.Empty;
    public int BundleVersion { get; set; } = BundleLimitsV6.BundleVersion;
    public long CreatedAtMs { get; set; }
    public BundleRunManifestV6 Run { get; set; } = new();
    public BundleScreenshotManifestV6? Screenshot { get; set; }
}

internal sealed class BundleRunManifestV6
{
    public string RunId { get; set; } = string.Empty;
    public string PlayerAccountId { get; set; } = string.Empty;
    public int RunFormatVersion { get; set; } = BundleLimitsV6.RunFormatVersion;
    public BundleProjectionV6 Projection { get; set; } = new();
    public BundleSegmentManifestV6 Payload { get; set; } = new();
}

internal sealed class BundleProjectionV6
{
    public List<BundleBattleProjectionV6> Battles { get; set; } = new();
}

internal sealed class BundleBattleProjectionV6
{
    public string BattleId { get; set; } = string.Empty;
    public long RecordedAtMs { get; set; }
    public long Day { get; set; }
    public long Hour { get; set; }
    public string? EncounterId { get; set; }
    public string CombatKind { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;
    public string? WinnerCombatantId { get; set; }
    public string? LoserCombatantId { get; set; }
    public bool IsFinalBattle { get; set; }
    public BundleCombatantProjectionV6 Player { get; set; } = new();
    public BundleCombatantProjectionV6 Opponent { get; set; } = new();
}

internal sealed class BundleCombatantProjectionV6
{
    public string AccountId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? HeroId { get; set; }
    public string? HeroName { get; set; }
    public string? Rank { get; set; }
    public long? Rating { get; set; }
    public long? Level { get; set; }
    public long? Prestige { get; set; }
    public long? Victories { get; set; }
}

internal class BundleSegmentManifestV6
{
    public long Offset { get; set; }
    public long Length { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
}

internal sealed class BundleScreenshotManifestV6 : BundleSegmentManifestV6
{
    public long Width { get; set; }
    public long Height { get; set; }
    public long Quality { get; set; }
    public long CapturedAtMs { get; set; }
}

internal sealed class BundleBuildInputV6
{
    public string BundleId { get; set; } = string.Empty;
    public long CreatedAtMs { get; set; }
    public string RunId { get; set; } = string.Empty;
    public string PlayerAccountId { get; set; } = string.Empty;
    public IReadOnlyList<BundleBattleProjectionV6> Battles { get; set; } =
        Array.Empty<BundleBattleProjectionV6>();
    public ReadOnlyMemory<byte> RunPayload { get; set; }
    public BundleScreenshotBuildInputV6? Screenshot { get; set; }
}

internal sealed class BundleScreenshotBuildInputV6
{
    public ReadOnlyMemory<byte> Bytes { get; set; }
    public string ContentType { get; set; } = BundleLimitsV6.JpegContentType;
    public int Width { get; set; }
    public int Height { get; set; }
    public int Quality { get; set; }
    public long CapturedAtMs { get; set; }
}

internal sealed class BundleBuildResultV6
{
    public BundleBuildResultV6(
        byte[] bytes,
        byte[] manifestBytes,
        BundleManifestV6 manifest,
        string sha256Hex,
        string contentDigest
    )
    {
        Bytes = bytes;
        ManifestBytes = manifestBytes;
        Manifest = manifest;
        Sha256Hex = sha256Hex;
        ContentDigest = contentDigest;
    }

    public byte[] Bytes { get; }
    public byte[] ManifestBytes { get; }
    public BundleManifestV6 Manifest { get; }
    public string Sha256Hex { get; }
    public string ContentDigest { get; }
}

internal sealed class OpenedBundleV6
{
    public OpenedBundleV6(
        BundleManifestV6 manifest,
        byte[] manifestBytes,
        byte[] runPayload,
        byte[]? screenshot,
        string sha256Hex,
        string contentDigest
    )
    {
        Manifest = manifest;
        ManifestBytes = manifestBytes;
        RunPayload = runPayload;
        Screenshot = screenshot;
        Sha256Hex = sha256Hex;
        ContentDigest = contentDigest;
    }

    public BundleManifestV6 Manifest { get; }
    public byte[] ManifestBytes { get; }
    public byte[] RunPayload { get; }
    public byte[]? Screenshot { get; }
    public string Sha256Hex { get; }
    public string ContentDigest { get; }
}

internal sealed class BundleV6Exception : Exception
{
    public BundleV6Exception(string code, string reason, string message)
        : base(message)
    {
        Code = code;
        Reason = reason;
    }

    public BundleV6Exception(string code, string reason, string message, Exception innerException)
        : base(message, innerException)
    {
        Code = code;
        Reason = reason;
    }

    public string Code { get; }
    public string Reason { get; }
}

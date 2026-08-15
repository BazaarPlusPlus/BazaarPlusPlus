#nullable enable
namespace BazaarPlusPlus.ModApi.Bundle;

public sealed class BundleManifestV5
{
    public string BundleId { get; set; } = string.Empty;
    public int BundleVersion { get; set; } = BundleLimitsV5.BundleVersion;
    public long CreatedAtMs { get; set; }
    public BundleRunManifestV5 Run { get; set; } = new();
    public BundleScreenshotManifestV5? Screenshot { get; set; }
}

public sealed class BundleRunManifestV5
{
    public string RunId { get; set; } = string.Empty;
    public string PlayerAccountId { get; set; } = string.Empty;
    public int RunFormatVersion { get; set; } = BundleLimitsV5.RunFormatVersion;
    public BundleProjectionV5 Projection { get; set; } = new();
    public BundleSegmentManifestV5 Payload { get; set; } = new();
}

public sealed class BundleProjectionV5
{
    public List<BundleBattleProjectionV5> Battles { get; set; } = new();
}

public sealed class BundleBattleProjectionV5
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
    public BundleCombatantProjectionV5 Player { get; set; } = new();
    public BundleCombatantProjectionV5 Opponent { get; set; } = new();
}

public sealed class BundleCombatantProjectionV5
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

public class BundleSegmentManifestV5
{
    public long Offset { get; set; }
    public long Length { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
}

public sealed class BundleScreenshotManifestV5 : BundleSegmentManifestV5
{
    public long Width { get; set; }
    public long Height { get; set; }
    public long Quality { get; set; }
    public long CapturedAtMs { get; set; }
}

public sealed class BundleBuildInputV5
{
    public string BundleId { get; set; } = string.Empty;
    public long CreatedAtMs { get; set; }
    public string RunId { get; set; } = string.Empty;
    public string PlayerAccountId { get; set; } = string.Empty;
    public IReadOnlyList<BundleBattleProjectionV5> Battles { get; set; } =
        Array.Empty<BundleBattleProjectionV5>();
    public ReadOnlyMemory<byte> RunPayload { get; set; }
    public BundleScreenshotBuildInputV5? Screenshot { get; set; }
}

public sealed class BundleScreenshotBuildInputV5
{
    public ReadOnlyMemory<byte> Bytes { get; set; }
    public string ContentType { get; set; } = BundleLimitsV5.JpegContentType;
    public int Width { get; set; }
    public int Height { get; set; }
    public int Quality { get; set; }
    public long CapturedAtMs { get; set; }
}

public sealed class BundleBuildResultV5
{
    public BundleBuildResultV5(
        byte[] bytes,
        byte[] manifestBytes,
        BundleManifestV5 manifest,
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
    public BundleManifestV5 Manifest { get; }
    public string Sha256Hex { get; }
    public string ContentDigest { get; }
}

public sealed class OpenedBundleV5
{
    public OpenedBundleV5(
        BundleManifestV5 manifest,
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

    public BundleManifestV5 Manifest { get; }
    public byte[] ManifestBytes { get; }
    public byte[] RunPayload { get; }
    public byte[]? Screenshot { get; }
    public string Sha256Hex { get; }
    public string ContentDigest { get; }
}

public sealed class BundleV5Exception : Exception
{
    public BundleV5Exception(string code, string reason, string message)
        : base(message)
    {
        Code = code;
        Reason = reason;
    }

    public BundleV5Exception(string code, string reason, string message, Exception innerException)
        : base(message, innerException)
    {
        Code = code;
        Reason = reason;
    }

    public string Code { get; }
    public string Reason { get; }
}

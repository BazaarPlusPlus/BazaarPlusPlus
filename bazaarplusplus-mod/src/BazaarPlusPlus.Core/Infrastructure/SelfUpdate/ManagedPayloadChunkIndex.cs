#nullable enable
using System.Globalization;
using System.Text;
using BazaarPlusPlus.ManagedPayload;

namespace BazaarPlusPlus.Infrastructure.SelfUpdate;

internal sealed record ManagedPayloadChunkRecord(
    string RawSha256,
    int RawSize,
    string CompressedSha256,
    int CompressedSize
);

internal sealed record ManagedPayloadChunkedFile(
    string Path,
    long Size,
    string Sha256,
    IReadOnlyList<int> Chunks
);

// release/managed-payload-chunks.ts owns this line-oriented format; release/fixtures/managed-payload/chunks.txt pins it.
internal sealed class ManagedPayloadChunkIndex
{
    public const string Header = "bpp-managed-payload-chunks 1";
    public const string CompressionName = "deflate-raw";
    public const int MaxIndexBytes = 4 * 1024 * 1024;
    public const int MaxManifestBytes = 1024 * 1024;
    public const int MaxManifestSignatureBytes = 16 * 1024;
    public const int MaxCompressedOverhead = 1024;
    public const int MaxChunkCount = 65536;
    public const int MaxFileCount = 4096;
    public const int MaxChunkReferences = 131072;

    private ManagedPayloadChunkIndex(
        string version,
        string platform,
        FastCdcParameters chunking,
        byte[] manifestBytes,
        byte[] manifestSignatureBytes,
        string manifestSignatureText,
        ManagedPayloadManifest manifest,
        IReadOnlyList<ManagedPayloadChunkRecord> chunks,
        IReadOnlyList<ManagedPayloadChunkedFile> files
    )
    {
        Version = version;
        Platform = platform;
        Chunking = chunking;
        ManifestBytes = manifestBytes;
        ManifestSignatureBytes = manifestSignatureBytes;
        ManifestSignatureText = manifestSignatureText;
        Manifest = manifest;
        Chunks = chunks;
        Files = files;
    }

    public string Version { get; }
    public string Platform { get; }
    public FastCdcParameters Chunking { get; }
    public byte[] ManifestBytes { get; }
    public byte[] ManifestSignatureBytes { get; }
    public string ManifestSignatureText { get; }
    public ManagedPayloadManifest Manifest { get; }
    public IReadOnlyList<ManagedPayloadChunkRecord> Chunks { get; }
    public IReadOnlyList<ManagedPayloadChunkedFile> Files { get; }

    public static ManagedPayloadChunkIndex Parse(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0 || bytes.Length > MaxIndexBytes)
            throw new FormatException("Managed Payload Chunk Index size is out of bounds.");
        return Parse(new UTF8Encoding(false, true).GetString(bytes));
    }

    public static ManagedPayloadChunkIndex Parse(string text)
    {
        if (text == null || !text.EndsWith("\n", StringComparison.Ordinal) || text.Contains("\r"))
            throw new FormatException("A Managed Payload Chunk Index must be LF-terminated lines.");
        if (Encoding.UTF8.GetByteCount(text) > MaxIndexBytes)
            throw new FormatException("Managed Payload Chunk Index exceeds maximum allowed size.");

        var lines = text.Substring(0, text.Length - 1).Split('\n');
        if (lines.Length < 8 || lines[0] != Header)
            throw new FormatException("Not a Managed Payload Chunk Index.");

        var version = Field(lines[1], "version");
        if (!ManagedPayloadVersion.TryParse(version, out _) || version.Split('.').Length != 3)
            throw new FormatException($"Invalid chunk index version '{version}'.");

        var platform = Field(lines[2], "platform");
        if (
            platform.Length == 0
            || platform.Any(c => !(char.IsLetterOrDigit(c) || c == '-' || c == '_'))
        )
            throw new FormatException($"Invalid chunk index platform '{platform}'.");

        var chunkingParts = Field(lines[3], "chunking").Split(' ');
        if (
            chunkingParts.Length != 4
            || chunkingParts[0] != FastCdcParameters.AlgorithmName
            || !TryParseStrictInt(chunkingParts[1], out var minSize)
            || !TryParseStrictInt(chunkingParts[2], out var avgSize)
            || !TryParseStrictInt(chunkingParts[3], out var maxSize)
        )
            throw new FormatException("Invalid chunking header.");
        var chunking = new FastCdcParameters(minSize, avgSize, maxSize);
        if (!chunking.TryValidate(out _))
            throw new FormatException("Unsupported FastCDC parameters.");

        var compression = Field(lines[4], "compression");
        if (compression != CompressionName)
            throw new FormatException($"Unsupported chunk compression '{compression}'.");

        var manifestBytes = DecodeStrictBase64(
            Field(lines[5], "manifest-base64"),
            MaxManifestBytes
        );
        var manifestSignatureBytes = DecodeStrictBase64(
            Field(lines[6], "manifest-sig-base64"),
            MaxManifestSignatureBytes
        );
        var manifestSignatureText = new UTF8Encoding(false, true).GetString(manifestSignatureBytes);
        if (string.IsNullOrWhiteSpace(manifestSignatureText))
            throw new FormatException("Embedded manifest signature is empty.");

        var manifest = ManagedPayloadManifest.Parse(manifestBytes);
        if (!string.Equals(manifest.Version, version, StringComparison.Ordinal))
            throw new FormatException("Chunk index version differs from manifest version.");
        if (!string.Equals(manifest.Platform, platform, StringComparison.Ordinal))
            throw new FormatException("Chunk index platform differs from manifest platform.");

        var chunks = new List<ManagedPayloadChunkRecord>();
        var seenRawSha = new HashSet<string>(StringComparer.Ordinal);
        var files = new List<ManagedPayloadChunkedFile>();
        var inFiles = false;
        var totalRefs = 0;

        for (var i = 7; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.StartsWith("chunk ", StringComparison.Ordinal))
            {
                if (inFiles)
                    throw new FormatException($"Chunk entry after file entries at line {i + 1}.");
                if (chunks.Count >= MaxChunkCount)
                    throw new FormatException("Chunk count exceeds limit.");
                var parts = line.Split(' ');
                if (
                    parts.Length != 5
                    || !IsSha256(parts[1])
                    || !TryParseStrictInt(parts[2], out var rawSize)
                    || !IsSha256(parts[3])
                    || !TryParseStrictInt(parts[4], out var compressedSize)
                    || rawSize <= 0
                    || rawSize > chunking.MaxSize
                    || compressedSize <= 0
                    || compressedSize > chunking.MaxSize + MaxCompressedOverhead
                )
                    throw new FormatException($"Invalid chunk line {i + 1}.");
                if (!seenRawSha.Add(parts[1]))
                    throw new FormatException($"Duplicate chunk digest at line {i + 1}.");
                chunks.Add(
                    new ManagedPayloadChunkRecord(parts[1], rawSize, parts[3], compressedSize)
                );
                continue;
            }

            if (line.StartsWith("file ", StringComparison.Ordinal))
            {
                inFiles = true;
                if (files.Count >= MaxFileCount)
                    throw new FormatException("File count exceeds limit.");
                var parts = line.Split(new[] { ' ' }, 5);
                if (
                    parts.Length != 5
                    || parts[0] != "file"
                    || !IsSha256(parts[1])
                    || !TryParseStrictLong(parts[2], out var fileSize)
                    || parts[3].Length == 0
                    || !ManagedPayloadPaths.IsSafeRelative(parts[4])
                )
                    throw new FormatException($"Invalid file line {i + 1}.");

                var refs = new List<int>();
                if (parts[3] == "-")
                {
                    if (fileSize != 0)
                        throw new FormatException($"Non-empty file has no chunks at line {i + 1}.");
                }
                else
                {
                    var tokens = parts[3].Split(',');
                    totalRefs += tokens.Length;
                    if (totalRefs > MaxChunkReferences)
                        throw new FormatException("Chunk reference count exceeds limit.");
                    long sum = 0;
                    foreach (var token in tokens)
                    {
                        if (!TryParseStrictInt(token, out var idx) || idx >= chunks.Count)
                            throw new FormatException($"Invalid chunk index at line {i + 1}.");
                        refs.Add(idx);
                        sum += chunks[idx].RawSize;
                    }
                    if (sum != fileSize)
                        throw new FormatException($"File chunk size sum mismatch at line {i + 1}.");
                }

                files.Add(new ManagedPayloadChunkedFile(parts[4], fileSize, parts[1], refs));
                continue;
            }

            throw new FormatException($"Unexpected chunk index line {i + 1}.");
        }

        if (files.Count != manifest.Files.Count)
            throw new FormatException("Chunk index file count differs from manifest.");
        for (var i = 0; i < files.Count; i++)
        {
            var file = files[i];
            var expected = manifest.Files[i];
            if (
                !string.Equals(file.Path, expected.Path, StringComparison.Ordinal)
                || file.Size != expected.Size
                || !string.Equals(file.Sha256, expected.Sha256, StringComparison.Ordinal)
            )
                throw new FormatException("Chunk index file list differs from manifest.");
        }

        return new ManagedPayloadChunkIndex(
            version,
            platform,
            chunking,
            manifestBytes,
            manifestSignatureBytes,
            manifestSignatureText,
            manifest,
            chunks,
            files
        );
    }

    private static string Field(string line, string name)
    {
        var prefix = name + " ";
        if (!line.StartsWith(prefix, StringComparison.Ordinal))
            throw new FormatException($"Expected chunk index field '{name}'.");
        return line.Substring(prefix.Length);
    }

    private static byte[] DecodeStrictBase64(string value, int maxBytes)
    {
        if (!IsStrictBase64(value))
            throw new FormatException("Invalid base64 field.");
        // Upper bound decoded size before allocating.
        var maxPossible = (value.Length / 4) * 3;
        if (maxPossible - 2 > maxBytes)
            throw new FormatException("Base64 field exceeds maximum decoded size.");
        byte[] decoded;
        try
        {
            decoded = Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            throw new FormatException("Invalid base64 field.");
        }
        if (decoded.Length == 0 || decoded.Length > maxBytes)
            throw new FormatException("Decoded base64 field size is out of bounds.");
        if (!string.Equals(Convert.ToBase64String(decoded), value, StringComparison.Ordinal))
            throw new FormatException("Non-canonical base64 field.");
        return decoded;
    }

    private static bool IsStrictBase64(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length % 4 != 0)
            return false;
        var padding = 0;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '=')
            {
                padding++;
                if (padding > 2 || i < value.Length - 2)
                    return false;
                continue;
            }
            if (padding > 0)
                return false;
            var valid =
                (c >= 'A' && c <= 'Z')
                || (c >= 'a' && c <= 'z')
                || (c >= '0' && c <= '9')
                || c == '+'
                || c == '/';
            if (!valid)
                return false;
        }
        return true;
    }

    private static bool TryParseStrictInt(string value, out int result)
    {
        result = 0;
        if (string.IsNullOrEmpty(value) || (value.Length > 1 && value[0] == '0'))
            return false;
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result);
    }

    private static bool TryParseStrictLong(string value, out long result)
    {
        result = 0;
        if (string.IsNullOrEmpty(value) || (value.Length > 1 && value[0] == '0'))
            return false;
        return long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result);
    }

    internal static bool IsSha256(string value) =>
        value.Length == 64 && value.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'));
}

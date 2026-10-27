using System.IO.Compression;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.HistoryPanel.Favorites;
using BazaarPlusPlus.Game.HistoryPanel.Ghost;
using BazaarPlusPlus.Infrastructure;

internal static class FileBackedPayloadDiagnosticsTests
{
    internal static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), $"bpp-payload-diagnostics-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var replayRoot = Path.Combine(root, "Replay");
            CheckInvalidFiles(
                replayRoot,
                ".payload.mpack.gz",
                new CombatReplayPayloadStore(replayRoot).LoadDetailed
            );
            var ghostRoot = Path.Combine(root, "Ghost");
            CheckInvalidFiles(
                ghostRoot,
                ".ghost.mpack.gz",
                new GhostBattlePayloadStore(ghostRoot).LoadDetailed
            );
            var favoriteRoot = Path.Combine(root, "Favorite");
            CheckInvalidFiles(
                favoriteRoot,
                ".favorite.mpack.gz",
                new FavoriteBattlePayloadStore(favoriteRoot).LoadDetailed
            );
            CheckFailureDetails(root);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static void CheckInvalidFiles<T>(
        string root,
        string suffix,
        Func<string, FileBackedPayloadLoadResult<T>> load
    )
        where T : class
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
            gzip.WriteByte(0xc0);
        (byte[] Bytes, string Reason)[] cases =
        [
            ([], "payload_empty"),
            ([1, 2, 3], "payload_not_gzip"),
            (output.ToArray(), "payload_deserialized_null"),
        ];
        foreach (var (bytes, reason) in cases)
        {
            var path = Path.Combine(root, reason + suffix);
            File.WriteAllBytes(path, bytes);
            var result = load(reason);
            Check(
                result.Status == FileBackedPayloadLoadStatus.Invalid,
                "Decode failure stays Invalid."
            );
            Check(
                result.Exception is InvalidDataException
                    && result.Exception.Message.Contains(reason, StringComparison.Ordinal)
                    && result.Exception.Message.Contains(typeof(T).Name, StringComparison.Ordinal),
                $"{typeof(T).Name} must retain the codec reason {reason} in its diagnostic."
            );
            Check(File.Exists(path), "Diagnostic handling must retain the source file.");
        }
        Check(
            load("absent").Status == FileBackedPayloadLoadStatus.Missing,
            "Missing stays Missing."
        );
    }

    private static void CheckFailureDetails(string root)
    {
        Exception? original = null;
        string? reason = "payload_too_large";
        bool Decode(
            byte[]? bytes,
            out GhostBattlePayload? payload,
            out string? error,
            out Exception? exception
        )
        {
            payload = null;
            error = reason;
            exception = original;
            return false;
        }
        var store = new FileBackedPayloadStore<GhostBattlePayload>(
            root,
            ".test",
            _ => [1],
            Decode,
            maximumFileBytes: 2
        );
        store.Save("decode", new GhostBattlePayload());
        var oversized = store.LoadDetailed("decode");
        Check(
            oversized.Status == FileBackedPayloadLoadStatus.Invalid
                && oversized.Exception is InvalidDataException
                && oversized.Exception.Message.Contains(
                    nameof(GhostBattlePayload),
                    StringComparison.Ordinal
                )
                && oversized.Exception.Message.Contains(
                    "payload_too_large",
                    StringComparison.Ordinal
                ),
            "Decompressed overflow keeps the payload type and codec reason."
        );
        original = new MissingMethodException("runtime binding failure");
        Check(
            ReferenceEquals(store.LoadDetailed("decode").Exception, original),
            "An original runtime exception must survive even when a codec also reports a size reason."
        );
        reason = null;
        Check(
            ReferenceEquals(store.LoadDetailed("decode").Exception, original),
            "The original exception must pass through unchanged."
        );
        original = null;
        Check(
            store
                .LoadDetailed("decode")
                .Exception?.Message.Contains("payload_decode_failed", StringComparison.Ordinal)
                == true,
            "A codec without a reason still produces a diagnostic."
        );
        File.WriteAllBytes(Path.Combine(root, "read.test"), [1, 2, 3]);
        var unreadable = store.LoadDetailed("read");
        Check(
            unreadable.Status == FileBackedPayloadLoadStatus.Unreadable
                && unreadable.Exception?.Message.Contains(
                    nameof(GhostBattlePayload),
                    StringComparison.Ordinal
                ) == true,
            "Compressed overflow stays Unreadable and identifies its payload type."
        );
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}

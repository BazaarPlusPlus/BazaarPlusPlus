#nullable enable
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BazaarPlusPlus.Game.VoiceSubtitles;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.TestSupport;
using Xunit;

// BppLog is process-global; one test's sink must not capture another's events.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace VoiceSubtitles.Tests;

public sealed class VoiceLinesDocumentTests
{
    [Fact]
    public void Seed_document_computed_content_hash_uses_sha256_format()
    {
        var json = TestInputs.Fixture("tests/TestData/remote-data/voice-lines.json");
        var lines = VoiceLinesDocument.Parse(json, VoiceCatalogSource.Embedded);

        var contentHash = VoiceLinesDocument.ComputeContentHash(lines);

        Assert.NotEmpty(lines);
        Assert.StartsWith("sha256:", contentHash, StringComparison.Ordinal);
        Assert.Equal(71, contentHash.Length);
    }

    [Fact]
    public void Parser_rejects_count_mismatch()
    {
        var json = BuildVoiceLinesJson(
            count: 2,
            contentHash: ContentHashFor(("stem", "English", "中文", 1.25)),
            ("stem", "English", "中文", 1.25)
        );

        var ex = Assert.Throws<InvalidOperationException>(() =>
            VoiceLinesDocument.Parse(json, VoiceCatalogSource.Embedded)
        );
        Assert.Contains("count", ex.Message);
    }

    [Fact]
    public void Parser_rejects_content_hash_mismatch()
    {
        var json = BuildVoiceLinesJson(
            count: 1,
            contentHash: "sha256:0000000000000000000000000000000000000000000000000000000000000000",
            ("stem", "English", "中文", 1.25)
        );

        var ex = Assert.Throws<InvalidOperationException>(() =>
            VoiceLinesDocument.Parse(json, VoiceCatalogSource.Embedded)
        );
        Assert.Contains("contentHash", ex.Message);
    }

    [Fact]
    public void Malformed_rows_emit_typed_debug_skips_without_operational_warnings()
    {
        var json = BuildVoiceLinesJson(
            count: 1,
            contentHash: ContentHashFor(("valid", "English", "中文", 1.25)),
            ("", "missing", "缺失", 1),
            ("valid", "English", "中文", 1.25),
            ("valid", "duplicate", "重复", 1),
            ("empty", "", "", 1)
        );
        var sink = new ListLogSink();
        BppLog.Install(sink);

        var lines = VoiceLinesDocument.Parse(json, VoiceCatalogSource.Remote);
        BppLog.Flush();

        Assert.Equal("valid", Assert.Single(lines).Stem);
        var skipped = sink.Events("voice_subtitles.catalog_row.skipped");
#if DEBUG
        Assert.Equal(3, skipped.Count);
        Assert.All(skipped, entry => Assert.Equal(BppLogSeverity.Debug, entry.Severity));
        Assert.Contains(skipped, entry => entry.Message.Contains("reason_code=missing_stem"));
        Assert.Contains(skipped, entry => entry.Message.Contains("reason_code=duplicate_stem"));
        Assert.Contains(skipped, entry => entry.Message.Contains("reason_code=empty_text"));
#else
        Assert.Empty(skipped);
#endif
        Assert.DoesNotContain(
            sink.All,
            entry => entry.Severity is BppLogSeverity.Warning or BppLogSeverity.Error
        );
    }

    private static string BuildVoiceLinesJson(
        int count,
        string contentHash,
        params (string Stem, string English, string Chinese, double DurationSeconds)[] lines
    )
    {
        var lineJson = string.Join(
            ",",
            lines.Select(line =>
                "{\"stem\":\""
                + line.Stem
                + "\",\"english\":\""
                + line.English
                + "\",\"chinese\":\""
                + line.Chinese
                + "\",\"durationSeconds\":"
                + line.DurationSeconds.ToString(CultureInfo.InvariantCulture)
                + "}"
            )
        );
        return "{\"schemaVersion\":1,\"contentHash\":\""
            + contentHash
            + "\",\"generatedAt\":\"2026-07-03T00:00:00Z\",\"count\":"
            + count
            + ",\"lines\":["
            + lineJson
            + "]}";
    }

    // An independent restatement of the canonical form, so the parser is not checked against itself.
    private static string ContentHashFor(
        params (string Stem, string English, string Chinese, double DurationSeconds)[] lines
    )
    {
        var records = lines.Select(line =>
            string.Join(
                "\x1f",
                line.Stem,
                line.English,
                line.Chinese,
                ((long)Math.Round(line.DurationSeconds * 100.0)).ToString(
                    CultureInfo.InvariantCulture
                )
            )
        );
        var canonical = string.Join("\x1e", records);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return "sha256:" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private sealed class ListLogSink : IBppLogSink
    {
        private readonly List<(BppLogSeverity Severity, string Message)> _entries = [];

        internal IReadOnlyList<(BppLogSeverity Severity, string Message)> All => _entries;

        public void Write(BppLogSeverity severity, string message) =>
            _entries.Add((severity, message));

        internal IReadOnlyList<(BppLogSeverity Severity, string Message)> Events(string eventId) =>
            _entries
                .Where(entry =>
                    entry.Message.Contains("event=" + eventId, StringComparison.Ordinal)
                )
                .ToArray();
    }
}

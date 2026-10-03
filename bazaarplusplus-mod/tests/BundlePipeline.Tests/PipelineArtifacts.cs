#nullable enable
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using BazaarPlusPlus.ModApi.Bundle;
using BazaarPlusPlus.TestSupport;
using Json.Schema;
using Microsoft.Data.Sqlite;

/// <summary>
/// The Bundle pipeline anchor's artifact: every sealed Bundle and manifest under
/// <c>artifacts/bundle-pipeline/</c>, and one normalized summary compared byte for byte with
/// <c>fixtures/bundle-pipeline.golden.json</c>. Regenerate with
/// <c>BPP_UPDATE_GOLDENS=1 dotnet run --project tests/BundlePipeline.Tests/BundlePipeline.Tests.csproj</c>
/// from the mod root, then review <c>git diff tests/BundlePipeline.Tests/fixtures/</c>.
///
/// Only <c>bundle_id</c>, <c>created_at_ms</c> (UlidV5Generator and the seal clock), the values
/// derived from them (file name, whole-Bundle SHA-256 and Content-Digest), and <c>*_at_utc</c>
/// are normalized. Run payload and screenshot digests stay raw: a change there means the payload
/// picked up an unfixed time or random source.
/// </summary>
internal sealed class PipelineArtifacts
{
    private const string GoldenRelativePath =
        "tests/BundlePipeline.Tests/fixtures/bundle-pipeline.golden.json";
    private const string Ulid = "<ulid>";
    private const string Utc = "<utc>";
    private const string Normalized = "<normalized>";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _directory;
    private readonly JsonSchema _manifestSchema;
    private readonly JsonObject _manifests = new();
    private readonly JsonObject _checksums = new();
    private readonly JsonArray _queue = new();
    private readonly JsonArray _uploads = new();
    private readonly JsonObject _screenshots = new();
    private readonly Dictionary<string, string> _bundleNamesBySha = new(StringComparer.Ordinal);

    internal PipelineArtifacts()
    {
        _directory = Path.Combine(TestInputs.RepoRoot, "artifacts", "bundle-pipeline");
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
        Directory.CreateDirectory(_directory);
        // The built schema keeps reading its source elements, so it gets a detached copy.
        using var schema = JsonDocument.Parse(Resource("manifest.schema.json"));
        _manifestSchema = JsonSchema.Build(schema.RootElement.Clone());
        VerifyServerChecksumsUseOurMeasure();
    }

    /// <summary>
    /// Writes the raw Bundle and manifest, validates the manifest against the server-owned
    /// schema before normalization, and records the normalized manifest and checksum entry.
    /// </summary>
    internal OpenedBundleV5 RecordBundle(string name, byte[] bytes)
    {
        var opened = BundleV5Codec.Open(bytes);
        File.WriteAllBytes(Path.Combine(_directory, name + ".bundle"), bytes);
        File.WriteAllBytes(Path.Combine(_directory, name + ".manifest.json"), opened.ManifestBytes);

        using (var manifest = JsonDocument.Parse(opened.ManifestBytes))
        {
            var validation = _manifestSchema.Evaluate(
                manifest.RootElement,
                new EvaluationOptions { OutputFormat = OutputFormat.List }
            );
            Check.That(
                validation.IsValid,
                $"{name}: sealed manifest violates the server manifest.schema.json: "
                    + JsonSerializer.Serialize(validation)
            );
        }

        var normalized = JsonNode.Parse(opened.ManifestBytes)!.AsObject();
        normalized["bundle_id"] = Ulid;
        normalized["created_at_ms"] = 0;
        _manifests[name] = normalized;
        _checksums[name + ".bundle"] = new JsonObject
        {
            ["decoded_bytes"] = bytes.Length,
            ["manifest_bytes"] = opened.ManifestBytes.Length,
            ["sha256"] = Normalized,
            ["expected"] = "valid",
        };
        _bundleNamesBySha[opened.Sha256Hex] = name;
        return opened;
    }

    /// <summary>Snapshots the queue rows of <paramref name="runIds"/> at the end of a step.</summary>
    internal void RecordQueue(string step, string database, params string[] runIds)
    {
        using var connection = new SqliteConnection($"Data Source={database}");
        connection.Open();
        _queue.Add(
            new JsonObject
            {
                ["step"] = step,
                ["bundle_seal_jobs"] = Rows(
                    connection,
                    "SELECT run_id, state, screenshot_requested, screenshot_state, input_deadline_at_utc, bundle_id, created_at_ms, attempts, last_attempt_at_utc, last_error_code FROM bundle_seal_jobs",
                    runIds
                ),
                ["bundle_outbox"] = Rows(
                    connection,
                    "SELECT run_id, bundle_id, file_name, content_sha256_hex, content_digest, total_bytes, has_screenshot, sealed_at_utc, status, next_attempt_at_utc, attempts, last_attempt_at_utc, last_error_code, failed_at_utc, uploaded_at_utc, server_outcome FROM bundle_outbox",
                    runIds
                ),
            }
        );
    }

    internal void RecordScreenshots(string database, string runId)
    {
        using var connection = new SqliteConnection($"Data Source={database}");
        connection.Open();
        _screenshots[runId] = Rows(
            connection,
            "SELECT screenshot_id, run_id, hero_name, is_primary, image_relative_path, captured_at_local, captured_at_utc, day, player_rank, player_rating, player_position, victories_at_capture, capture_source FROM run_screenshots",
            [runId],
            normalizeTimes: false
        );
    }

    internal void RecordUpload(string step, RecordedUpload upload)
    {
        Check.That(
            _bundleNamesBySha.TryGetValue(upload.BodySha256, out var name),
            $"{step}: uploaded body is not byte-identical to any sealed Bundle."
        );
        _uploads.Add(
            new JsonObject
            {
                ["step"] = step,
                ["method"] = upload.Method,
                ["route"] = upload.Route,
                ["content_type"] = upload.ContentType,
                ["body"] = $"<bundle:{name}>",
                ["length"] = upload.Length,
                ["response_status"] = upload.ResponseStatus,
            }
        );
    }

    /// <summary>
    /// Writes <c>bundle-pipeline.actual.json</c> and compares it with the golden, or rewrites
    /// the golden under <c>BPP_UPDATE_GOLDENS=1</c>. Returns false on a mismatch.
    /// </summary>
    internal bool CompareWithGolden()
    {
        var summary = new JsonObject
        {
            ["manifests"] = _manifests,
            ["checksums"] = _checksums,
            ["run_screenshots"] = _screenshots,
            ["queue"] = _queue,
            ["uploads"] = _uploads,
        };
        var actual = summary.ToJsonString(JsonOptions).Replace("\r\n", "\n") + "\n";
        var actualPath = Path.Combine(_directory, "bundle-pipeline.actual.json");
        File.WriteAllText(actualPath, actual, new UTF8Encoding(false));

        var goldenPath = Path.Combine(TestInputs.RepoRoot, GoldenRelativePath);
        if (Environment.GetEnvironmentVariable("BPP_UPDATE_GOLDENS") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(goldenPath)!);
            File.WriteAllText(goldenPath, actual, new UTF8Encoding(false));
            Console.WriteLine($"Rewrote {GoldenRelativePath}; review it with git diff.");
            return true;
        }
        if (!File.Exists(goldenPath))
        {
            Console.Error.WriteLine(
                $"{GoldenRelativePath} is missing; create it with BPP_UPDATE_GOLDENS=1."
            );
            return false;
        }
        var expected = TestInputs.Fixture(GoldenRelativePath).Replace("\r\n", "\n");
        if (string.Equals(expected, actual, StringComparison.Ordinal))
            return true;
        Console.Error.WriteLine(
            $"Bundle pipeline output differs from {GoldenRelativePath}; actual output is "
                + $"artifacts/bundle-pipeline/bundle-pipeline.actual.json. Regenerate with "
                + "BPP_UPDATE_GOLDENS=1 only if the change is intended."
        );
        Console.Error.WriteLine(UnifiedDiff.Render(expected, actual, GoldenRelativePath));
        return false;
    }

    internal static string Resource(string name)
    {
        using var stream =
            Assembly
                .GetExecutingAssembly()
                .GetManifestResourceStream("BundlePipeline.Tests." + name)
            ?? throw new InvalidOperationException($"The server-owned contract {name} is missing.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    // The checksums section reuses the server fixture's measure; prove it on the server golden.
    private static void VerifyServerChecksumsUseOurMeasure()
    {
        var bytes = Convert.FromBase64String(Resource("run-only.bundle.b64").Trim());
        var opened = BundleV5Codec.Open(bytes);
        using var checksums = JsonDocument.Parse(Resource("checksums.json"));
        var entry = checksums.RootElement.GetProperty("run-only.bundle.b64");
        Check.That(
            entry.GetProperty("decoded_bytes").GetInt32() == bytes.Length
                && entry.GetProperty("manifest_bytes").GetInt32() == opened.ManifestBytes.Length
                && entry.GetProperty("sha256").GetString() == opened.Sha256Hex
                && entry.GetProperty("expected").GetString() == "valid",
            "The codec's decoded_bytes, manifest_bytes, and sha256 must match the server checksums.json."
        );
    }

    private static JsonArray Rows(
        SqliteConnection connection,
        string select,
        string[] runIds,
        bool normalizeTimes = true
    )
    {
        using var command = connection.CreateCommand();
        var names = runIds.Select((_, index) => "$run" + index).ToArray();
        command.CommandText =
            $"{select} WHERE run_id IN ({string.Join(", ", names)}) ORDER BY run_id;";
        for (var index = 0; index < runIds.Length; index++)
            command.Parameters.AddWithValue(names[index], runIds[index]);
        using var reader = command.ExecuteReader();
        var rows = new JsonArray();
        while (reader.Read())
        {
            var row = new JsonObject();
            for (var column = 0; column < reader.FieldCount; column++)
            {
                var name = reader.GetName(column);
                row[name] = reader.IsDBNull(column)
                    ? null
                    : Normalize(name, reader.GetValue(column), normalizeTimes);
            }
            rows.Add(row);
        }
        return rows;
    }

    private static JsonNode? Normalize(string column, object value, bool normalizeTimes) =>
        column switch
        {
            "bundle_id" => Ulid,
            "file_name" => Ulid + ".bundle",
            "created_at_ms" => 0,
            "content_sha256_hex" or "content_digest" => Normalized,
            _ when normalizeTimes && column.EndsWith("_at_utc", StringComparison.Ordinal) => Utc,
            _ => value switch
            {
                long number => number,
                string text => text,
                double real => real,
                _ => throw new InvalidOperationException(
                    $"Unexpected SQLite value for {column}: {value.GetType()}"
                ),
            },
        };
}

internal sealed record RecordedUpload(
    string Method,
    string Route,
    string ContentType,
    string BodySha256,
    long Length,
    int ResponseStatus
);

internal static class Check
{
    internal static void That(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}

/// <summary>A minimal line diff (LCS) with three lines of context, for golden mismatches.</summary>
internal static class UnifiedDiff
{
    internal static string Render(string expected, string actual, string label)
    {
        var a = expected.Split('\n');
        var b = actual.Split('\n');
        var lengths = new int[a.Length + 1, b.Length + 1];
        for (var i = a.Length - 1; i >= 0; i--)
        for (var j = b.Length - 1; j >= 0; j--)
            lengths[i, j] =
                a[i] == b[j]
                    ? lengths[i + 1, j + 1] + 1
                    : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);

        var lines = new List<(char Kind, string Text)>();
        int x = 0,
            y = 0;
        while (x < a.Length || y < b.Length)
        {
            if (x < a.Length && y < b.Length && a[x] == b[y])
            {
                lines.Add((' ', a[x]));
                x++;
                y++;
            }
            else if (x < a.Length && (y == b.Length || lengths[x + 1, y] >= lengths[x, y + 1]))
                lines.Add(('-', a[x++]));
            else
                lines.Add(('+', b[y++]));
        }

        const int context = 3;
        var output = new StringBuilder($"--- {label}\n+++ actual\n");
        var printedThrough = -1;
        for (var index = 0; index < lines.Count; index++)
        {
            if (lines[index].Kind == ' ')
                continue;
            var start = Math.Max(printedThrough + 1, index - context);
            if (start > printedThrough + 1)
                output.Append("@@\n");
            var end = index;
            while (
                end + 1 < lines.Count
                && (
                    lines[end + 1].Kind != ' '
                    || lines.Skip(end + 1).Take(context * 2).Any(line => line.Kind != ' ')
                )
            )
                end++;
            end = Math.Min(lines.Count - 1, end + context);
            for (var line = start; line <= end; line++)
                output.Append(lines[line].Kind).Append(lines[line].Text).Append('\n');
            printedThrough = end;
            index = end;
        }
        return output.ToString();
    }
}

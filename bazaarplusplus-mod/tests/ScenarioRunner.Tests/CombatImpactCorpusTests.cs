using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using BazaarPlusPlus.TestSupport;
using Xunit;
using Xunit.Abstractions;

namespace ScenarioRunner.Tests;

/// <summary>
/// The host-lane half of the Combat Impact Corpus golden (ADR-0019): runs
/// <c>CombatImpact.CorpusExport export</c> over the committed game payloads
/// (<c>tests/CombatImpact.CorpusExport/corpus/</c>) and requires the BPP timeline it writes to
/// match the committed corpus <c>tests/CombatImpact.Corpus/corpus/pinned-corpus.json.gz</c>,
/// which the Core-lane runner <c>CombatImpact.Corpus</c> projects. Regenerate with
/// <c>BPP_UPDATE_GOLDENS=1</c>; <c>tests/CombatImpact.Corpus/evidence/README.md</c> owns the
/// procedure.
///
/// The decompressed JSON must match except the hashes and versions of the <c>game_types</c> and
/// <c>json_runtime</c> verification inputs: both come from the Managed snapshot the exporter ran
/// against and change with every game update. A difference there is printed; every battle must
/// still match.
/// </summary>
public sealed partial class ScenarioRunnerTests(ITestOutputHelper output)
{
    private const string CorpusExporter = "CombatImpact.CorpusExport";
    private const string GamePayloads = "tests/CombatImpact.CorpusExport/corpus";
    private const string PinnedCorpus = "tests/CombatImpact.Corpus/corpus/pinned-corpus.json.gz";
    private const string ActualCorpus = "artifacts/combat-impact-corpus/pinned-corpus.json.gz";
    private static readonly string[] GameManagedRoles = ["game_types", "json_runtime"];

    [Fact]
    [Trait("TestKind", "ScenarioRunner")]
    public async Task Combat_impact_corpus_export_matches_committed_corpus()
    {
        var root = TestInputs.RepoRoot;
        var actualPath = Path.Combine(root, ActualCorpus);
        var pinnedPath = Path.Combine(root, PinnedCorpus);
        if (File.Exists(actualPath))
            File.Delete(actualPath);

        await RunAsync(
            CorpusExporter,
            ["export", Path.Combine(root, GamePayloads), actualPath],
            new Dictionary<string, string>()
        );

        var actual = ReadCorpusText(actualPath);
        var expected = File.Exists(pinnedPath) ? ReadCorpusText(pinnedPath) : null;
        var differences = new List<string>();
        var gameDrift = new List<string>();
        if (expected != null)
            Compare(JsonNode.Parse(expected), JsonNode.Parse(actual), "$", differences, gameDrift);
        foreach (var line in gameDrift)
            output.WriteLine(line);

        if (Environment.GetEnvironmentVariable("BPP_UPDATE_GOLDENS") == "1")
        {
            // Rewrite only on a real difference, so a regeneration on a newer game snapshot does
            // not churn the committed file over the game-managed hashes alone.
            if (expected == null || differences.Count > 0)
            {
                File.Copy(actualPath, pinnedPath, overwrite: true);
                output.WriteLine($"Rewrote {PinnedCorpus}; review it and the evidence golden.");
            }
            return;
        }

        Assert.True(
            expected != null,
            $"{PinnedCorpus} is missing; create it with BPP_UPDATE_GOLDENS=1."
        );
        if (differences.Count == 0)
            return;
        var message = new StringBuilder()
            .AppendLine(
                $"The exported Combat Impact Corpus differs from {PinnedCorpus} in {differences.Count} field(s):"
            )
            .AppendJoin('\n', differences.Take(40).Select(line => "  " + line))
            .AppendLine(differences.Count > 40 ? $"\n  ... {differences.Count - 40} more" : "")
            .AppendJoin('\n', gameDrift)
            .AppendLine()
            .AppendLine(
                $"Actual corpus: {ActualCorpus}. If the change is intended, regenerate with "
                    + "BPP_UPDATE_GOLDENS=1 dotnet test tests/ScenarioRunner.Tests/ScenarioRunner.Tests.csproj "
                    + "--filter Combat_impact_corpus_export_matches_committed_corpus, then rerun "
                    + "CombatImpact.Corpus with BPP_UPDATE_GOLDENS=1 and review git diff."
            );
        Assert.Fail(message.ToString());
    }

    // The JSON text is the contract; the gzip framing around it is not compared.
    private static string ReadCorpusText(string path)
    {
        using var gzip = new GZipStream(File.OpenRead(path), CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, new UTF8Encoding(false));
        return reader.ReadToEnd();
    }

    private static void Compare(
        JsonNode? expected,
        JsonNode? actual,
        string path,
        List<string> differences,
        List<string> gameDrift
    )
    {
        switch (expected, actual)
        {
            case (JsonObject left, JsonObject right):
                if (GameManagedInput(left, right) is { } role)
                {
                    foreach (var key in new[] { "sha256", "version" })
                    {
                        if (!JsonNode.DeepEquals(left[key], right[key]))
                        {
                            gameDrift.Add(
                                $"Game-managed input {role} {key} differs: "
                                    + $"pinned={left[key]?.ToJsonString() ?? "null"} "
                                    + $"actual={right[key]?.ToJsonString() ?? "null"}"
                            );
                        }
                    }
                    left = Without(left, "sha256", "version");
                    right = Without(right, "sha256", "version");
                }
                // Keys compare in document order: the corpus is ordered, and the runner iterates
                // its dictionaries in that order.
                var leftKeys = left.Select(item => item.Key).ToList();
                var rightKeys = right.Select(item => item.Key).ToList();
                if (!leftKeys.SequenceEqual(rightKeys, StringComparer.Ordinal))
                {
                    differences.Add(
                        $"{path} keys: {Show(new JsonArray([.. leftKeys.Select(key => JsonValue.Create(key))]))} -> "
                            + Show(
                                new JsonArray([.. rightKeys.Select(key => JsonValue.Create(key))])
                            )
                    );
                }
                foreach (var key in leftKeys.Union(rightKeys, StringComparer.Ordinal))
                {
                    var child = $"{path}.{key}";
                    if (!left.ContainsKey(key) || !right.ContainsKey(key))
                        differences.Add($"{child}: {Show(left[key])} -> {Show(right[key])}");
                    else
                        Compare(left[key], right[key], child, differences, gameDrift);
                }
                return;
            case (JsonArray left, JsonArray right):
                for (var index = 0; index < Math.Max(left.Count, right.Count); index++)
                {
                    var child = $"{path}[{index}]";
                    if (index >= left.Count || index >= right.Count)
                    {
                        differences.Add(
                            $"{child}: {Show(index < left.Count ? left[index] : null)} -> "
                                + Show(index < right.Count ? right[index] : null)
                        );
                    }
                    else
                        Compare(left[index], right[index], child, differences, gameDrift);
                }
                return;
            default:
                if (!JsonNode.DeepEquals(expected, actual))
                    differences.Add($"{path}: {Show(expected)} -> {Show(actual)}");
                return;
        }
    }

    private static string? GameManagedInput(JsonObject left, JsonObject right)
    {
        var role = left["role"]?.GetValue<string>();
        return
            role != null
            && GameManagedRoles.Contains(role)
            && right["role"]?.GetValue<string>() == role
            && left["name"]?.GetValue<string>() == right["name"]?.GetValue<string>()
            ? role
            : null;
    }

    private static JsonObject Without(JsonObject node, params string[] keys)
    {
        var copy = node.DeepClone().AsObject();
        foreach (var key in keys)
            copy.Remove(key);
        return copy;
    }

    private static string Show(JsonNode? node)
    {
        var text = node?.ToJsonString() ?? "<absent>";
        return text.Length > 120 ? text[..117] + "..." : text;
    }
}

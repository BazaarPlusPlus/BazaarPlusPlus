using System.Text;
using System.Text.Json.Nodes;
using BazaarPlusPlus.TestSupport;
using Xunit;
using Xunit.Abstractions;

namespace ScenarioRunner.Tests;

/// <summary>
/// The Combat Impact anchor: runs <c>CombatImpact.Corpus</c> over the committed pinned corpus
/// (<c>tests/CombatImpact.Corpus/corpus/</c>) and compares the evidence it writes to
/// <c>artifacts/combat-impact-corpus/evidence.json</c> with
/// <c>tests/CombatImpact.Corpus/evidence/pinned-corpus.json</c>. Regenerate with
/// <c>BPP_UPDATE_GOLDENS=1</c>; <c>tests/CombatImpact.Corpus/evidence/README.md</c> owns the
/// procedure.
///
/// The whole evidence document must match except the hashes and versions of the
/// <c>game_types</c> and <c>json_runtime</c> verification inputs: both come from the installed
/// game's Managed directory and change with every game update. A difference there is printed,
/// and <c>fullReportSha256</c> decides.
/// </summary>
public sealed partial class ScenarioRunnerTests(ITestOutputHelper output)
{
    private const string CorpusRunner = "CombatImpact.Corpus";
    private const string CorpusDirectory = "tests/CombatImpact.Corpus/corpus";
    private const string PinnedEvidence = "tests/CombatImpact.Corpus/evidence/pinned-corpus.json";
    private const string ActualEvidence = "artifacts/combat-impact-corpus/evidence.json";
    private const string ActualReport = "artifacts/combat-impact-corpus/report.json";
    private static readonly string[] GameManagedRoles = ["game_types", "json_runtime"];

    [Fact]
    [Trait("TestKind", "ScenarioRunner")]
    public async Task Combat_impact_corpus_matches_committed_evidence()
    {
        var root = TestInputs.RepoRoot;
        var evidencePath = Path.Combine(root, ActualEvidence);
        if (File.Exists(evidencePath))
            File.Delete(evidencePath);

        await RunAsync(
            CorpusRunner,
            [Path.Combine(root, CorpusDirectory), Path.Combine(root, ActualReport)],
            new Dictionary<string, string>
            {
                ["BPP_GAMEDATA_DB"] = Path.Combine(root, CorpusDirectory, "GameData.cards.db"),
                ["BPP_COMBAT_IMPACT_EVIDENCE_PATH"] = evidencePath,
            }
        );

        var actual = TestInputs.Fixture(ActualEvidence).Replace("\r\n", "\n");
        if (Environment.GetEnvironmentVariable("BPP_UPDATE_GOLDENS") == "1")
        {
            File.WriteAllText(Path.Combine(root, PinnedEvidence), actual, new UTF8Encoding(false));
            output.WriteLine($"Rewrote {PinnedEvidence}; review it with git diff.");
            return;
        }

        Assert.True(
            File.Exists(Path.Combine(root, PinnedEvidence)),
            $"{PinnedEvidence} is missing; create it with BPP_UPDATE_GOLDENS=1."
        );
        var expected = TestInputs.Fixture(PinnedEvidence).Replace("\r\n", "\n");
        var differences = new List<string>();
        var gameDrift = new List<string>();
        Compare(JsonNode.Parse(expected), JsonNode.Parse(actual), "$", differences, gameDrift);
        foreach (var line in gameDrift)
            output.WriteLine(line);

        if (differences.Count == 0)
            return;
        var message = new StringBuilder()
            .AppendLine(
                $"Combat Impact evidence differs from {PinnedEvidence} in {differences.Count} field(s):"
            )
            .AppendJoin('\n', differences.Take(40).Select(line => "  " + line))
            .AppendLine(differences.Count > 40 ? $"\n  ... {differences.Count - 40} more" : "")
            .AppendJoin('\n', gameDrift)
            .AppendLine()
            .AppendLine(UnifiedDiff.Render(expected, actual, PinnedEvidence))
            .AppendLine(
                $"Actual evidence: {ActualEvidence}. If the change is intended, regenerate with "
                    + "BPP_UPDATE_GOLDENS=1 dotnet test tests/ScenarioRunner.Tests/ScenarioRunner.Tests.csproj "
                    + "--filter Combat_impact_corpus_matches_committed_evidence, then review git diff."
            );
        Assert.Fail(message.ToString());
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
                                $"Game-managed input {role} {key} differs (fullReportSha256 decides): "
                                    + $"pinned={left[key]?.ToJsonString() ?? "null"} "
                                    + $"actual={right[key]?.ToJsonString() ?? "null"}"
                            );
                        }
                    }
                    left = Without(left, "sha256", "version");
                    right = Without(right, "sha256", "version");
                }
                foreach (
                    var key in left.Select(item => item.Key)
                        .Union(right.Select(item => item.Key))
                        .Order(StringComparer.Ordinal)
                )
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

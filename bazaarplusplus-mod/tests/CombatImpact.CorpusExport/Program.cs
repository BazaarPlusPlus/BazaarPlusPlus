#nullable enable
using System.Security.Cryptography;
using BazaarGameShared.Infra.Messages.CombatSimEvents;

// The host lane of the Combat Impact Corpus (ADR-0019).
//
// export <game-corpus-dir> <corpus.json.gz>: reads a directory of game replay payloads, run
// bundles or raw bundles plus the game catalog, and writes the BPP-owned Combat Impact Corpus
// that the Core-lane runner CombatImpact.Corpus projects.
// prepare <game-corpus-dir> <replay-store> [battle-id ...]: rebuilds the committed game corpus.
//
// The catalog is BPP_GAMEDATA_DB when set, else GameData.cards.db beside the game payloads,
// else the installed game's GameData.db.

const string gameDataPathEnvironmentVariable = "BPP_GAMEDATA_DB";
const string trimmedGameDataFileName = "GameData.cards.db";

switch (args)
{
    case ["prepare", .. var prepareArguments]:
        CorpusPreparation.Run(prepareArguments, ResolveGameDataPath(null));
        return;
    case ["export", var sourcePath, var outputPath]:
        Export(Path.GetFullPath(sourcePath), Path.GetFullPath(outputPath));
        return;
    default:
        throw new ArgumentException(
            "Usage: CombatImpact.CorpusExport export <game-corpus-dir> <corpus.json.gz> | "
                + "prepare <game-corpus-dir> <replay-store> [battle-id ...]"
        );
}

static void Export(string sourcePath, string outputPath)
{
    if (!Directory.Exists(sourcePath))
        throw new DirectoryNotFoundException($"Game replay corpus does not exist: {sourcePath}");

    var gameDataPath = ResolveGameDataPath(sourcePath);
    // The catalog loads first: BazaarJsonSerializerSettings scans every loaded assembly, and a
    // host assembly loaded before it fails that scan on its UnityEngine references.
    var catalog = CombatImpactCorpusCatalog.Load(gameDataPath);
    var source = GameReplayCorpus.Load(sourcePath);
    var document = new CombatImpactCorpusDocument
    {
        SourceKind = source.SourceKind,
        SourceCount = source.SourceCount,
        SourceArtifacts = source.SourceArtifacts.ToList(),
        InvalidPayloads = source.InvalidPayloads.ToList(),
        VerificationInputs =
        [
            DescribeVerificationInput("game_data", gameDataPath),
            DescribeVerificationInput(
                "game_types",
                typeof(CombatSim).Assembly.Location,
                typeof(CombatSim).Assembly.GetName().Version?.ToString()
            ),
            DescribeVerificationInput(
                "json_runtime",
                typeof(Newtonsoft.Json.JsonConvert).Assembly.Location,
                typeof(Newtonsoft.Json.JsonConvert).Assembly.GetName().Version?.ToString()
            ),
        ],
        Battles = source
            .Replays.OrderBy(replay => replay.BattleId, StringComparer.Ordinal)
            .Select(replay => CombatImpactCorpusExport.ToBattle(replay, catalog))
            .ToList(),
    };

    var forbiddenAssemblies = AppDomain
        .CurrentDomain.GetAssemblies()
        .Select(assembly => assembly.GetName().Name ?? string.Empty)
        .Where(name =>
            name.Equals("Assembly-CSharp", StringComparison.Ordinal)
            || name.StartsWith("UnityEngine", StringComparison.Ordinal)
        )
        .Order(StringComparer.Ordinal)
        .ToArray();
    if (forbiddenAssemblies.Length > 0)
    {
        throw new InvalidOperationException(
            $"Pure replay host loaded forbidden assemblies: {string.Join(",", forbiddenAssemblies)}"
        );
    }

    CombatImpactCorpusCodec.Write(outputPath, document);
    Console.WriteLine(
        $"EXPORT corpus={outputPath} source={document.SourceKind} "
            + $"source_count={document.SourceCount} battles={document.Battles.Count} "
            + $"invalid={document.InvalidPayloads.Count} game_data={gameDataPath}"
    );
}

static VerificationInputArtifact DescribeVerificationInput(
    string role,
    string path,
    string? version = null
)
{
    using var stream = File.OpenRead(path);
    return new VerificationInputArtifact(
        role,
        Path.GetFileName(path),
        Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(),
        version
    );
}

static string ResolveGameDataPath(string? sourcePath)
{
    var configured = Environment.GetEnvironmentVariable(gameDataPathEnvironmentVariable);
    if (!string.IsNullOrWhiteSpace(configured))
        return Path.GetFullPath(configured);
    if (sourcePath != null)
    {
        var trimmed = Path.Combine(sourcePath, trimmedGameDataFileName);
        if (File.Exists(trimmed))
            return trimmed;
    }

    var candidates = new[]
    {
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library",
            "Application Support",
            "com.TempoStorm.TheBazaar",
            "prod",
            "cache",
            "GameData.db"
        ),
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TempoStorm",
            "TheBazaar",
            "prod",
            "cache",
            "GameData.db"
        ),
    };
    var resolved = candidates.FirstOrDefault(File.Exists);
    return resolved
        ?? throw new FileNotFoundException(
            $"GameData.db was not found; set {gameDataPathEnvironmentVariable}."
        );
}

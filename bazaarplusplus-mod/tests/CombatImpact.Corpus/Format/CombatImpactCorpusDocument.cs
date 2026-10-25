#nullable enable
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.Combat;
using BazaarPlusPlus.Game.PostCombatImpact.Data;

// The Combat Impact Corpus in BPP's own vocabulary (ADR-0019): what the Core-lane runner
// (CombatImpact.Corpus) projects, written by the host-lane exporter (CombatImpact.CorpusExport)
// from game replay payloads and the game catalog. Both projects compile this file; neither reads
// the other's assembly, because the exporter reaches Core's netstandard2.1 build through the
// desktop host and the runner Core's net10.0 build.

/// <summary>A whole corpus: where it came from and every battle in it, ordered by battle id.</summary>
internal sealed class CombatImpactCorpusDocument
{
    public int FormatVersion { get; set; } = CombatImpactCorpusCodec.FormatVersion;

    public string SourceKind { get; set; } = string.Empty;

    public int SourceCount { get; set; }

    public List<CorpusSourceArtifact> SourceArtifacts { get; set; } = [];

    /// <summary>Source payloads the exporter could not decode, as <c>battle:reason</c>.</summary>
    public List<string> InvalidPayloads { get; set; } = [];

    /// <summary>
    /// The game inputs the exporter interpreted the payloads with (the catalog, the game's types,
    /// its JSON runtime). The runner has none of them and copies these into its evidence.
    /// </summary>
    public List<VerificationInputArtifact> VerificationInputs { get; set; } = [];

    public List<CombatImpactCorpusBattle> Battles { get; set; } = [];
}

/// <summary>
/// One battle: its combat timeline, the spawn facts the inventory reads, the identities of the
/// cards transforms create, and the entities the exporter built from the catalog.
/// </summary>
internal sealed class CombatImpactCorpusBattle
{
    public string BattleId { get; set; } = string.Empty;

    public CombatImpactCorpusSpawn Spawn { get; set; } = new();

    public CombatTimeline Timeline { get; set; } = new();

    /// <summary>
    /// Core's transform events carry instance ids only; the inventory also reads each card's
    /// template, type and side. One entry per frame that has transform events.
    /// </summary>
    public List<CombatImpactCorpusTransformFrame> TransformIdentities { get; set; } = [];

    /// <summary>
    /// The player updates the game flagged as the player being dead, which Core's timeline leaves
    /// out because attribution reads the death events instead.
    /// </summary>
    public List<CombatImpactCorpusPlayerFrame> DeadPlayerUpdates { get; set; } = [];

    /// <summary>The producer's entities by id, in the order the exporter built them.</summary>
    public Dictionary<string, CombatImpactEntity> Entities { get; set; } = [];

    /// <summary>How many PlayerEffect entities the implicit-effect reader added.</summary>
    public int ImplicitPlayerEffects { get; set; }

    public bool IsPlayerDead(int frameIndex, Combatant combatant) =>
        DeadPlayerUpdates.Contains(new CombatImpactCorpusPlayerFrame(frameIndex, combatant));
}

/// <summary>One side's player update in one frame.</summary>
internal sealed record CombatImpactCorpusPlayerFrame(int FrameIndex, Combatant Combatant);

/// <summary>The spawn state the game sent before combat, as far as the inventory reads it.</summary>
internal sealed class CombatImpactCorpusSpawn
{
    public Dictionary<CombatPlayerAttribute, int> PlayerAttributes { get; set; } = [];

    public Dictionary<CombatPlayerAttribute, int> OpponentAttributes { get; set; } = [];

    /// <summary>The spawn state's cards by instance id, in the game's order.</summary>
    public Dictionary<string, CombatImpactCorpusSpawnCard> Cards { get; set; } = [];

    /// <summary>The spawn's card-spawned events, in order.</summary>
    public List<CombatImpactCorpusCardIdentity> SpawnedCards { get; set; } = [];

    /// <summary>The spawn's effect-executed events (the opening actions), in order.</summary>
    public List<CombatEffectExecuted> Effects { get; set; } = [];

    public Dictionary<CombatPlayerAttribute, int> Attributes(Combatant combatant) =>
        combatant == Combatant.Player ? PlayerAttributes : OpponentAttributes;
}

internal sealed class CombatImpactCorpusSpawnCard
{
    /// <summary>The side the card's placement names; null when it has none.</summary>
    public Combatant? Owner { get; set; }

    public Dictionary<CombatCardAttribute, int> Attributes { get; set; } = [];
}

/// <summary>A card's identity as a game event states it.</summary>
internal sealed record CombatImpactCorpusCardIdentity(
    string InstanceId,
    string? TemplateId,
    CardType? Type,
    Combatant? Combatant
);

/// <summary>
/// The cards one frame's transforms create (in event and card order) and the originals its
/// reverts restore.
/// </summary>
internal sealed class CombatImpactCorpusTransformFrame
{
    public int FrameIndex { get; set; }

    public List<CombatImpactCorpusCardIdentity> Transformed { get; set; } = [];

    public List<CombatImpactCorpusCardIdentity> Reverted { get; set; } = [];
}

internal sealed record CorpusSourceArtifact(string Name, string? Sha256);

internal sealed record VerificationInputArtifact(
    string Role,
    string Name,
    string Sha256,
    string? Version
);

/// <summary>
/// Reads and writes the corpus as gzip-compressed JSON. Every enum is written by member name,
/// values and dictionary keys alike, because a facet's integer is BPP's own and never persisted
/// (ADR-0015, ADR-0019). Object and dictionary order is the order the exporter observed, so the
/// runner iterates every collection as the game did. The JSON text is deterministic; the gzip
/// bytes around it are not promised to be, so goldens compare the text.
/// </summary>
internal static class CombatImpactCorpusCodec
{
    public const int FormatVersion = 1;

    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static string Serialize(CombatImpactCorpusDocument document) =>
        JsonSerializer.Serialize(document, Options) + "\n";

    public static CombatImpactCorpusDocument Deserialize(string json)
    {
        var document =
            JsonSerializer.Deserialize<CombatImpactCorpusDocument>(json, Options)
            ?? throw new InvalidDataException("The Combat Impact Corpus is empty.");
        if (document.FormatVersion != FormatVersion)
        {
            throw new InvalidDataException(
                $"Combat Impact Corpus format {document.FormatVersion} is not {FormatVersion}."
            );
        }
        return document;
    }

    public static void Write(string path, CombatImpactCorpusDocument document)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var file = File.Create(path);
        using var gzip = new GZipStream(file, CompressionLevel.SmallestSize);
        var bytes = new UTF8Encoding(false).GetBytes(Serialize(document));
        gzip.Write(bytes, 0, bytes.Length);
    }

    /// <summary>The decompressed JSON text of a corpus file.</summary>
    public static string ReadText(string path)
    {
        using var gzip = new GZipStream(File.OpenRead(path), CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, new UTF8Encoding(false));
        return reader.ReadToEnd();
    }

    public static CombatImpactCorpusDocument Read(string path) => Deserialize(ReadText(path));

    private static JsonSerializerOptions CreateOptions()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(AddPolymorphism);
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = null,
            WriteIndented = true,
            NewLine = "\n",
            IgnoreReadOnlyProperties = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            TypeInfoResolver = resolver,
        };
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        options.MakeReadOnly();
        return options;
    }

    // Core's abstract combat types carry no serializer attributes (Core is netstandard2.1 and
    // serializer-free), so the discriminators are named here. A derived type missing from this
    // list fails serialization instead of losing its members.
    private static void AddPolymorphism(JsonTypeInfo info)
    {
        var derived =
            info.Type == typeof(CombatEvent)
                ? new (Type, string)[]
                {
                    (typeof(CombatEffectExecuted), "EffectExecuted"),
                    (typeof(CombatAuraExecuted), "AuraExecuted"),
                    (typeof(CombatantDied), "CombatantDied"),
                    (typeof(CombatCardEnchanted), "CardEnchanted"),
                    (typeof(CombatCardTransformed), "CardTransformed"),
                    (typeof(CombatCardTransformReverted), "CardTransformReverted"),
                    (typeof(CombatCardActionCostSpent), "CardActionCostSpent"),
                }
            : info.Type == typeof(CombatTarget)
                ? new (Type, string)[]
                {
                    (typeof(CombatCardTarget), "Card"),
                    (typeof(CombatPlayerTarget), "Player"),
                }
            : info.Type == typeof(CombatValue)
                ? new (Type, string)[]
                {
                    (typeof(CombatFixedValue), "Fixed"),
                    (typeof(CombatRangeValue), "Range"),
                    (typeof(CombatCardAttributeReference), "CardAttributeReference"),
                    (typeof(CombatOpaqueReference), "OpaqueReference"),
                    (typeof(CombatUnsupportedValue), "Unsupported"),
                }
            : null;
        if (derived == null)
            return;
        var polymorphism = new JsonPolymorphismOptions { TypeDiscriminatorPropertyName = "$type" };
        foreach (var (type, discriminator) in derived)
            polymorphism.DerivedTypes.Add(new JsonDerivedType(type, discriminator));
        info.PolymorphismOptions = polymorphism;
    }
}

#nullable enable
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.Combat;
using BazaarPlusPlus.Game.PostCombatImpact.Data;

// The runner and exporter compile this format separately because they bind different Core targets.

internal sealed class CombatImpactCorpusDocument
{
    public int FormatVersion { get; set; } = CombatImpactCorpusCodec.FormatVersion;

    public string SourceKind { get; set; } = string.Empty;

    public int SourceCount { get; set; }

    public List<CorpusSourceArtifact> SourceArtifacts { get; set; } = [];

    public List<string> InvalidPayloads { get; set; } = [];

    public List<VerificationInputArtifact> VerificationInputs { get; set; } = [];

    public List<CombatImpactCorpusBattle> Battles { get; set; } = [];
}

internal sealed class CombatImpactCorpusBattle
{
    public string BattleId { get; set; } = string.Empty;

    public CombatImpactCorpusSpawn Spawn { get; set; } = new();

    public CombatTimeline Timeline { get; set; } = new();

    // Transform events carry only instance IDs. The inventory also needs each card's template, type, and side.
    public List<CombatImpactCorpusTransformFrame> TransformIdentities { get; set; } = [];

    // The timeline omits these updates because attribution reads death events.
    public List<CombatImpactCorpusPlayerFrame> DeadPlayerUpdates { get; set; } = [];

    public Dictionary<string, CombatImpactEntity> Entities { get; set; } = [];

    public int ImplicitPlayerEffects { get; set; }

    public bool IsPlayerDead(int frameIndex, Combatant combatant) =>
        DeadPlayerUpdates.Contains(new CombatImpactCorpusPlayerFrame(frameIndex, combatant));
}

internal sealed record CombatImpactCorpusPlayerFrame(int FrameIndex, Combatant Combatant);

internal sealed class CombatImpactCorpusSpawn
{
    public Dictionary<CombatPlayerAttribute, int> PlayerAttributes { get; set; } = [];

    public Dictionary<CombatPlayerAttribute, int> OpponentAttributes { get; set; } = [];

    public Dictionary<string, CombatImpactCorpusSpawnCard> Cards { get; set; } = [];

    public List<CombatImpactCorpusCardIdentity> SpawnedCards { get; set; } = [];

    public List<CombatEffectExecuted> Effects { get; set; } = [];

    public Dictionary<CombatPlayerAttribute, int> Attributes(Combatant combatant) =>
        combatant == Combatant.Player ? PlayerAttributes : OpponentAttributes;
}

internal sealed class CombatImpactCorpusSpawnCard
{
    public Combatant? Owner { get; set; }

    public Dictionary<CombatCardAttribute, int> Attributes { get; set; } = [];
}

internal sealed record CombatImpactCorpusCardIdentity(
    string InstanceId,
    string? TemplateId,
    CardType? Type,
    Combatant? Combatant
);

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

// Persist facet names because their numbers are local to BPP. Preserve game iteration order.
// Compare JSON in goldens because the gzip bytes can vary.
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

    // Core is serializer-free. Register derived types here so an unlisted type fails serialization.
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

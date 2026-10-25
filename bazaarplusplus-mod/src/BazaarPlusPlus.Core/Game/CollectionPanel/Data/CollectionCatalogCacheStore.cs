#nullable enable
using System.Globalization;
using System.Text;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Infrastructure;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

/// <summary>
/// A built Collection catalog on disk, one file per language, so a launch whose
/// <see cref="CollectionCatalogCacheIdentity"/> matches skips the build. Facets are stored by name
/// (ADR-0015). A miss names its reason and never throws; the caller builds instead. Safe to call
/// from several threads.
/// </summary>
public sealed class CollectionCatalogCacheStore
{
    public const int SchemaVersion = 1;
    public const int MaxCacheFileBytes = 32 * 1024 * 1024;

    private static readonly UTF8Encoding Utf8NoBom = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true
    );
    private static readonly JsonSerializerSettings SerializerSettings = new()
    {
        TypeNameHandling = TypeNameHandling.None,
        MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
        MissingMemberHandling = MissingMemberHandling.Ignore,
        Formatting = Formatting.None,
        Culture = CultureInfo.InvariantCulture,
        MaxDepth = 32,
        Converters = { new StringEnumConverter() },
    };

    private readonly object _syncRoot = new();

    public CollectionCatalogCacheStore(string directory)
    {
        DirectoryPath = !string.IsNullOrWhiteSpace(directory)
            ? directory
            : throw new ArgumentException("Cache directory is required.", nameof(directory));
    }

    public string DirectoryPath { get; }

    public string PathFor(string language) =>
        Path.Combine(DirectoryPath, $"catalog.{FileSafe(language)}.json");

    public void Save(CollectionCatalogCacheIdentity identity, IReadOnlyList<CollectionCardVm> cards)
    {
        if (identity == null)
            throw new ArgumentNullException(nameof(identity));
        if (cards == null)
            throw new ArgumentNullException(nameof(cards));

        var document = new DocumentWire
        {
            SchemaVersion = SchemaVersion,
            Identity = IdentityWire.From(identity),
            Cards = cards.Select(CardWire.From).ToList(),
        };
        var bytes = Utf8NoBom.GetBytes(JsonConvert.SerializeObject(document, SerializerSettings));
        if (bytes.Length > MaxCacheFileBytes)
            throw new InvalidOperationException(
                $"Collection catalog cache is too large to persist ({bytes.Length} bytes)."
            );

        var path = PathFor(identity.Language);
        lock (_syncRoot)
        {
            DeleteOrphanedTempFiles(path);
            AtomicFileWriter.Write(path, bytes);
        }
    }

    public bool TryLoad(
        CollectionCatalogCacheIdentity identity,
        out IReadOnlyList<CollectionCardVm>? cards,
        out string missReason
    )
    {
        if (identity == null)
            throw new ArgumentNullException(nameof(identity));

        cards = null;
        var path = PathFor(identity.Language);
        string json;
        lock (_syncRoot)
        {
            if (!File.Exists(path))
            {
                missReason = "not-found";
                return false;
            }
            try
            {
                if (new FileInfo(path).Length > MaxCacheFileBytes)
                {
                    missReason = "file-too-large";
                    return false;
                }
                json = File.ReadAllText(path, Utf8NoBom);
            }
            catch (Exception ex)
                when (ex is IOException
                    || ex is UnauthorizedAccessException
                    || ex is DecoderFallbackException
                )
            {
                missReason = "read-failed";
                return false;
            }
        }

        DocumentWire? document;
        try
        {
            document = JsonConvert.DeserializeObject<DocumentWire>(json, SerializerSettings);
        }
        catch (Exception ex)
            when (ex is JsonException
                || ex is ArgumentException
                || ex is FormatException
                || ex is OverflowException
            )
        {
            missReason = "invalid-json";
            return false;
        }

        if (document == null)
        {
            missReason = "invalid-json";
            return false;
        }
        if (document.SchemaVersion != SchemaVersion)
        {
            missReason = "schema-mismatch";
            return false;
        }
        if (document.Identity == null || document.Cards == null)
        {
            missReason = "invalid-json";
            return false;
        }
        if (!identity.Equals(document.Identity.ToIdentity()))
        {
            missReason = "identity-mismatch";
            return false;
        }

        try
        {
            cards = document.Cards.Select(card => card.ToVm()).ToArray();
        }
        catch (ArgumentException)
        {
            // A card listing one enchantment twice.
            missReason = "invalid-json";
            return false;
        }
        missReason = string.Empty;
        return true;
    }

    private static string FileSafe(string language)
    {
        var builder = new StringBuilder(language?.Length ?? 0);
        foreach (var character in language ?? string.Empty)
            builder.Append(char.IsLetterOrDigit(character) || character == '-' ? character : '_');
        return builder.Length > 0 ? builder.ToString() : "default";
    }

    // A crash between the temp write and the rename leaves the temp file behind.
    private static void DeleteOrphanedTempFiles(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return;
        try
        {
            foreach (
                var temp in Directory.EnumerateFiles(directory, Path.GetFileName(path) + ".*.tmp")
            )
                File.Delete(temp);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            // Best effort: a stale temp file never makes the cache unreadable.
        }
    }

    private sealed class DocumentWire
    {
        public int SchemaVersion { get; set; }
        public IdentityWire? Identity { get; set; }
        public List<CardWire>? Cards { get; set; }
    }

    private sealed class IdentityWire
    {
        public string? GameDataEtag { get; set; }
        public string? TranslationEtag { get; set; }
        public string? Language { get; set; }
        public string? GameVersion { get; set; }
        public string? ModBuild { get; set; }

        public static IdentityWire From(CollectionCatalogCacheIdentity identity) =>
            new()
            {
                GameDataEtag = identity.GameDataEtag,
                TranslationEtag = identity.TranslationEtag,
                Language = identity.Language,
                GameVersion = identity.GameVersion,
                ModBuild = identity.ModBuild,
            };

        public CollectionCatalogCacheIdentity ToIdentity() =>
            new(
                GameDataEtag ?? string.Empty,
                TranslationEtag ?? string.Empty,
                Language ?? string.Empty,
                GameVersion ?? string.Empty,
                ModBuild ?? string.Empty
            );
    }

    private sealed class CardWire
    {
        public Guid Id { get; set; }
        public CardType Type { get; set; }
        public CardSize Size { get; set; }
        public CardTier StartingTier { get; set; }
        public List<CardHero>? Heroes { get; set; }
        public List<CardTag>? Tags { get; set; }
        public List<CardHiddenTag>? HiddenTags { get; set; }
        public CollectionMechanic Mechanics { get; set; }
        public string? DisplayName { get; set; }
        public string? Description { get; set; }
        public string? InternalName { get; set; }
        public string? ArtKey { get; set; }
        public string? SearchText { get; set; }
        public bool IsEnchantable { get; set; }
        public List<EnchantmentWire>? Enchantments { get; set; }
        public bool IsPackage { get; set; }

        public static CardWire From(CollectionCardVm card) =>
            new()
            {
                Id = card.Id,
                Type = card.Type,
                Size = card.Size,
                StartingTier = card.StartingTier,
                Heroes = card.Heroes.ToList(),
                Tags = card.Tags.ToList(),
                HiddenTags = card.HiddenTags.ToList(),
                Mechanics = card.Mechanics,
                DisplayName = card.DisplayName,
                Description = card.Description,
                InternalName = card.InternalName,
                ArtKey = card.ArtKey,
                SearchText = card.SearchText,
                IsEnchantable = card.IsEnchantable,
                Enchantments = card
                    .Enchantments.Values.Select(enchantment => new EnchantmentWire
                    {
                        Type = enchantment.Type,
                        Tags = enchantment.Tags.ToList(),
                        HiddenTags = enchantment.HiddenTags.ToList(),
                    })
                    .ToList(),
                IsPackage = card.IsPackage,
            };

        // The projection hands out sets; a hit hands back the same shapes.
        public CollectionCardVm ToVm() =>
            new()
            {
                Id = Id,
                Type = Type,
                Size = Size,
                StartingTier = StartingTier,
                Heroes = new HashSet<CardHero>(Heroes ?? []),
                Tags = new HashSet<CardTag>(Tags ?? []),
                HiddenTags = new HashSet<CardHiddenTag>(HiddenTags ?? []),
                Mechanics = Mechanics,
                DisplayName = DisplayName ?? string.Empty,
                Description = Description ?? string.Empty,
                InternalName = InternalName ?? string.Empty,
                ArtKey = ArtKey ?? string.Empty,
                SearchText = SearchText ?? string.Empty,
                IsEnchantable = IsEnchantable,
                Enchantments = (Enchantments ?? []).ToDictionary(
                    enchantment => enchantment.Type,
                    enchantment => new CollectionCardEnchantmentFacets(
                        enchantment.Type,
                        new HashSet<CardTag>(enchantment.Tags ?? []),
                        new HashSet<CardHiddenTag>(enchantment.HiddenTags ?? [])
                    )
                ),
                IsPackage = IsPackage,
            };
    }

    private sealed class EnchantmentWire
    {
        public CardEnchantment Type { get; set; }
        public List<CardTag>? Tags { get; set; }
        public List<CardHiddenTag>? HiddenTags { get; set; }
    }
}

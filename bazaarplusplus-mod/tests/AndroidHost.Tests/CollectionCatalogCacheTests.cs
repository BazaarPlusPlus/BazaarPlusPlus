using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.TestSupport;
using Xunit;

namespace AndroidHost.Tests;

/// <summary>
/// The Android Collection catalog's on-disk cache (<see cref="CollectionCatalogCacheStore"/>),
/// which lets a launch skip the per-card build. Its failure modes, each pinned below:
/// <list type="number">
/// <item>Stale cards: a changed game data or translation ETag, language, game version or mod
/// build must miss, never serve the old catalog.</item>
/// <item>A damaged or foreign file (truncated, not JSON, another schema version) must miss
/// without throwing, so the catalog falls back to a build.</item>
/// <item>Lost fields: a hit must hand back every view-model field the filters, search and grid
/// read, enchantment facets and mechanic flags included.</item>
/// <item>Facet integers on disk: facets are stored by name (ADR-0015).</item>
/// <item>Languages overwriting each other: each language keeps its own file.</item>
/// <item>No game data ETag: the identity does not resolve, so nothing is cached.</item>
/// </list>
/// </summary>
public sealed class CollectionCatalogCacheTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"bpp-collection-catalog-{Guid.NewGuid():N}"
    );

    [Fact]
    public void A_hit_returns_every_field_and_stores_facets_by_name()
    {
        var store = new CollectionCatalogCacheStore(_directory);
        var identity = Identity();
        var expected = Card();

        store.Save(identity, [expected]);

        Assert.True(store.TryLoad(identity, out var cards, out var missReason), missReason);
        var actual = Assert.Single(cards!);
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.Type, actual.Type);
        Assert.Equal(expected.Size, actual.Size);
        Assert.Equal(expected.StartingTier, actual.StartingTier);
        Assert.Equal(expected.Heroes, actual.Heroes);
        Assert.Equal(expected.Tags, actual.Tags);
        Assert.Equal(expected.HiddenTags, actual.HiddenTags);
        Assert.Equal(expected.Mechanics, actual.Mechanics);
        Assert.Equal(expected.DisplayName, actual.DisplayName);
        Assert.Equal(expected.Description, actual.Description);
        Assert.Equal(expected.InternalName, actual.InternalName);
        Assert.Equal(expected.ArtKey, actual.ArtKey);
        Assert.Equal(expected.SearchText, actual.SearchText);
        Assert.Equal(expected.IsEnchantable, actual.IsEnchantable);
        Assert.Equal(expected.IsPackage, actual.IsPackage);
        var enchantment = Assert.Single(actual.Enchantments);
        Assert.Equal(CardEnchantment.Fiery, enchantment.Key);
        Assert.Equal(CardEnchantment.Fiery, enchantment.Value.Type);
        Assert.Equal([CardTag.Weapon], enchantment.Value.Tags);
        Assert.Equal([CardHiddenTag.Burn], enchantment.Value.HiddenTags);

        var text = TestInputs.Scratch(store.PathFor(identity.Language));
        Assert.Contains("\"Skill\"", text);
        Assert.Contains("\"Multicast, Destroy\"", text);
        Assert.Contains("\"Fiery\"", text);
    }

    [Theory]
    [InlineData("game_data")]
    [InlineData("translation")]
    [InlineData("game_version")]
    [InlineData("mod_build")]
    public void A_changed_identity_misses(string field)
    {
        var store = new CollectionCatalogCacheStore(_directory);
        var saved = Identity();
        store.Save(saved, [Card()]);
        var changed = field switch
        {
            "game_data" => Identity(gameData: "\"etag-b\""),
            "translation" => Identity(translation: "\"tr-b\""),
            "game_version" => Identity(gameVersion: "1.0.2"),
            _ => Identity(modBuild: "build-b"),
        };

        Assert.False(store.TryLoad(changed, out var cards, out var missReason));
        Assert.Null(cards);
        Assert.Equal("identity-mismatch", missReason);
    }

    [Fact]
    public void Each_language_keeps_its_own_file()
    {
        var store = new CollectionCatalogCacheStore(_directory);
        var english = Identity(language: "en-US");
        var chinese = Identity(language: "zh-CN");
        store.Save(english, [Card(name: "Eagle Talisman")]);
        store.Save(chinese, [Card(name: "鹰之护符")]);

        Assert.True(store.TryLoad(english, out var englishCards, out _));
        Assert.True(store.TryLoad(chinese, out var chineseCards, out _));
        Assert.Equal("Eagle Talisman", Assert.Single(englishCards!).DisplayName);
        Assert.Equal("鹰之护符", Assert.Single(chineseCards!).DisplayName);
    }

    [Theory]
    [InlineData("{\"SchemaVersion\":1,\"Identity\":", "invalid-json")]
    [InlineData("not json", "invalid-json")]
    [InlineData("{\"SchemaVersion\":999,\"Identity\":null,\"Cards\":[]}", "schema-mismatch")]
    public void A_damaged_or_foreign_file_misses_without_throwing(string content, string reason)
    {
        var store = new CollectionCatalogCacheStore(_directory);
        var identity = Identity();
        Directory.CreateDirectory(_directory);
        File.WriteAllText(store.PathFor(identity.Language), content);

        Assert.False(store.TryLoad(identity, out var cards, out var missReason));
        Assert.Null(cards);
        Assert.Equal(reason, missReason);
    }

    [Fact]
    public void A_missing_file_misses()
    {
        var store = new CollectionCatalogCacheStore(_directory);

        Assert.False(store.TryLoad(Identity(), out _, out var missReason));
        Assert.Equal("not-found", missReason);
    }

    [Fact]
    public void Identity_needs_the_game_data_etag()
    {
        Directory.CreateDirectory(_directory);
        var gameData = Path.Combine(_directory, "manifest.json");
        var translations = Path.Combine(_directory, "translations.json");
        File.WriteAllText(gameData, "{\"Entries\":{\"maintenance\":{\"ETag\":\"\\\"m\\\"\"}}}");
        File.WriteAllText(translations, "{\"Entries\":{\"zh-CN\":{\"ETag\":\"\\\"t\\\"\"}}}");

        Assert.Null(
            CollectionCatalogCacheIdentity.Resolve(gameData, translations, "zh-CN", "1.0.1", "b")
        );

        File.WriteAllText(gameData, "{\"Entries\":{\"GameData\":{\"ETag\":\"\\\"g\\\"\"}}}");
        var resolved = CollectionCatalogCacheIdentity.Resolve(
            gameData,
            translations,
            "zh-CN",
            "1.0.1",
            "b"
        );
        Assert.NotNull(resolved);
        Assert.Equal("\"g\"", resolved.GameDataEtag);
        Assert.Equal("\"t\"", resolved.TranslationEtag);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private static CollectionCatalogCacheIdentity Identity(
        string gameData = "\"etag-a\"",
        string translation = "\"tr-a\"",
        string language = "zh-CN",
        string gameVersion = "1.0.1",
        string modBuild = "build-a"
    ) => new(gameData, translation, language, gameVersion, modBuild);

    private static CollectionCardVm Card(string name = "Eagle Talisman") =>
        new()
        {
            Id = Guid.Parse("001092a2-3703-4f7d-b686-ce812f6622ea"),
            Type = CardType.Skill,
            Size = CardSize.Medium,
            StartingTier = CardTier.Silver,
            Heroes = new HashSet<CardHero> { CardHero.Vanessa, CardHero.Pygmalien },
            Tags = new HashSet<CardTag> { CardTag.Weapon },
            HiddenTags = new HashSet<CardHiddenTag> { CardHiddenTag.Damage, CardHiddenTag.Heal },
            Mechanics = CollectionMechanic.Multicast | CollectionMechanic.Destroy,
            DisplayName = name,
            Description = "Deal 10 damage.",
            InternalName = "[Skill] Eagle Talisman",
            ArtKey = "Icon_Eagle.png",
            SearchText = "eagle talisman deal 10 damage",
            IsEnchantable = true,
            Enchantments = new Dictionary<CardEnchantment, CollectionCardEnchantmentFacets>
            {
                [CardEnchantment.Fiery] = new(
                    CardEnchantment.Fiery,
                    new HashSet<CardTag> { CardTag.Weapon },
                    new HashSet<CardHiddenTag> { CardHiddenTag.Burn }
                ),
            },
            IsPackage = true,
        };
}

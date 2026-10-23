#nullable enable
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.GameState;
using Xunit;

namespace AndroidHost.Tests;

/// <summary>
/// The Android card facet adapter maps the interop enums by member name (ADR-0015), so every
/// member the Interop Snapshot defines must name a card facet member or a documented alias.
/// Read from the interop assembly's metadata, never loaded: its types resolve only in the game.
/// </summary>
public sealed class AndroidCardFacetVocabularyTests
{
    private const string TypesNamespace = "Il2CppBazaarGameShared.Domain.Core.Types";

    public static TheoryData<string, Type, string[]> Vocabularies =>
        new()
        {
            { "EHero", typeof(CardHero), new[] { "Hero8" } },
            { "ETier", typeof(CardTier), Array.Empty<string>() },
            { "ECardSize", typeof(CardSize), Array.Empty<string>() },
            { "ECardType", typeof(CardType), Array.Empty<string>() },
            { "ECardTag", typeof(CardTag), Array.Empty<string>() },
            { "EHiddenTag", typeof(CardHiddenTag), Array.Empty<string>() },
            { "EEnchantmentType", typeof(CardEnchantment), Array.Empty<string>() },
            { "EInventorySection", typeof(CardSection), Array.Empty<string>() },
            { "EContainerSocketId", typeof(CardSocket), Array.Empty<string>() },
        };

    [Theory]
    [MemberData(nameof(Vocabularies))]
    public void Every_interop_enum_member_has_a_card_facet(
        string gameEnum,
        Type facet,
        string[] aliases
    )
    {
        var members = InteropEnumMembers(gameEnum);
        Assert.NotEmpty(members);
        var unmapped = members
            .Where(name => !Enum.IsDefined(facet, name) && !aliases.Contains(name))
            .ToArray();
        Assert.True(
            unmapped.Length == 0,
            $"{TypesNamespace}.{gameEnum} defines {string.Join(", ", unmapped)} with no {facet.Name} member."
        );
    }

    // The pedestal catalog names enchantments by member name too: the enchant preview compares them
    // with the interop EEnchantmentType member names, so a name the enum lacks would never match.
    [Fact]
    public void Every_pedestal_enchantment_is_an_interop_enum_member()
    {
        var members = InteropEnumMembers("EEnchantmentType");
        var unknown = PedestalEnchantCatalog
            .EnchantmentTypeNames.Where(name => !members.Contains(name))
            .ToArray();
        Assert.NotEmpty(PedestalEnchantCatalog.EnchantmentTypeNames);
        Assert.True(
            unknown.Length == 0,
            $"{TypesNamespace}.EEnchantmentType defines no {string.Join(", ", unknown)}."
        );
    }

    private static string[] InteropEnumMembers(string typeName)
    {
        using var stream = File.OpenRead(InteropGameSharedPath());
        using var reader = new PEReader(stream);
        var metadata = reader.GetMetadataReader();
        var type = metadata
            .TypeDefinitions.Select(handle => metadata.GetTypeDefinition(handle))
            .Single(definition =>
                metadata.GetString(definition.Namespace) == TypesNamespace
                && metadata.GetString(definition.Name) == typeName
            );
        return type.GetFields()
            .Select(handle => metadata.GetFieldDefinition(handle))
            .Where(field => (field.Attributes & FieldAttributes.Literal) != 0)
            .Select(field => metadata.GetString(field.Name))
            .ToArray();
    }

    private static string InteropGameSharedPath()
    {
        var directory = typeof(AndroidCardFacetVocabularyTests)
            .Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "BppInteropPath")
            .Value;
        Assert.False(string.IsNullOrEmpty(directory), "Run `just mod::fetch android online`.");
        return Path.Combine(directory!, "Il2CppBazaarGameShared.dll");
    }
}

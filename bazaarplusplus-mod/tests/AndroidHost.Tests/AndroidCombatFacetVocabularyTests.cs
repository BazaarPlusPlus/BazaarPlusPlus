#nullable enable
using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using BazaarPlusPlus.Core.Combat;
using Xunit;

namespace AndroidHost.Tests;

/// <summary>
/// Vocabulary pin (ADR-0019): Core's combat facets mirror the game's combat enums by member
/// name and value, so an Android adapter maps them by name as the desktop's
/// <c>DesktopCombatFacets</c> does. Every member the Interop Snapshot defines must be a Core
/// member with the same value, and the other way round. Read from the interop assembly's
/// metadata, never loaded: its types resolve only in the game.
/// </summary>
public sealed class AndroidCombatFacetVocabularyTests
{
    private const string TypesNamespace = "Il2CppBazaarGameShared.Domain.Core.Types";

    public static TheoryData<string, string, Type> Vocabularies =>
        new()
        {
            {
                "Il2CppBazaarGameShared.Domain.Effect",
                "EActionCommandType",
                typeof(CombatActionType)
            },
            { TypesNamespace, "ECardAttributeType", typeof(CombatCardAttribute) },
            { TypesNamespace, "EPlayerAttributeType", typeof(CombatPlayerAttribute) },
            { TypesNamespace, "ECombatantId", typeof(Combatant) },
            { TypesNamespace, "ECardStats", typeof(CombatCardStat) },
            { TypesNamespace, "EPlayerHealthChangeType", typeof(CombatHealthChange) },
            { TypesNamespace, "EAttributeModifierOperation", typeof(CombatAttributeOperation) },
            { TypesNamespace, "EDamageType", typeof(CombatDamageType) },
            { TypesNamespace, "EListComparisonOperator", typeof(CombatListComparison) },
            { TypesNamespace, "EEffectPriority", typeof(CombatEffectPriority) },
            {
                "Il2CppBazaarGameShared.Domain.Values",
                "EValueModifierMode",
                typeof(CombatValueModifierMode)
            },
        };

    [Theory]
    [MemberData(nameof(Vocabularies))]
    public void Every_interop_combat_enum_member_is_a_combat_facet_member(
        string typeNamespace,
        string gameEnum,
        Type facet
    )
    {
        var members = InteropEnumMembers(typeNamespace, gameEnum);
        Assert.NotEmpty(members);
        var facetMembers = Enum.GetNames(facet)
            .Select(name =>
                $"{name}={Convert.ToInt64(Enum.Parse(facet, name), CultureInfo.InvariantCulture)}"
            )
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(members, facetMembers);
    }

    private static string[] InteropEnumMembers(string typeNamespace, string typeName)
    {
        using var stream = File.OpenRead(InteropGameSharedPath());
        using var reader = new PEReader(stream);
        var metadata = reader.GetMetadataReader();
        var type = metadata
            .TypeDefinitions.Select(handle => metadata.GetTypeDefinition(handle))
            .Single(definition =>
                metadata.GetString(definition.Namespace) == typeNamespace
                && metadata.GetString(definition.Name) == typeName
            );
        return type.GetFields()
            .Select(handle => metadata.GetFieldDefinition(handle))
            .Where(field => (field.Attributes & FieldAttributes.Literal) != 0)
            .Select(field =>
            {
                var constant = metadata.GetConstant(field.GetDefaultValue());
                var value = metadata.GetBlobReader(constant.Value).ReadConstant(constant.TypeCode);
                return $"{metadata.GetString(field.Name)}={Convert.ToInt64(value, CultureInfo.InvariantCulture)}";
            })
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static string InteropGameSharedPath()
    {
        var directory = typeof(AndroidCombatFacetVocabularyTests)
            .Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "BppInteropPath")
            .Value;
        Assert.False(string.IsNullOrEmpty(directory), "Run `just mod::fetch android online`.");
        return Path.Combine(directory!, "Il2CppBazaarGameShared.dll");
    }
}

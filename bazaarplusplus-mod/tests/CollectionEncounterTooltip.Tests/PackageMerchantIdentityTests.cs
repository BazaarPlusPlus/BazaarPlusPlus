using BazaarGameShared.Domain.Cards.Item;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Effect;
using BazaarGameShared.Domain.Prerequisites;
using BazaarGameShared.Domain.Prerequisites.Conditionals;
using BazaarPlusPlus.GameInterop.Cards;
using Xunit;

namespace EncounterTooltip.Tests;

public sealed class PackageMerchantIdentityTests
{
    private static readonly Guid MerchantId = Guid.Parse("a4fa13f8-6beb-4b6c-839b-60af167628d9");

    [Fact]
    public void Resolves_repeated_current_encounter_reference_from_package_abilities()
    {
        var package = Package(Ability(MerchantId), Ability(MerchantId));

        Assert.True(
            PackageMerchantIdentity.TryResolveMerchantTemplateId(package, out var merchantId)
        );
        Assert.Equal(MerchantId, merchantId);
    }

    [Fact]
    public void Rejects_non_packages_and_negated_encounter_references()
    {
        var ordinaryItem = Package(Ability(MerchantId)) with
        {
            HiddenTags = new HashSet<EHiddenTag>(),
        };
        var negatedPackage = Package(Ability(MerchantId, isNot: true));

        Assert.False(
            PackageMerchantIdentity.TryResolveMerchantTemplateId(
                ordinaryItem,
                out var ordinaryMerchantId
            )
        );
        Assert.Equal(Guid.Empty, ordinaryMerchantId);
        Assert.False(
            PackageMerchantIdentity.TryResolveMerchantTemplateId(
                negatedPackage,
                out var negatedMerchantId
            )
        );
        Assert.Equal(Guid.Empty, negatedMerchantId);
    }

    [Fact]
    public void Rejects_ambiguous_merchant_references()
    {
        var package = Package(Ability(MerchantId), Ability(Guid.NewGuid()));

        Assert.False(
            PackageMerchantIdentity.TryResolveMerchantTemplateId(package, out var merchantId)
        );
        Assert.Equal(Guid.Empty, merchantId);
    }

    private static TCardItem Package(params TCardAbility[] abilities) =>
        new()
        {
            Type = ECardType.Item,
            HiddenTags = new HashSet<EHiddenTag> { EHiddenTag.Package },
            Abilities = abilities.ToDictionary(ability => ability.Id, StringComparer.Ordinal),
        };

    private static TCardAbility Ability(Guid merchantId, bool isNot = false) =>
        new()
        {
            Id = Guid.NewGuid().ToString(),
            Prerequisites =
            [
                new TPrerequisiteRun
                {
                    Conditions = new TRunConditionalCurrentEncounter
                    {
                        Conditions = new TCardConditionalId { Id = merchantId, IsNot = isNot },
                    },
                },
            ],
        };
}

#nullable enable
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Prerequisites;
using BazaarGameShared.Domain.Prerequisites.Conditionals;

namespace BazaarPlusPlus.GameInterop.Cards;

/// <summary>
/// Resolves the encounter template that accepts a package. Package abilities gate their sell
/// rewards on the current encounter's stable template ID; that runtime rule is authoritative.
/// </summary>
internal static class PackageMerchantIdentity
{
    internal static bool TryResolveMerchantTemplateId(
        ITCard? packageTemplate,
        out Guid merchantTemplateId
    )
    {
        merchantTemplateId = Guid.Empty;
        if (
            packageTemplate == null
            || !PackageIdentity.IsPackage(packageTemplate.HiddenTags)
            || packageTemplate.Abilities == null
        )
            return false;

        foreach (var ability in packageTemplate.Abilities.Values)
        {
            if (ability?.Prerequisites == null)
                continue;

            foreach (var prerequisite in ability.Prerequisites)
            {
                if (
                    prerequisite
                        is not TPrerequisiteRun
                        {
                            Conditions: TRunConditionalCurrentEncounter
                            {
                                Conditions: TCardConditionalId { IsNot: false, Id: var candidate },
                            },
                        }
                    || candidate == Guid.Empty
                )
                    continue;

                if (merchantTemplateId == Guid.Empty)
                {
                    merchantTemplateId = candidate;
                    continue;
                }

                if (merchantTemplateId != candidate)
                {
                    merchantTemplateId = Guid.Empty;
                    return false;
                }
            }
        }

        return merchantTemplateId != Guid.Empty;
    }
}

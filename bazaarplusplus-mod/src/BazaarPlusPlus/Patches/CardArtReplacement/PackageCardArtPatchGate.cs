#nullable enable
#pragma warning disable CS0436
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Cards;
using BazaarPlusPlus.Game.CardArtReplacement;
using BazaarPlusPlus.GameInterop.CardArtReplacement;
using UnityEngine;

namespace BazaarPlusPlus.Patches.CardArtReplacement;

internal static class PackageCardArtPatchGate
{
    public static bool TryGetReplacementTexture(Card? card, out Texture2D? texture)
    {
        texture = null;

        if (!PackageCardArtReplacementPolicy.IsEnabled(BppPatchHost.Services.Config))
            return false;

        if (card == null || !CardArtInjector.IsPackageCard(card))
            return false;

        var feature = CardArtReplacementFeature.Current;
        return feature != null
            && feature.TryGetTexture(card.TemplateId, out texture, out _)
            && texture != null;
    }

    public static bool TryGetReplacementPreviewMaterial(
        TCardBase? template,
        Material? baseMaterial,
        out Material? material
    )
    {
        material = null;

        if (!PackageCardArtReplacementPolicy.IsEnabled(BppPatchHost.Services.Config))
            return false;

        if (template == null || !CardArtInjector.IsPackageTemplate(template))
            return false;

        if (baseMaterial == null)
            return false;

        var feature = CardArtReplacementFeature.Current;
        return feature != null
            && feature.TryGetPreviewMaterial(template.Id, baseMaterial, out material, out _)
            && material != null;
    }
}

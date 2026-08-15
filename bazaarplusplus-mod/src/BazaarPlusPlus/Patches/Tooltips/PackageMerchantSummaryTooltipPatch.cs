#nullable enable
#pragma warning disable CS0436
using System.Text;
using BazaarGameShared.Domain.Cards;
using BazaarPlusPlus.Game.Tooltips;
using BazaarPlusPlus.GameInterop.Cards;
using BazaarPlusPlus.GameInterop.StaticCards;
using BazaarPlusPlus.Infrastructure;
using HarmonyLib;
using TheBazaar.Tooltips;

namespace BazaarPlusPlus.Patches.Tooltips;

// Package cards already identify their accepting merchant by stable encounter-template GUID.
// Append the native-compiled merchant/rule text while the game is still building its passive
// tooltip string. The native renderer, enchant-preview section host, and layout system then consume
// the complete block in their normal order.
[HarmonyPatch(typeof(CardTooltipData), nameof(CardTooltipData.GetPassiveTooltipBlock))]
internal static class PackageMerchantSummaryTooltipPatch
{
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Normal)]
    private static void Postfix(
        CardTooltipData __instance,
        ref ValueTuple<StringBuilder, TooltipSegment?> __result
    )
    {
        try
        {
            var packageTemplate = ResolvePackageTemplate(__instance);
            if (packageTemplate == null)
                return;

            if (
                !PackageMerchantSummary.TryResolve(
                    packageTemplate,
                    out var summary,
                    out var merchantTemplateId,
                    out var failureReason
                )
            )
            {
                ReportDegraded(failureReason, merchantTemplateId);
                return;
            }

            if (__result.Item1 == null || __result.Item1.Length == 0)
            {
                ReportDegraded(
                    PackageMerchantSummaryFailureReason.PassiveTextUnavailable,
                    merchantTemplateId
                );
                return;
            }

            PackageMerchantSummaryText.AppendToPassiveBlock(__result.Item1, summary);
        }
        catch (Exception ex)
        {
            ReportDegraded(PackageMerchantSummaryFailureReason.RenderException, Guid.Empty, ex);
        }
    }

    private static ITCard? ResolvePackageTemplate(CardTooltipData tooltipData)
    {
        if (PackageIdentity.IsPackage(tooltipData.CardTemplate?.HiddenTags))
            return tooltipData.CardTemplate;

        var staticData = BppStaticDataAccess.TryGetReadyManagerObject();
        var canonicalTemplate = BppStaticDataAccess.GetCardTemplate(
            staticData,
            tooltipData.CardInstance.TemplateId
        );
        return PackageIdentity.IsPackage(canonicalTemplate?.HiddenTags) ? canonicalTemplate : null;
    }

    private static void ReportDegraded(
        PackageMerchantSummaryFailureReason reasonCode,
        Guid merchantTemplateId,
        Exception? exception = null
    )
    {
        var fields = new[]
        {
            TooltipLogEvents.PackageMerchantSummaryReasonCode.Bind(reasonCode),
            TooltipLogEvents.PackageMerchantSummaryMerchantTemplateId.Bind(merchantTemplateId),
        };
        if (exception == null)
            BppLog.WarnEvent(TooltipLogEvents.PackageMerchantSummaryDegraded, fields);
        else
            BppLog.WarnEvent(TooltipLogEvents.PackageMerchantSummaryDegraded, exception, fields);
    }
}

#nullable enable
#pragma warning disable CS0436
using System.Text;
using BazaarGameShared.Domain.Cards;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.GameInterop.Cards;
using BazaarPlusPlus.GameInterop.StaticCards;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using HarmonyLib;
using TheBazaar.Tooltips;

namespace BazaarPlusPlus.Patches.Tooltips;

// Patch while the native passive text is being built so enchantment previews and layout
// both see the complete block.
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

    private static readonly StaticDataScopedCache<Guid, ITCard?> PackageTemplates = new();

    private static ITCard? ResolvePackageTemplate(CardTooltipData tooltipData)
    {
        var templateId = tooltipData.CardInstance.TemplateId;
        var staticData = BppStaticDataAccess.TryGetReadyManagerObject();
        if (staticData == null)
            return ResolvePackageTemplateUncached(tooltipData, null, templateId);

        return PackageTemplates.GetOrAdd(
            staticData,
            string.Empty,
            templateId,
            (tooltipData, staticData, templateId),
            static inputs =>
                ResolvePackageTemplateUncached(
                    inputs.tooltipData,
                    inputs.staticData,
                    inputs.templateId
                ),
            static _ => true
        );
    }

    private static ITCard? ResolvePackageTemplateUncached(
        CardTooltipData tooltipData,
        object? staticData,
        Guid templateId
    )
    {
        if (PackageIdentity.IsPackage(tooltipData.CardTemplate?.HiddenTags))
            return tooltipData.CardTemplate;

        var canonicalTemplate = BppStaticDataAccess.GetCardTemplate(staticData, templateId);
        return PackageIdentity.IsPackage(canonicalTemplate?.HiddenTags) ? canonicalTemplate : null;
    }

    private static void ReportDegraded(
        PackageMerchantSummaryFailureReason reasonCode,
        Guid merchantTemplateId,
        Exception? exception = null
    )
    {
        var fields = new BppLogField[]
        {
            ("reason_code", reasonCode),
            ("merchant_template_id", merchantTemplateId),
        };
        if (exception == null)
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.Tooltips,
                    "tooltips.package_merchant_summary.degraded",
                    storm: ["reason_code"]
                ),
                fields
            );
        else
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.Tooltips,
                    "tooltips.package_merchant_summary.degraded",
                    storm: ["reason_code"]
                ),
                exception,
                fields
            );
    }
}

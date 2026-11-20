#nullable enable
using BazaarGameShared.Domain.Cards;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.GameInterop.StaticCards;
using BazaarPlusPlus.Localization;
using TheBazaar;
using TheBazaar.Tooltips;

namespace BazaarPlusPlus.GameInterop.Cards;

internal enum PackageMerchantSummaryFailureReason
{
    None,
    MerchantReferenceUnavailable,
    StaticDataUnavailable,
    MerchantTemplateUnavailable,
    NativeTooltipUnavailable,
    EmptyText,
    PassiveTextUnavailable,
    RenderException,
}

internal static class PackageMerchantSummary
{
    // The passive block rerenders on every combat attribute event. Caching without Data.Run
    // is safe only while the first sentence has no run-dependent tokens (previews.md Gotchas).
    private static readonly StaticDataScopedCache<Guid, Resolution> Resolutions = new();

    internal static bool TryResolve(
        ITCard? packageTemplate,
        out string summary,
        out Guid merchantTemplateId,
        out PackageMerchantSummaryFailureReason failureReason
    )
    {
        var staticData = BppStaticDataAccess.TryGetReadyManagerObject();
        var resolution =
            staticData == null || packageTemplate == null
                ? ResolveUncached(packageTemplate, staticData)
                : Resolutions.GetOrAdd(
                    staticData,
                    L.CurrentLanguageCode,
                    packageTemplate.Id,
                    (packageTemplate, staticData),
                    static inputs => ResolveUncached(inputs.packageTemplate, inputs.staticData),
                    static resolved => IsCacheable(resolved.FailureReason)
                );

        summary = resolution.Summary;
        merchantTemplateId = resolution.MerchantTemplateId;
        failureReason = resolution.FailureReason;
        return failureReason == PackageMerchantSummaryFailureReason.None;
    }

    private static Resolution ResolveUncached(ITCard? packageTemplate, object? staticData)
    {
        if (
            !PackageMerchantIdentity.TryResolveMerchantTemplateId(
                packageTemplate,
                out var merchantTemplateId
            )
        )
            return new(PackageMerchantSummaryFailureReason.MerchantReferenceUnavailable);

        if (staticData == null)
            return new(
                PackageMerchantSummaryFailureReason.StaticDataUnavailable,
                merchantTemplateId
            );

        var merchantTemplate = BppStaticDataAccess.GetCardTemplate(staticData, merchantTemplateId);
        if (merchantTemplate == null)
            return new(
                PackageMerchantSummaryFailureReason.MerchantTemplateUnavailable,
                merchantTemplateId
            );

        var merchantCard = DTOUtils.CreateCard(
            merchantTemplateId.ToString(),
            merchantTemplate.Type
        );
        var merchantTooltip = CardTooltipData.CreateCardTooltipData(merchantCard);
        if (merchantTooltip == null)
            return new(
                PackageMerchantSummaryFailureReason.NativeTooltipUnavailable,
                merchantTemplateId
            );

        var (description, _) = merchantTooltip.GetCardDescription();
        var summary = PackageMerchantSummaryText.Format(merchantTooltip.GetTitle(), description);
        if (string.IsNullOrWhiteSpace(summary))
            return new(PackageMerchantSummaryFailureReason.EmptyText, merchantTemplateId);

        return new(PackageMerchantSummaryFailureReason.None, merchantTemplateId, summary);
    }

    // A native tooltip that failed to build or rendered empty may succeed on a later render.
    internal static bool IsCacheable(PackageMerchantSummaryFailureReason failureReason) =>
        failureReason
            is PackageMerchantSummaryFailureReason.None
                or PackageMerchantSummaryFailureReason.MerchantReferenceUnavailable
                or PackageMerchantSummaryFailureReason.MerchantTemplateUnavailable;

    private readonly record struct Resolution(
        PackageMerchantSummaryFailureReason FailureReason,
        Guid MerchantTemplateId = default,
        string Summary = ""
    );
}

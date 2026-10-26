#nullable enable
using System.Globalization;
using System.Reflection;
using System.Text;
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameClient.Domain.Tooltips;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Enchantments;
using BazaarGameShared.Domain.Core;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Values;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using TheBazaar;
using TheBazaar.Tooltips;

namespace BazaarPlusPlus.Game.ItemEnchantPreview.Preview;

public static class ItemEnchantPreviewRenderer
{
    private static readonly MethodInfo CardTooltipRenderMethod = typeof(CardTooltipData).GetMethod(
        "RenderTooltip",
        BindingFlags.Instance | BindingFlags.NonPublic,
        null,
        new[] { typeof(TooltipBuilder) },
        null
    );

    public static List<TooltipSegment> Render(ItemCard previewCard, TEnchantment enchantment)
    {
        var segments = new List<TooltipSegment>();
        if (
            enchantment.Localization?.Tooltips == null
            || enchantment.Localization.Tooltips.Count == 0
        )
            return segments;

        foreach (var tooltip in enchantment.Localization.Tooltips)
        {
            var content = tooltip?.Content;
            if (content == null)
                continue;

            var renderedText = RenderTooltipText(previewCard, content);
            if (string.IsNullOrWhiteSpace(renderedText))
                continue;

            segments.Add(
                ItemEnchantPreviewFormatting.CreateSegment(
                    previewCard.Enchantment ?? EEnchantmentType.Heavy,
                    renderedText
                )
            );
        }

        return segments;
    }

    private static string RenderTooltipText(ItemCard previewCard, TLocalizableText content)
    {
        var enchantment = previewCard.Enchantment ?? EEnchantmentType.Heavy;
        var localized = GetLocalizedText(content, enchantment);
        if (string.IsNullOrWhiteSpace(localized))
            return string.Empty;

        try
        {
            return RenderWithCardTooltipData(previewCard, localized).TrimEnd();
        }
        catch (Exception ex)
        {
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.ItemEnchantPreview,
                    "item_enchant_preview.render.degraded",
                    storm: ["stage", "reason_code"]
                ),
                ex,
                ("stage", ItemEnchantRenderStage.CardTooltipData),
                ("reason_code", ItemEnchantLogReasonCode.RenderFallback),
                ("enchantment", enchantment)
            );
            try
            {
                var builder = TooltipBuilder.Create(
                    CreateTooltipContext(
                        previewCard,
                        previewCard.Template!,
                        new ValueContext(Data.Run, previewCard)
                    ),
                    localized
                );

                return RenderTooltipBuilder(builder).TrimEnd();
            }
            catch (Exception innerEx)
            {
                BppLog.WarnEvent(
                    new BppLogEvent(
                        BppLogFeatureScope.ItemEnchantPreview,
                        "item_enchant_preview.render.degraded",
                        storm: ["stage", "reason_code"]
                    ),
                    innerEx,
                    ("stage", ItemEnchantRenderStage.TooltipBuilder),
                    ("reason_code", ItemEnchantLogReasonCode.RawTextFallback),
                    ("enchantment", enchantment)
                );
                return localized;
            }
        }
    }

    private static string RenderWithCardTooltipData(ItemCard previewCard, string localized)
    {
        var builder = TooltipBuilder.Create(
            CreateTooltipContext(
                previewCard,
                previewCard.Template!,
                new ValueContext(Data.Run, previewCard)
            ),
            localized
        );

        if (CardTooltipRenderMethod == null)
            return RenderTooltipBuilder(builder);

        var tooltipData = new CardTooltipData(previewCard, previewCard.Template!);
        var rendered =
            CardTooltipRenderMethod.Invoke(tooltipData, new object[] { builder }) as string;
        return string.IsNullOrWhiteSpace(rendered) ? RenderTooltipBuilder(builder) : rendered;
    }

    private static readonly ConstructorInfo? TooltipContextConstructor =
        typeof(TooltipContext).GetConstructor(
            new[] { typeof(Card), typeof(ITCard), typeof(ValueContext) }
        );
    private static readonly FieldInfo? TooltipContextInstanceField = TooltipContextField(
        nameof(TooltipContext.Instance)
    );
    private static readonly FieldInfo? TooltipContextTemplateField = TooltipContextField(
        nameof(TooltipContext.Template)
    );
    private static readonly FieldInfo? TooltipContextValueContextField = TooltipContextField(
        nameof(TooltipContext.ValueContext)
    );

    private static TooltipContext CreateTooltipContext(
        Card instance,
        ITCard template,
        ValueContext valueContext
    )
    {
        if (TooltipContextConstructor != null)
            return (TooltipContext)
                TooltipContextConstructor.Invoke(new object[] { instance, template, valueContext });

        var context = default(TooltipContext);
        object boxed = context;
        TooltipContextInstanceField?.SetValue(boxed, instance);
        TooltipContextTemplateField?.SetValue(boxed, template);
        TooltipContextValueContextField?.SetValue(boxed, valueContext);
        return (TooltipContext)boxed;
    }

    private static FieldInfo? TooltipContextField(string fieldName) =>
        typeof(TooltipContext).GetField(fieldName, BindingFlags.Instance | BindingFlags.Public);

    private static string GetLocalizedText(TLocalizableText content, EEnchantmentType enchantment)
    {
        try
        {
            return content.GetLocalizedText();
        }
        catch (Exception ex)
        {
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.ItemEnchantPreview,
                    "item_enchant_preview.render.degraded",
                    storm: ["stage", "reason_code"]
                ),
                ex,
                ("stage", ItemEnchantRenderStage.Localization),
                ("reason_code", ItemEnchantLogReasonCode.LocalizationFallback),
                ("enchantment", enchantment)
            );
            return content.Text ?? string.Empty;
        }
    }

    private static string RenderTooltipBuilder(TooltipBuilder builder)
    {
        var rendered = new StringBuilder();
        foreach (var component in builder.Components)
        {
            if (
                component is ITooltipToken token
                && token.ReferencedAttribute.HasValue
                && token.ReferencedAttribute.Value.RequiresConversionToSeconds()
            )
            {
                var seconds = TooltipExtensions.MillisecondsToSeconds(
                    token.Resolve().GetValueOrDefault()
                );
                rendered.Append(
                    seconds.IsDecimal()
                        ? seconds.GetDecimalValueString()
                        : seconds.ToString(CultureInfo.CurrentCulture)
                );
            }
            else
            {
                rendered.Append(component.Render());
            }
        }

        return rendered.ToString();
    }
}

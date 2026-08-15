#nullable enable
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Cards.Encounter;
using TheBazaar.Tooltips;
using TheBazaar.Utilities;

namespace BazaarPlusPlus.GameInterop.Cards;

/// <summary>Builds the same runtime-valued mechanic text shown by the native card tooltip.</summary>
internal static class CardDescriptionResolver
{
    internal static string? Resolve(Card? card)
    {
        if (card is null)
            return null;

        try
        {
            var tooltip = CardTooltipData.CreateCardTooltipData(card);
            if (tooltip is null)
                return null;

            var parts = new List<string>();
            if (tooltip.CardTemplate is TCardEncounter)
            {
                var description = tooltip.GetCardDescription().description;
                Add(parts, description);
            }
            else
            {
                var passive = tooltip.GetPassiveTooltipBlock(card).passiveTooltip;
                try
                {
                    Add(parts, passive.ToString());
                }
                finally
                {
                    passive.ReturnStringBuilder();
                }
            }

            var active = tooltip.GetActiveAbilityTooltipBlock();
            try
            {
                foreach (var segment in active)
                    Add(parts, segment.Text);
            }
            finally
            {
                active.ReturnTooltipSegments();
            }

            return parts.Count == 0 ? null : string.Join("\n", parts);
        }
        catch
        {
            // Tooltip data is unavailable during some scene transitions and degraded paths.
            return null;
        }
    }

    private static void Add(ICollection<string> parts, string? text)
    {
        var normalized = CardDescriptionTextNormalizer.Normalize(text);
        if (normalized is not null)
            parts.Add(normalized);
    }
}

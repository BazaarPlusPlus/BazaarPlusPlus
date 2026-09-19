#nullable enable
using System.Text;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Item;

namespace BazaarPlusPlus.GameInterop.MonsterBoardPreview;

internal static class NativeBoardSignature
{
    internal static string For(IEnumerable<TCardInstance> cards)
    {
        var result = new StringBuilder();
        foreach (var card in cards)
        {
            result.Append('|').Append(card.TemplateId).Append(':').Append(card.Tier);
            if (card is TCardInstanceItem item)
                result.Append(':').Append(item.SocketId).Append(':').Append(item.EnchantmentType);
            if (card.Attributes != null)
                foreach (var pair in card.Attributes.OrderBy(p => p.Key))
                    result.Append(';').Append(pair.Key).Append('=').Append(pair.Value);
        }
        return result.ToString();
    }
}

internal sealed class NativeBoardPartialFailure(int count, Exception inner)
    : Exception($"{count} native card previews are unavailable.", inner)
{
    internal int Count { get; } = count;
}

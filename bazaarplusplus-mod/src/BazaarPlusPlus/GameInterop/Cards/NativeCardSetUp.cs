#nullable enable
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Core.Types;
using TheBazaar.UI;

namespace BazaarPlusPlus.GameInterop.Cards;

// Keep saved values before SetUp because it overwrites the instance dictionary with starting
// tier values and replaces the client dictionary with tier and enchantment defaults.
internal static partial class NativeCardSetUp
{
    private const string TinyArtKeyword = "_ISTINY_ON";

    // Apply Tiny after SetUp because SetUp uses the template size and clears the material keyword.
    public static void ShowTiny(CardPreviewBase card, ECardSize size)
    {
        if (card._clientCard != null)
            card._clientCard.Size = size;
        if (card._cardMaterial != null)
            card._cardMaterial.EnableKeyword(TinyArtKeyword);
    }

    public static Kept Keep(TCardInstance instance) => new(instance.Attributes);

    internal sealed class Kept
    {
        private readonly Dictionary<ECardAttributeType, int>? _instance;
        private readonly Dictionary<ECardAttributeType, int> _values;

        internal Kept(Dictionary<ECardAttributeType, int>? instance)
        {
            _instance = instance;
            _values = instance != null ? new(instance) : new();
        }

        // Restore the instance dictionary too so later previews of the same instance key and show
        // the same saved values.
        public bool WriteOver(IDictionary<ECardAttributeType, int>? target)
        {
            if (_values.Count == 0)
                return false;
            _instance!.Clear();
            foreach (var pair in _values)
                _instance[pair.Key] = pair.Value;
            if (target == null)
                return false;
            foreach (var pair in _values)
                target[pair.Key] = pair.Value;
            return true;
        }

        public void ShowOn(CardPreviewBase card)
        {
            if (WriteOver(card._clientCard?.Attributes))
                (card as CardPreviewItem)?._gemGroupController?.Initialize(card._clientCard);
        }
    }
}

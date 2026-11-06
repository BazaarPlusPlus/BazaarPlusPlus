#nullable enable
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Core.Types;
using TheBazaar.UI;

namespace BazaarPlusPlus.GameInterop.Cards;

/// <summary>
/// A native card BPP sets up from a template shows the Saved Card Values its instance carries,
/// written over the template's tier and enchantment values. The game's
/// <c>CardPreviewBase.SetUp</c> adopts the instance's attribute dictionary, writes the starting
/// tier into it, then gives the client card a new dictionary with the tier's and the
/// enchantment's values, so the saved values go on after it: a caller takes <see cref="Keep"/>
/// before it runs the set-up and calls <see cref="Kept.ShowOn"/> after a successful one.
/// </summary>
internal static partial class NativeCardSetUp
{
    private const string TinyArtKeyword = "_ISTINY_ON";

    /// <summary>
    /// After a successful set-up, on a Tiny item: the client card at its real size and the art at
    /// the crop the game's board gives a Tiny item (<c>ItemVisualsController.SetIsTiny</c>). The
    /// game's set-up uses the template's size and turns the keyword off on the fresh material.
    /// </summary>
    public static void ShowTiny(CardPreviewBase card, ECardSize size)
    {
        if (card._clientCard != null)
            card._clientCard.Size = size;
        if (card._cardMaterial != null)
            card._cardMaterial.EnableKeyword(TinyArtKeyword);
    }

    /// <summary>Before the set-up: the instance's values, for after it.</summary>
    public static Kept Keep(TCardInstance instance) => new(instance.Attributes);

    /// <summary>An instance's values, kept through the set-up.</summary>
    internal sealed class Kept
    {
        private readonly Dictionary<ECardAttributeType, int>? _instance;
        private readonly Dictionary<ECardAttributeType, int> _values;

        internal Kept(Dictionary<ECardAttributeType, int>? instance)
        {
            _instance = instance;
            _values = instance != null ? new(instance) : new();
        }

        /// <summary>
        /// Puts the kept values back into the instance's own dictionary, which the set-up wrote the
        /// starting tier into, so a board that renders the same instance again keys and shows the
        /// same values (<c>OwnedMonsterBoardPreview</c>, <c>NativeBoardSignature</c>), then writes
        /// them over <paramref name="target"/>. Returns whether anything was written.
        /// </summary>
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

        /// <summary>
        /// After a successful set-up: the values over the card's client card, and an item's gems
        /// drawn again from them, as the set-up drew them from the template's.
        /// </summary>
        public void ShowOn(CardPreviewBase card)
        {
            if (WriteOver(card._clientCard?.Attributes))
                (card as CardPreviewItem)?._gemGroupController?.Initialize(card._clientCard);
        }
    }
}

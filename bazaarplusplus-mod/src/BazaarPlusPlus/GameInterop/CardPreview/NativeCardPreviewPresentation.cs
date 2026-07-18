#nullable enable
using TheBazaar;
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.CardPreview;

internal sealed class NativeCardPreviewPresentation
{
    private readonly GameObject[] _supplementalVisualRoots;

    private NativeCardPreviewPresentation(GameObject[] supplementalVisualRoots) =>
        _supplementalVisualRoots = supplementalVisualRoots;

    internal static NativeCardPreviewPresentation Create(Component card)
    {
        if (card == null)
            throw new ArgumentNullException(nameof(card));

        var roots = new List<GameObject>();
        var seen = new HashSet<GameObject>();
        foreach (var group in card.GetComponentsInChildren<CardGemGroupBase>(true))
        {
            if (group == null || group.gameObject == card.gameObject || !seen.Add(group.gameObject))
                continue;
            roots.Add(group.gameObject);
        }

        var presentation = new NativeCardPreviewPresentation(roots.ToArray());
        presentation.ConcealSupplementalVisuals();
        return presentation;
    }

    internal void RevealSupplementalVisuals() => SetSupplementalVisualsActive(true);

    internal void ConcealSupplementalVisuals() => SetSupplementalVisualsActive(false);

    private void SetSupplementalVisualsActive(bool active)
    {
        foreach (var root in _supplementalVisualRoots)
        {
            if (root != null)
                root.SetActive(active);
        }
    }
}

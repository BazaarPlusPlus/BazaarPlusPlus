#nullable enable
using TheBazaar;
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.CardPreview;

internal sealed class NativeCardPreviewPresentation
{
    // Main-thread scratch for Create, which never awaits; cleared before it returns.
    private static readonly List<CardGemGroupBase> GemGroups = new();
    private static readonly List<GameObject> Roots = new();
    private readonly GameObject[] _supplementalVisualRoots;

    private NativeCardPreviewPresentation(GameObject[] supplementalVisualRoots) =>
        _supplementalVisualRoots = supplementalVisualRoots;

    internal static NativeCardPreviewPresentation Create(Component card)
    {
        if (card == null)
            throw new ArgumentNullException(nameof(card));

        GameObject[] roots;
        try
        {
            card.GetComponentsInChildren(true, GemGroups);
            foreach (var group in GemGroups)
            {
                if (group == null)
                    continue;
                var root = group.gameObject;
                // A card carries a handful of gem groups, so a linear scan replaces the HashSet.
                if (root == card.gameObject || Roots.Contains(root))
                    continue;
                Roots.Add(root);
            }
            roots = Roots.ToArray();
        }
        finally
        {
            GemGroups.Clear();
            Roots.Clear();
        }

        var presentation = new NativeCardPreviewPresentation(roots);
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

#nullable enable
namespace BazaarPlusPlus.Game.Tooltips;

// The game's tooltip transition skips exit for the previous CurrentCard, leaving its fusion visual latched in toggle mode.
internal static class UpgradePreviewCardLatch
{
    private static readonly HashSet<CardController> Entered = new();
    private static readonly List<CardController> ReleaseScratch = new();

    internal static void NoteEntered(CardController controller)
    {
        if (controller != null)
            Entered.Add(controller);
    }

    internal static void NoteExited(CardController controller)
    {
        if (controller is not null)
            Entered.Remove(controller);
    }

    internal static void ReleaseStale(bool previewActive)
    {
        if (Entered.Count == 0)
            return;

        ReleaseScratch.Clear();
        foreach (var controller in Entered)
        {
            // Unity's overloaded == treats destroyed controllers as null while the set still
            // holds a live reference, so the null-forgiving add is safe.
            if (
                controller == null
                || ShouldReleaseLatch(controller.IsCursorOverCard, previewActive)
            )
            {
                ReleaseScratch.Add(controller!);
            }
        }

        foreach (var controller in ReleaseScratch)
        {
            Entered.Remove(controller);
            if (controller != null)
                controller.ExitUpgradePreview();
        }
        ReleaseScratch.Clear();
    }

    internal static bool ShouldReleaseLatch(bool isCursorOverCard, bool previewActive) =>
        !isCursorOverCard || !previewActive;
}

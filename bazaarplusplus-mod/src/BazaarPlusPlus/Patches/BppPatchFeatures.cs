#nullable enable
using BazaarPlusPlus.Game.EventPreview;
using BazaarPlusPlus.Game.Screenshots;

namespace BazaarPlusPlus.Patches;

internal sealed class BppPatchFeatures
{
    internal BppPatchFeatures(
        IEncounterPreviewModule encounterPreview,
        IEndOfRunCaptureWorkflow endOfRunCaptureWorkflow
    )
    {
        EncounterPreview =
            encounterPreview ?? throw new ArgumentNullException(nameof(encounterPreview));
        EndOfRunCaptureWorkflow =
            endOfRunCaptureWorkflow
            ?? throw new ArgumentNullException(nameof(endOfRunCaptureWorkflow));
    }

    internal IEncounterPreviewModule EncounterPreview { get; }
    internal IEndOfRunCaptureWorkflow EndOfRunCaptureWorkflow { get; }
}

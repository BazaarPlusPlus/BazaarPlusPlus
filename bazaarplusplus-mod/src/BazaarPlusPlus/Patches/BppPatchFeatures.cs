#nullable enable
using BazaarPlusPlus.Game.EventPreview;
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.PostCombatImpact;
using BazaarPlusPlus.Game.Screenshots;

namespace BazaarPlusPlus.Patches;

internal sealed class BppPatchFeatures
{
    internal BppPatchFeatures(
        IEncounterPreviewModule encounterPreview,
        IEndOfRunCaptureWorkflow endOfRunCaptureWorkflow,
        IPostCombatImpactModule postCombatImpact
    )
    {
        EncounterPreview =
            encounterPreview ?? throw new ArgumentNullException(nameof(encounterPreview));
        EndOfRunCaptureWorkflow =
            endOfRunCaptureWorkflow
            ?? throw new ArgumentNullException(nameof(endOfRunCaptureWorkflow));
        PostCombatImpact =
            postCombatImpact ?? throw new ArgumentNullException(nameof(postCombatImpact));
    }

    internal HistoryPanelMenuEntry HistoryMenu { get; } = new();

    internal IEncounterPreviewModule EncounterPreview { get; }
    internal IEndOfRunCaptureWorkflow EndOfRunCaptureWorkflow { get; }
    internal IPostCombatImpactModule PostCombatImpact { get; }
}

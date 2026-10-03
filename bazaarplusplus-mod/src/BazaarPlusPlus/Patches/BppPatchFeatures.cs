#nullable enable
using BazaarPlusPlus.Game.EventPreview;
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.PostCombatImpact;
using BazaarPlusPlus.Game.Screenshots;

namespace BazaarPlusPlus.Patches;

internal sealed class BppPatchFeatures
{
    internal BppPatchFeatures(
        EncounterPreviewModule encounterPreview,
        IEndOfRunCaptureWorkflow endOfRunCaptureWorkflow,
        PostCombatImpactModule postCombatImpact
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

    internal EncounterPreviewModule EncounterPreview { get; }
    internal IEndOfRunCaptureWorkflow EndOfRunCaptureWorkflow { get; }
    internal PostCombatImpactModule PostCombatImpact { get; }
}

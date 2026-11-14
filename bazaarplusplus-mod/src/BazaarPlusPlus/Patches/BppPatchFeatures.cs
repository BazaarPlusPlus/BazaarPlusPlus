#nullable enable
using BazaarPlusPlus.Game.CollectionPanel;
using BazaarPlusPlus.Game.EncounterPreview;
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.PostCombatImpact;
using BazaarPlusPlus.Game.RandomCosmetics;
using BazaarPlusPlus.Game.Screenshots;
using BazaarPlusPlus.Game.SkillDetails;

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
    internal CollectionInspectionModule CollectionInspection { get; } = new();
    internal RandomCosmeticPoolService RandomCosmeticPool { get; } = new();
    internal DesktopSkillOverlayLayout SkillOverlayLayout { get; } = new();
    internal GameInterop.HeroPortraits.NativeHeroPortraitCatalog HeroPortraits { get; } = new();
    internal Game.HeroPortraits.AnimatedHeroPortraitController? PortraitAnimation { get; set; }

    internal EncounterPreviewModule EncounterPreview { get; }
    internal IEndOfRunCaptureWorkflow EndOfRunCaptureWorkflow { get; }
    internal PostCombatImpactModule PostCombatImpact { get; }
}

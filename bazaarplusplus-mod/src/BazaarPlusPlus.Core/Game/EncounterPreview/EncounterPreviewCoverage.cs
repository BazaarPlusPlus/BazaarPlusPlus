#nullable enable
namespace BazaarPlusPlus.Game.EncounterPreview;

public sealed class EncounterPreviewCoverage
{
    public EncounterPreviewCoverage(
        int eventFailureCount,
        int levelUpFailureCount,
        int unsupportedLevelUpPartCount,
        int missingReferencedTemplateCount
    )
    {
        EventFailureCount = Math.Max(0, eventFailureCount);
        LevelUpFailureCount = Math.Max(0, levelUpFailureCount);
        UnsupportedLevelUpPartCount = Math.Max(0, unsupportedLevelUpPartCount);
        MissingReferencedTemplateCount = Math.Max(0, missingReferencedTemplateCount);
    }

    public int EventFailureCount { get; }

    public int LevelUpFailureCount { get; }

    public int UnsupportedLevelUpPartCount { get; }

    public int MissingReferencedTemplateCount { get; }
}

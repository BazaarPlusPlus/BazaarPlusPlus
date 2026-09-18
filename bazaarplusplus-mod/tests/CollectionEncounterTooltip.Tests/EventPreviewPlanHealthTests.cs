using Xunit;

namespace EncounterTooltip.Tests;

public sealed class EventPreviewPlanHealthTests
{
    [Fact]
    public void Expected_source_limitations_do_not_degrade_runtime_health()
    {
        var coverage = new EventPreviewCoverage(
            eventFailureCount: 0,
            levelUpFailureCount: 0,
            unsupportedLevelUpPartCount: 107,
            missingReferencedTemplateCount: 28
        );

        Assert.False(EncounterPreviewModule.EventPreviewPlanHealth.HasDegradedCoverage(coverage));
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    public void Compilation_failures_degrade_runtime_health(
        int eventFailureCount,
        int levelUpFailureCount
    )
    {
        var coverage = new EventPreviewCoverage(
            eventFailureCount,
            levelUpFailureCount,
            unsupportedLevelUpPartCount: 0,
            missingReferencedTemplateCount: 0
        );

        Assert.True(EncounterPreviewModule.EventPreviewPlanHealth.HasDegradedCoverage(coverage));
    }
}

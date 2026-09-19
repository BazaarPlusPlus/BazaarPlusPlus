using BazaarPlusPlus.Game.PostCombatImpact.Ui;
using Xunit;

namespace PostCombatImpact.Tests;

public sealed class CombatImpactDetailPresentationPolicyTests
{
    [Fact]
    public void Entity_rows_are_rendered_for_compact_groups()
    {
        Assert.True(CombatImpactDetailPresentationPolicy.ShouldRenderEntityRows(0));
        Assert.True(
            CombatImpactDetailPresentationPolicy.ShouldRenderEntityRows(
                CombatImpactDetailPresentationPolicy.MaximumDetailedEntityRows
            )
        );
    }

    [Fact]
    public void Entity_rows_are_suppressed_for_oversized_groups()
    {
        Assert.False(
            CombatImpactDetailPresentationPolicy.ShouldRenderEntityRows(
                CombatImpactDetailPresentationPolicy.MaximumDetailedEntityRows + 1
            )
        );
    }
}

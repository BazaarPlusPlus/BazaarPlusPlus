#nullable enable
namespace BazaarPlusPlus.Game.PostCombatImpact.Ui;

internal static class CombatImpactDetailPresentationPolicy
{
    internal const int MaximumDetailedEntityRows = 12;

    internal static bool ShouldRenderEntityRows(int rowCount) =>
        rowCount <= MaximumDetailedEntityRows;
}

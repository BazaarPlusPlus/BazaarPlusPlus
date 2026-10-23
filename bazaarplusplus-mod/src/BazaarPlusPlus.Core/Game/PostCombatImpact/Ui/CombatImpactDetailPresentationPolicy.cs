#nullable enable
namespace BazaarPlusPlus.Game.PostCombatImpact.Ui;

public static class CombatImpactDetailPresentationPolicy
{
    public const int MaximumDetailedEntityRows = 12;

    public static bool ShouldRenderEntityRows(int rowCount) =>
        rowCount <= MaximumDetailedEntityRows;
}

#nullable enable

namespace BazaarPlusPlus.Game.MonsterPreview;

internal static class CardSetBuildRecommendationModeFlow
{
    public static CardSetBuildRecommendationMode GetNext(
        CardSetBuildRecommendationMode currentMode
    )
    {
        return currentMode == CardSetBuildRecommendationMode.SelectedSet
            ? CardSetBuildRecommendationMode.FinalBuild
            : CardSetBuildRecommendationMode.SelectedSet;
    }
}

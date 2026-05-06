#nullable enable

namespace BazaarPlusPlus.Game.MonsterPreview;

internal static class CardSetPreviewHotkeys
{
    public static CardSetBuildRecommendationMode? ResolveDisplayMode(
        bool currentSetPressed,
        bool finalBuildPressed
    )
    {
        if (currentSetPressed == finalBuildPressed)
            return null;

        return currentSetPressed
            ? CardSetBuildRecommendationMode.SelectedSet
            : CardSetBuildRecommendationMode.FinalBuild;
    }

    public static int ResolveRecommendationDelta(
        bool previousCandidatePressed,
        bool nextCandidatePressed
    )
    {
        if (previousCandidatePressed == nextCandidatePressed)
            return 0;

        return previousCandidatePressed ? -1 : 1;
    }
}

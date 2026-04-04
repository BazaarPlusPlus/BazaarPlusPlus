namespace BazaarPlusPlus.Game.MonsterPreview;

internal enum PreviewCardKind
{
    Item,
    Skill,
}

internal static class PreviewCardLifecyclePolicy
{
    public static bool ShouldReturnToPool(PreviewCardKind kind)
    {
        return false;
    }

    public static bool ShouldRefreshAfterInstantiate(PreviewCardKind kind)
    {
        return kind == PreviewCardKind.Item || kind == PreviewCardKind.Skill;
    }
}

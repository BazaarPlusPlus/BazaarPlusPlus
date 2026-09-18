#nullable enable
namespace BazaarPlusPlus.GameInterop.MonsterBoardPreview;

internal readonly record struct NativeMonsterBoardLayout(
    float BoardScale,
    float SkillScale,
    float SkillContentWidth
)
{
    internal static NativeMonsterBoardLayout Calculate(
        float width,
        float height,
        float boardWidth,
        float boardHeight,
        float skillsWidth,
        float skillsHeight,
        bool itemsOnly = false,
        float itemFooterHeight = 0,
        float itemTopInset = 0
    )
    {
        var boardScale = Math.Min(
            width / Math.Max(1, boardWidth),
            (itemsOnly ? Math.Max(1, height - itemFooterHeight - itemTopInset) : height * .74f)
                / Math.Max(1, boardHeight)
        );
        var skillScale = height * .20f / Math.Max(1, skillsHeight);
        return new(boardScale, skillScale, Math.Max(width, skillsWidth * skillScale));
    }
}

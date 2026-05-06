using BazaarPlusPlus.Game.MonsterPreview;

namespace BazaarPlusPlus.Game.PreviewSurface;

internal static class PreviewBoardRenderTargetFactory
{
    public static IBoardRenderTarget Create(string surfaceName)
    {
        return new PreviewBoardRenderTarget(
            new PreviewBoardSurface(
                surfaceName,
                new PreviewItemCardSurface(),
                new PreviewSkillCardSurface()
            )
        );
    }
}

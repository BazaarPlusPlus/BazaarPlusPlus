#pragma warning disable CS0436
namespace BazaarPlusPlus.Game.MonsterPreview;

internal interface IBoardRenderTarget
{
    void Render(BoardRenderModel renderModel);

    void SetVisible(bool visible);
}

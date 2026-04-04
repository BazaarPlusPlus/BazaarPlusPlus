#pragma warning disable CS0436
namespace BazaarPlusPlus.Game.MonsterPreview;

internal interface IPreviewDataSource
{
    bool TryBuild(out PreviewBoardModel model);
}

#pragma warning disable CS0436
using BazaarPlusPlus.Game.PreviewSurface;

namespace BazaarPlusPlus.Game.MonsterPreview;

internal interface IPreviewDataSource
{
    bool TryBuild(out PreviewBoardModel model);
}

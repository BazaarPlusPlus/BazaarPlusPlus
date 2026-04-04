#pragma warning disable CS0436
namespace BazaarPlusPlus.Game.MonsterPreview;

internal interface IBoardAnchorStrategy
{
    bool TryResolve(out BoardPose pose);
}

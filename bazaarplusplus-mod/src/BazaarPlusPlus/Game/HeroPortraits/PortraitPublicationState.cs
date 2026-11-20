#nullable enable

namespace BazaarPlusPlus.Game.HeroPortraits;

// Keep the native sprite through an actual frame boundary while masks and native renderers activate.
internal sealed class PortraitPublicationState
{
    private int? _stagedFrame;

    internal void Stage(int frame) => _stagedFrame = frame;

    internal bool CanPublish(int frame) => _stagedFrame.HasValue && frame > _stagedFrame.Value;

    internal void Hide() => _stagedFrame = null;
}

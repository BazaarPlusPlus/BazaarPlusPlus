#nullable enable

namespace BazaarPlusPlus.Game.HeroPortraits;

// Keep the native sprite through the frame that activates masks and native renderers.
// A real frame boundary, rather than a second callback in the same frame, publishes them.
internal sealed class PortraitPublicationState
{
    private int? _stagedFrame;

    internal void Stage(int frame) => _stagedFrame = frame;

    internal bool CanPublish(int frame) => _stagedFrame.HasValue && frame > _stagedFrame.Value;

    internal void Hide() => _stagedFrame = null;
}

#nullable enable
using BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;

namespace BazaarPlusPlus.Game.LiveBuildPanel;

internal enum LiveBuildRefreshRequest
{
    Ignore,
    Download,
    NoChange,
}

internal enum LiveBuildRefreshEffect
{
    None,
    Status,
    Recommendations,
}

/// <summary>Owns manual refresh decisions for one Live Build Panel lifetime.</summary>
internal sealed class LiveBuildRefreshState
{
    private enum Presence
    {
        Hidden,
        Visible,
        Destroyed,
    }

    private Presence _presence;
    private bool _hasSuccessfullyPulled;

    internal bool IsVisible => _presence == Presence.Visible;
    internal bool IsPending { get; private set; }
    internal BuildRecommendationRefreshResult? Feedback { get; private set; }

    internal void SetVisible(bool visible)
    {
        if (_presence == Presence.Destroyed)
            return;

        _presence = visible ? Presence.Visible : Presence.Hidden;
        if (visible && !IsPending)
            Feedback = null;
    }

    internal void Destroy() => _presence = Presence.Destroyed;

    internal LiveBuildRefreshRequest BeginRefresh()
    {
        if (!IsVisible || IsPending)
            return LiveBuildRefreshRequest.Ignore;

        IsPending = true;
        Feedback = null;
        return _hasSuccessfullyPulled
            ? LiveBuildRefreshRequest.NoChange
            : LiveBuildRefreshRequest.Download;
    }

    internal LiveBuildRefreshEffect CompleteRefresh(BuildRecommendationRefreshResult result)
    {
        IsPending = false;
        Feedback = result;
        if (result.Succeeded)
            _hasSuccessfullyPulled = true;

        if (!IsVisible)
            return LiveBuildRefreshEffect.None;

        return result.Succeeded
            ? LiveBuildRefreshEffect.Recommendations
            : LiveBuildRefreshEffect.Status;
    }
}

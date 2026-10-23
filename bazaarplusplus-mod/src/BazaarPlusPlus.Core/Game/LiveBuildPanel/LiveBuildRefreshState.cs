#nullable enable
using BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;

namespace BazaarPlusPlus.Game.LiveBuildPanel;

public enum LiveBuildRefreshRequest
{
    Ignore,
    Download,
    NoChange,
}

public enum LiveBuildRefreshEffect
{
    None,
    Status,
    Recommendations,
}

/// <summary>Owns manual refresh decisions for one Live Build Panel lifetime.</summary>
public sealed class LiveBuildRefreshState
{
    private enum Presence
    {
        Hidden,
        Visible,
        Destroyed,
    }

    private Presence _presence;
    private bool _hasSuccessfullyPulled;

    public bool IsVisible => _presence == Presence.Visible;
    public bool IsPending { get; private set; }
    public BuildRecommendationRefreshResult? Feedback { get; private set; }

    public void SetVisible(bool visible)
    {
        if (_presence == Presence.Destroyed)
            return;

        _presence = visible ? Presence.Visible : Presence.Hidden;
        if (visible && !IsPending)
            Feedback = null;
    }

    public void Destroy() => _presence = Presence.Destroyed;

    public LiveBuildRefreshRequest BeginRefresh()
    {
        if (!IsVisible || IsPending)
            return LiveBuildRefreshRequest.Ignore;

        IsPending = true;
        Feedback = null;
        return _hasSuccessfullyPulled
            ? LiveBuildRefreshRequest.NoChange
            : LiveBuildRefreshRequest.Download;
    }

    public LiveBuildRefreshEffect CompleteRefresh(BuildRecommendationRefreshResult result)
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

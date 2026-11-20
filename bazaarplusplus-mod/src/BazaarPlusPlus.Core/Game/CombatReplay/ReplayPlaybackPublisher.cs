#nullable enable
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.PvpBattles;

namespace BazaarPlusPlus.Game.CombatReplay;

public sealed class ReplayPlaybackPublisher
{
    private readonly IBppServices _services;
    private string? _activeBattleId;
    private PvpBattleManifest? _activeManifest;
    private CombatReplayPlaybackSource _activeSource;
    private bool _activeRecordVideo;
    private bool _startingPublished;

    public ReplayPlaybackPublisher(IBppServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public string? ActiveSessionBattleId => _activeBattleId;

    public PvpBattleManifest? ActiveSessionManifest => _activeManifest;

    public bool TryPromoteActiveSessionToRecording(string battleId)
    {
        if (
            string.IsNullOrWhiteSpace(battleId)
            || !string.Equals(_activeBattleId, battleId, StringComparison.Ordinal)
        )
        {
            return false;
        }

        _activeRecordVideo = true;
        _startingPublished = false;
        return true;
    }

    public void BeginSession(
        string battleId,
        PvpBattleManifest? manifest,
        CombatReplayPlaybackSource source,
        bool recordVideo
    )
    {
        _activeBattleId = battleId;
        _activeManifest = manifest;
        _activeSource = source;
        _activeRecordVideo = recordVideo;
        _startingPublished = false;
    }

    public ReplayPlaybackPublishOutcome PublishStarting()
    {
        if (_startingPublished)
            return ReplayPlaybackPublishOutcome.Success();

        var battleId = _activeBattleId;
        if (string.IsNullOrEmpty(battleId))
            return ReplayPlaybackPublishOutcome.Failure(
                new InvalidOperationException("Replay playback session is unavailable.")
            );

        _startingPublished = true;
        try
        {
            _services.EventBus.Publish(
                new CombatReplayPlaybackStarting
                {
                    BattleId = battleId,
                    Manifest = _activeManifest,
                    Source = _activeSource,
                    RecordVideo = _activeRecordVideo,
                }
            );
            return ReplayPlaybackPublishOutcome.Success();
        }
        catch (Exception ex)
        {
            return ReplayPlaybackPublishOutcome.Failure(ex);
        }
    }

    public ReplayPlaybackPublishOutcome PublishEnded(string reason, bool failed)
    {
        // Clear even a session that failed before publishing its start so its battle ID cannot leak into later replays.
        var battleId = _activeBattleId ?? string.Empty;
        var startingPublished = _startingPublished;
        _startingPublished = false;
        _activeBattleId = null;
        _activeManifest = null;

        if (!startingPublished)
            return ReplayPlaybackPublishOutcome.Success();

        try
        {
            _services.EventBus.Publish(
                new CombatReplayPlaybackEnded
                {
                    BattleId = battleId,
                    Reason = reason,
                    Failed = failed,
                }
            );
            return ReplayPlaybackPublishOutcome.Success();
        }
        catch (Exception ex)
        {
            return ReplayPlaybackPublishOutcome.Failure(ex);
        }
    }
}

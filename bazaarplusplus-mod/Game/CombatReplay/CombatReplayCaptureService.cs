#nullable enable
using System;
using BazaarGameShared.Infra.Messages;
using BazaarPlusPlus.Game.PvpBattles;

namespace BazaarPlusPlus.Game.CombatReplay;

internal sealed class CombatReplayCaptureService
{
    /*
        Compatibility markers preserved for source-level tests while the implementation now
        delegates to matcher/collector/factories:
        CaptureCurrentHandCardsAtOpening(ECombatantId.Player)
        CaptureCurrentSkillsAtOpening(ECombatantId.Player)
        CaptureOpeningHandCards(message, ECombatantId.Opponent)
        CaptureOpponentSkillsFromOpening(message)
        GameSimEventCardSpawned
        GameSimEventPlayerSkillEquipped
        return state == ERunState.PVPCombat;
        OpponentName = candidate.OpponentName
        OpponentAccountId = candidate.OpponentAccountId
        CapturedEmpty
        LiveRetry
        OpeningMessage
    */

    private readonly Func<DateTimeOffset> _clock;
    private readonly PvpBattleSequenceMatcher _matcher;
    private readonly PvpBattleSnapshotCollector _collector;
    private readonly PvpBattleManifestFactory _manifestFactory;
    private readonly PvpReplayPayloadFactory _payloadFactory;
    private CombatReplaySequenceCandidate _candidate = new CombatReplaySequenceCandidate();

    public CombatReplayCaptureService()
        : this(null) { }

    public CombatReplayCaptureService(Func<DateTimeOffset>? clock = null)
    {
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _matcher = new PvpBattleSequenceMatcher();
        _collector = new PvpBattleSnapshotCollector();
        _manifestFactory = new PvpBattleManifestFactory();
        _payloadFactory = new PvpReplayPayloadFactory();
    }

    public PvpBattleCaptureArtifact? Accept(INetMessage message, string? runId)
    {
        if (message == null)
            throw new ArgumentNullException(nameof(message));

        return message switch
        {
            NetMessageGameSim gameSimMessage => AcceptGameSim(gameSimMessage, runId),
            NetMessageCombatSim combatSimMessage => AcceptCombatSim(combatSimMessage, runId),
            _ => null,
        };
    }

    private PvpBattleCaptureArtifact? AcceptGameSim(NetMessageGameSim message, string? runId)
    {
        if (_candidate.SpawnMessage == null)
        {
            if (_matcher.IsPvpCombatOpeningMessage(message))
            {
                _candidate = CreateOpeningCandidate(message, runId);
            }

            return null;
        }

        if (_candidate.CombatMessage == null)
        {
            if (_matcher.IsPvpCombatOpeningMessage(message))
            {
                _candidate = CreateOpeningCandidate(message, runId);
            }
            else
            {
                _candidate = _matcher.ResetCandidate();
            }

            return null;
        }

        if (_matcher.IsAnyCombatOpeningMessage(message))
        {
            _candidate = _matcher.IsPvpCombatOpeningMessage(message)
                ? CreateOpeningCandidate(message, runId)
                : _matcher.ResetCandidate();
            return null;
        }

        var completedWindow = _matcher.CreateCompletedWindow(_candidate, message, runId);
        var record = CreateArtifact(_candidate, completedWindow);
        _candidate = _matcher.ResetCandidate();
        return record;
    }

    private PvpBattleCaptureArtifact? AcceptCombatSim(NetMessageCombatSim message, string? runId)
    {
        if (_candidate.SpawnMessage == null)
            return null;

        if (_candidate.CombatMessage != null)
        {
            _candidate = _matcher.ResetCandidate();
            return null;
        }

        _candidate.RunId ??= runId;
        _candidate.CombatMessage = message;
        CaptureLiveSnapshots(_candidate);
        return null;
    }

    private PvpBattleCaptureArtifact CreateArtifact(
        CombatReplaySequenceCandidate candidate,
        PvpBattleSequenceWindow sequenceWindow
    )
    {
        var combatMessage =
            sequenceWindow.CombatMessage
            ?? throw new InvalidOperationException("Combat message is required.");
        CaptureLiveSnapshots(candidate);

        var snapshots = _collector.BuildSnapshots(candidate);
        var battleId = candidate.BattleId ?? _manifestFactory.CreateBattleId();
        var manifest = _manifestFactory.Create(
            battleId,
            sequenceWindow,
            _clock(),
            _collector.BuildParticipants(candidate),
            _collector.BuildOutcome(combatMessage),
            snapshots
        );
        var payload = _payloadFactory.Create(battleId, sequenceWindow);

        return new PvpBattleCaptureArtifact { Manifest = manifest, Payload = payload };
    }

    private CombatReplaySequenceCandidate CreateOpeningCandidate(
        NetMessageGameSim message,
        string? runId
    )
    {
        var candidate = _collector.CreateOpeningCandidate(message, runId);
        candidate.BattleId = _manifestFactory.CreateBattleId();
        return candidate;
    }

    private void CaptureLiveSnapshots(CombatReplaySequenceCandidate candidate)
    {
        _collector.CaptureLiveSnapshots(candidate);
    }
}

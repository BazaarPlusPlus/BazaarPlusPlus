#nullable enable
using BazaarGameShared.Domain.Cards.Skill;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.GameInterop.CardPreview;
using BazaarPlusPlus.GameInterop.MonsterBoardPreview;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Infrastructure.UiTokens;
using UnityEngine;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed partial class HistoryPanel
{
    private OwnedMonsterBoardPreview? _nativePlayerBoard;
    private OwnedMonsterBoardPreview? _nativeOpponentBoard;
    private HistoryBoardFacts _nativePlayerStatus;
    private HistoryBoardFacts _nativeOpponentStatus;
    private Rect _opponentBounds;

    private void RefreshNativeHistoryBoards()
    {
        if (!IsVisible || _uiView == null)
            return;
        _nativePlayerBoard ??= CreateNativeHistoryBoard(false);
        if (_hasPreviewContainerBounds)
            _nativePlayerBoard.SetBounds(_previewContainerBounds);
        var battle = ActiveSelectedBattle;
        var snapshots = _state.DetailBattleId == battle?.BattleId ? _state.DetailSnapshots : null;
        var player = HistoryBattlePreviewProjection.BuildPlayer(
            snapshots,
            $"player:{battle?.BattleId}"
        );
        var playerSkills = snapshots?.PlayerSkills.Items;
        _nativePlayerBoard.Render(
            $"{player.Signature}:{player.Cards.Count}:{playerSkills?.Count}",
            NativeMonsterBoardItemMapper.Map(player, "history-item"),
            NativeHistorySkills(playerSkills)
        );
        if (_uiView.ShowsBothBoards)
        {
            _nativeOpponentBoard ??= CreateNativeHistoryBoard(true);
            if (_opponentBounds.width > 1)
                _nativeOpponentBoard.SetBounds(_opponentBounds);
            var opponent = HistoryBattlePreviewProjection.BuildOpponent(
                snapshots,
                $"opponent:{battle?.BattleId}"
            );
            var skills = snapshots?.OpponentSkills.Items;
            _nativeOpponentBoard.Render(
                $"{opponent.Signature}:{opponent.Cards.Count}:{skills?.Count}",
                NativeMonsterBoardItemMapper.Map(opponent, "history-item"),
                NativeHistorySkills(skills)
            );
        }
        else
        {
            _nativeOpponentBoard?.Dispose();
            _nativeOpponentBoard = null;
            _nativeOpponentStatus = default;
        }
        RefreshNativeHistoryMessages();
    }

    private HistoryArchiveStatus ArchiveStatus() =>
        HistoryPanelDecisions.ArchiveStatus(
            HistoryArchiveFacts.Observe(
                _state,
                ActiveSelectedBattle,
                _nativePlayerStatus,
                _nativeOpponentStatus
            )
        );

    // Render can reuse an unchanged board without a callback, so every refresh re-derives both
    // board messages from the stored native status.
    private void RefreshNativeHistoryMessages()
    {
        if (_uiView == null)
            return;
        var status = ArchiveStatus();
        SetPreviewStatus(status.PlayerBoardMessage, status.PlayerBoardMessage.Length > 0);
        _uiView.SetOpponentStatus(status.OpponentBoardMessage);
    }

    private OwnedMonsterBoardPreview CreateNativeHistoryBoard(bool opponent) =>
        new(
            transform,
            BppOverlaySorting.NativeCardPreview,
            !opponent,
            (status, exception) =>
            {
                var board = new HistoryBoardFacts(
                    status,
                    (exception as NativeBoardPartialFailure)?.Count ?? 1
                );
                if (opponent)
                    _nativeOpponentStatus = board;
                else
                    _nativePlayerStatus = board;
                RefreshNativeHistoryMessages();
                if (exception != null)
                    BppLog.WarnEvent(
                        new BppLogEvent(
                            BppLogFeatureScope.HistoryPanel,
                            "history_panel.card_preview.degraded",
                            storm: ["operation", "reason_code"]
                        ),
                        exception,
                        ("operation", NativeCardPreviewOperation.SetUp),
                        ("reason_code", NativeCardPreviewFailureReason.SetUpException),
                        ("template_id", null)
                    );
            }
        );

    private static List<TCardInstanceSkill> NativeHistorySkills(
        IList<PvpBattleCardSnapshot>? snapshots
    )
    {
        var skills = new List<TCardInstanceSkill>();
        foreach (var snapshot in snapshots ?? Array.Empty<PvpBattleCardSnapshot>())
        {
            if (
                snapshot.Type != ECardType.Skill
                || !Guid.TryParse(snapshot.TemplateId, out var templateId)
            )
                continue;
            var attributes = new Dictionary<ECardAttributeType, int>();
            foreach (var pair in snapshot.Attributes)
                if (Enum.TryParse<ECardAttributeType>(pair.Key, out var key))
                    attributes[key] = pair.Value;
            skills.Add(
                new TCardInstanceSkill
                {
                    TemplateId = templateId,
                    TemplateVersion = string.Empty,
                    InstanceId = $"history-skill-{skills.Count}",
                    Tier = Enum.TryParse<ETier>(snapshot.Tier, out var tier) ? tier : ETier.Bronze,
                    Attributes = attributes,
                }
            );
        }
        return skills;
    }

    private void DisposeNativeHistoryBoards()
    {
        _nativePlayerBoard?.Dispose();
        _nativeOpponentBoard?.Dispose();
        _nativePlayerBoard = _nativeOpponentBoard = null;
        _nativePlayerStatus = _nativeOpponentStatus = default;
    }
}

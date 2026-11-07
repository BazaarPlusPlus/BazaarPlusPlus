#nullable enable
using BazaarGameShared.Domain.Cards.Skill;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Core.Cards.ItemBoards;
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.GameInterop.CardPreview;
using BazaarPlusPlus.GameInterop.Cards;
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
        var battle = ActiveSelectedBattle;
        var snapshots =
            _state.DetailBattleId == battle?.BattleId ? _coordinator?.DetailSnapshots : null;
        _uiView.SetBoardsAvailable(snapshots != null);
        if (snapshots == null)
        {
            DisposeNativeHistoryBoards();
            RefreshNativeHistoryMessages();
            return;
        }
        _nativePlayerBoard ??= CreateNativeHistoryBoard(false);
        if (_hasPreviewContainerBounds)
            _nativePlayerBoard.SetBounds(_previewContainerBounds);
        var templates = DesktopHistoryCardTemplates.Instance;
        var player = HistoryBoardProjection.Items(
            snapshots?.PlayerHand,
            templates.Contains,
            templates.SocketEffectAttribute,
            $"player:{battle?.BattleId}"
        );
        var playerSkills = HistoryBoardProjection.Skills(
            snapshots?.PlayerSkills,
            templates.Contains
        );
        _nativePlayerBoard.Render(
            $"{player.Signature}:{player.Cards.Count}:{playerSkills.Count}",
            NativeMonsterBoardItemMapper.Map(player, "history-item"),
            NativeHistorySkills(playerSkills)
        );
        if (ShowsBothBoards)
        {
            _nativeOpponentBoard ??= CreateNativeHistoryBoard(true);
            if (_opponentBounds.width > 1)
                _nativeOpponentBoard.SetBounds(_opponentBounds);
            var opponent = HistoryBoardProjection.Items(
                snapshots?.OpponentHand,
                templates.Contains,
                templates.SocketEffectAttribute,
                $"opponent:{battle?.BattleId}"
            );
            var skills = HistoryBoardProjection.Skills(
                snapshots?.OpponentSkills,
                templates.Contains
            );
            _nativeOpponentBoard.Render(
                $"{opponent.Signature}:{opponent.Cards.Count}:{skills.Count}",
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
        HistoryPanelViewModelBuilder.ArchiveStatus(
            _state,
            _coordinator,
            _nativePlayerStatus,
            _nativeOpponentStatus
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

    // Core's skills as the game's instances: the tier through the facets, the saved values by
    // member name.
    private static List<TCardInstanceSkill> NativeHistorySkills(
        IReadOnlyList<BppItemBoardCard> skills
    ) =>
        skills
            .Select(
                (skill, index) =>
                    new TCardInstanceSkill
                    {
                        TemplateId = skill.TemplateId,
                        TemplateVersion = string.Empty,
                        InstanceId = $"history-skill-{index}",
                        Tier = DesktopCardFacets.Tier.TryToSource(skill.Tier, out var tier)
                            ? tier
                            : ETier.Bronze,
                        Attributes = NativeCardSetUp.Values(skill.Attributes),
                    }
            )
            .ToList();

    private void DisposeNativeHistoryBoards()
    {
        _nativePlayerBoard?.Dispose();
        _nativeOpponentBoard?.Dispose();
        _nativePlayerBoard = _nativeOpponentBoard = null;
        _nativePlayerStatus = _nativeOpponentStatus = default;
    }
}

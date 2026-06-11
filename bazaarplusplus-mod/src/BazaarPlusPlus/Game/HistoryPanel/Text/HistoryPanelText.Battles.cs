#nullable enable

using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal static partial class HistoryPanelText
{
    private static readonly LocalizedTextSet NoBattleSelectedText = new(
        "No battle selected",
        "未选择战斗"
    );

    private static readonly LocalizedTextSet UnknownOpponentText = new(
        "Unknown Opponent",
        "未知对手"
    );

    private static readonly LocalizedTextSet SelectBattleForFooterText = new(
        "Select one battle to inspect it, then use Replay when you want to jump back into it.",
        "选择一场战斗进行查看，想重新进入时再使用回放。",
        "選擇一場戰鬥進行檢視，想重新進入時再使用重播。",
        "選擇一場戰鬥進行檢視，想重新進入時再使用重播。"
    );

    private static readonly LocalizedTextSet SelectedBattleText = new(
        "Selected",
        "当前战斗",
        "當前戰鬥",
        "當前戰鬥"
    );

    private static readonly LocalizedTextSet PreviewUnavailablePrefixText = new(
        "Replay unavailable:",
        "回放不可用："
    );

    private static readonly LocalizedTextSet PreviewSelectBattleText = new(
        "Select a battle to preview its recorded cards.",
        "选择一场战斗以预览其记录卡牌。",
        "選擇一場戰鬥以預覽其記錄卡牌。",
        "選擇一場戰鬥以預覽其記錄卡牌。"
    );

    private static readonly LocalizedTextSet NoGhostBattlesText = new(
        "No ghost battles synced yet.",
        "还没有同步到幽灵战斗。",
        "還沒有同步到幽靈戰鬥。",
        "還沒有同步到幽靈戰鬥。"
    );

    private static readonly LocalizedTextSet WinText = new("Win", "胜利", "勝利", "勝利");

    private static readonly LocalizedTextSet LossText = new("Loss", "失败", "失敗", "失敗");

    private static readonly LocalizedTextSet GhostOpponentEliminatedNoticeText = new(
        "After this battle, the opponent is eliminated.",
        "打完这场战斗后，对手直接出局。",
        "打完這場戰鬥後，對手直接出局。",
        "打完這場戰鬥後，對手直接出局。"
    );

    private static readonly LocalizedTextSet GhostOpponentEliminatedShortText = new(
        "Knocked Out",
        "对手出局",
        "對手出局",
        "對手出局"
    );

    internal static string NoBattleSelected() => Resolve(NoBattleSelectedText);

    internal static string UnknownOpponent() => Resolve(UnknownOpponentText);

    internal static string SelectBattleForFooter() => Resolve(SelectBattleForFooterText);

    internal static string SelectedBattle() => Resolve(SelectedBattleText);

    internal static string PreviewUnavailablePrefix() => Resolve(PreviewUnavailablePrefixText);

    internal static string PreviewSelectBattle() => Resolve(PreviewSelectBattleText);

    internal static string NoGhostBattles() => Resolve(NoGhostBattlesText);

    internal static string Win() => Resolve(WinText);

    internal static string Loss() => Resolve(LossText);

    internal static string GhostOpponentEliminatedNotice() =>
        Resolve(GhostOpponentEliminatedNoticeText);

    internal static string GhostOpponentEliminatedShort() =>
        Resolve(GhostOpponentEliminatedShortText);

    internal static string PlayerSideShort() => FormatSimple("YOU", "我方", "我方", "我方");

    internal static string OpponentSideShort() => FormatSimple("OPP", "对手", "對手", "對手");

    internal static string PlayerHeroPill(string shortCode)
    {
        var languageCode = L.CurrentLanguageCode;
        if (LanguageCodeMatcher.IsChinese(languageCode))
            return ResolveChinese($"我方 {shortCode}", $"我方 {shortCode}", $"我方 {shortCode}");

        return $"YOU {shortCode}";
    }

    internal static string ParticipantSummary(
        string playerHero,
        string playerLevel,
        string opponentHero,
        string opponentLevel
    )
    {
        var languageCode = L.CurrentLanguageCode;
        if (LanguageCodeMatcher.IsChinese(languageCode))
        {
            return ResolveChinese(
                $"我方 {playerHero} Lv{playerLevel}  |  对手 {opponentHero} Lv{opponentLevel}",
                $"我方 {playerHero} Lv{playerLevel}  |  對手 {opponentHero} Lv{opponentLevel}",
                $"我方 {playerHero} Lv{playerLevel}  |  對手 {opponentHero} Lv{opponentLevel}"
            );
        }

        return $"YOU {playerHero} Lv{playerLevel}  |  OPP {opponentHero} Lv{opponentLevel}";
    }

    internal static string SnapshotSummary(
        int playerItems,
        int playerSkills,
        int opponentItems,
        int opponentSkills
    )
    {
        var languageCode = L.CurrentLanguageCode;
        if (LanguageCodeMatcher.IsChinese(languageCode))
        {
            return ResolveChinese(
                $"我方 {playerItems} 件物品 · {playerSkills} 个技能  |  对手 {opponentItems} 件物品 · {opponentSkills} 个技能",
                $"我方 {playerItems} 件物品 · {playerSkills} 個技能  |  對手 {opponentItems} 件物品 · {opponentSkills} 個技能",
                $"我方 {playerItems} 件物品 · {playerSkills} 個技能  |  對手 {opponentItems} 件物品 · {opponentSkills} 個技能"
            );
        }

        return $"YOU {playerItems} {Pluralize(playerItems, "item", "items")} · {playerSkills} {Pluralize(playerSkills, "skill", "skills")}  |  OPP {opponentItems} {Pluralize(opponentItems, "item", "items")} · {opponentSkills} {Pluralize(opponentSkills, "skill", "skills")}";
    }

    internal static string LoadedGhostBattles(int count)
    {
        return FormatSimple($"{count} ghost battles loaded.", $"已载入 {count} 场幽灵对战。");
    }

    internal static string GhostHistoryLoadFailed(string details)
    {
        return FormatSimple(
            $"Ghost history load failed: {details}",
            $"幽灵历史加载失败：{details}"
        );
    }

    internal static string GhostSyncUnavailable()
    {
        return FormatSimple("Ghost sync is unavailable right now.", "幽灵同步暂不可用。");
    }

    internal static string GhostSyncFailed(string details)
    {
        return FormatSimple($"Couldn't sync ghost battles: {details}", $"幽灵同步失败：{details}");
    }

    internal static string GhostSyncSucceeded(int count)
    {
        return FormatSimple($"{count} ghost battles synced.", $"已同步 {count} 场幽灵对战。");
    }

    internal static string GhostDeleteUnavailable()
    {
        return FormatSimple(
            "Ghost battles cannot be deleted from this panel yet.",
            "暂时不能在这个面板里删除幽灵战斗。"
        );
    }

    internal static string ReplayActionAlreadyRunning()
    {
        return FormatSimple("Replay is already being prepared.", "正在准备回放。");
    }

    internal static string DownloadingGhostReplay()
    {
        return FormatSimple("Fetching replay data...", "正在获取回放数据...");
    }

    internal static string StartingReplay()
    {
        return FormatSimple("Opening replay...", "正在启动回放...");
    }

    internal static string ReplayFailed(string details)
    {
        return FormatSimple($"Couldn't start replay: {details}", $"回放失败：{details}");
    }

    internal static string GhostSyncAlreadyRunning()
    {
        return FormatSimple("Ghost sync is already in progress.", "幽灵同步进行中。");
    }

    internal static string SyncingGhostBattles()
    {
        return FormatSimple("Syncing ghost battles...", "正在同步幽灵对战...");
    }

    internal static string BattleLoadFailed(string details)
    {
        return FormatSimple($"Couldn't load battles: {details}", $"载入战斗失败：{details}");
    }

    internal static string SelectBattleToReplay()
    {
        return FormatSimple("Select a battle to replay.", "选择一场战斗进行回放。");
    }

    internal static string CombatReplayRuntimeUnavailable()
    {
        return FormatSimple("Combat replay runtime is unavailable.", "战斗回放运行时不可用。");
    }

    internal static string RecordingUnavailable()
    {
        return FormatSimple(
            "FFmpeg not detected; recording is unavailable.",
            "未检测到 FFmpeg，无法录制。",
            "未偵測到 FFmpeg，無法錄製。",
            "未偵測到 FFmpeg，無法錄製。"
        );
    }

    internal static string GhostReplayPayloadUnavailable()
    {
        return FormatSimple(
            "Replay payload for the selected ghost battle is unavailable.",
            "所选幽灵战斗的回放负载不可用。"
        );
    }

    internal static string ReplayRejectedForBattle(string battleId)
    {
        return FormatSimple(
            $"Replay rejected for battle {battleId}.",
            $"战斗 {battleId} 的回放被拒绝。"
        );
    }

    internal static string StartingReplayForBattle(string battleId)
    {
        return FormatSimple($"Starting replay for {battleId}.", $"正在为 {battleId} 启动回放。");
    }

    internal static string CombatReplayDirectoryUnavailable()
    {
        return FormatSimple(
            "Combat replay directory path is unavailable.",
            "战斗回放目录路径不可用。"
        );
    }

    internal static string GhostReplayDownloadUnavailable()
    {
        return FormatSimple("Ghost replay download is unavailable.", "幽灵回放下载不可用。");
    }

    internal static string FailedToDownloadGhostReplay(string details)
    {
        return FormatSimple(
            $"Failed to download ghost replay: {details}",
            $"下载幽灵回放失败：{details}"
        );
    }

    internal static string GhostManifestUnavailable(string battleId)
    {
        return FormatSimple(
            $"Ghost manifest for battle {battleId} is unavailable.",
            $"战斗 {battleId} 的 ghost manifest 不可用。"
        );
    }

    internal static string ReplayPayloadUnavailable(string battleId)
    {
        return FormatSimple(
            $"Replay payload for battle {battleId} is unavailable.",
            $"战斗 {battleId} 的回放负载不可用。"
        );
    }

    internal static string ReplayRejectedForGhostBattle(string battleId)
    {
        return FormatSimple(
            $"Replay rejected for ghost battle {battleId}.",
            $"幽灵战斗 {battleId} 的回放被拒绝。"
        );
    }

    internal static string DownloadedAndStartingReplay(string battleId)
    {
        return FormatSimple(
            $"Downloaded and starting replay for {battleId}.",
            $"已下载并开始回放 {battleId}。"
        );
    }

    internal static string DeletePayloadFailed(string battleId, string details)
    {
        return FormatSimple(
            $"Failed to delete replay payload for battle {battleId}: {details}",
            $"删除战斗 {battleId} 的回放负载失败：{details}"
        );
    }

    internal static string PreviewSelectRunOrBattle()
    {
        return FormatSimple(
            "Select a run or battle to preview recorded cards.",
            "选择一个 run 或战斗以预览记录卡牌。"
        );
    }

    internal static string NoLocallyRenderableCards()
    {
        return FormatSimple(
            "No locally renderable cards were recorded for this selection.",
            "这个选择没有记录可在本地渲染的卡牌。"
        );
    }

    internal static string PreviewRendererInitFailed()
    {
        return FormatSimple("Preview renderer failed to initialize.", "预览渲染器初始化失败。");
    }

    internal static string LoadingPreview()
    {
        return FormatSimple("Loading preview...", "正在加载预览...");
    }

    internal static string PreviewBuildFailed()
    {
        return FormatSimple(
            "Failed to build the selected battle preview.",
            "构建所选战斗预览失败。"
        );
    }
}

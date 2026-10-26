#nullable enable

using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.HistoryPanel;

public static partial class HistoryPanelText
{
    private static readonly LocalizedTextSet UnknownOpponentText = new(
        "Unknown Opponent",
        "未知对手"
    );

    public static string UnknownOpponent() => Resolve(UnknownOpponentText);

    // The challenger's run ended with this battle.
    public static string FinalBattle() => FormatSimple("Final", "终局", "終局");

    // Board ownership. In Runs the lower board is the local player's; in Ghost the ONLY
    // board shown is the challenger's, because ghost payloads stay in recorder perspective
    // (ADR-0002) and NativeBoards renders BuildPlayer, which reads Snapshots.PlayerHand.
    public static string BoardYou() => FormatSimple("You", "你", "你");

    public static string BoardOpponent() => FormatSimple("Opponent", "对手", "對手");

    public static string BoardChallenger() => FormatSimple("Challenger", "挑战者", "挑戰者");

    public static string BattleWon() => FormatSimple("Won", "胜", "勝");

    public static string BattleLost() => FormatSimple("Lost", "负", "負");

    public static string GhostSyncUnavailable()
    {
        return FormatSimple("Ghost sync is unavailable right now.", "幽灵同步暂不可用。");
    }

    public static string GhostSyncFailed(string details)
    {
        return FormatSimple($"Couldn't sync ghost battles: {details}", $"幽灵同步失败：{details}");
    }

    public static string GhostSyncSucceeded(int count, bool discoveryLimitReached)
    {
        var message = FormatSimple(
            $"{count} ghost battles synced.",
            $"已同步 {count} 场幽灵对战。"
        );
        return discoveryLimitReached ? message + " " + GhostDiscoveryHint() : message;
    }

    private static string GhostDiscoveryHint() =>
        FormatSimple(
            "The server returns up to 200 battles from the last 5 days. Saved local history remains browsable.",
            "云端仅返回近 5 天最多 200 场；已保存的本地历史仍可继续浏览。"
        );

    public static string GhostAccountUnavailable() =>
        FormatSimple(
            "Your game account isn't available yet, so Ghost battles cannot be listed. Sign in to The Bazaar and wait for your profile to load in the main menu.",
            "尚未读取到游戏账号，因此无法列出幽灵对战。请登录游戏，等待主菜单的账号资料加载完成。"
        );

    public static string NoSavedGhostBattles() =>
        FormatSimple(
            "No Ghost battles are saved for this account yet.",
            "此账号尚无已保存的幽灵对战。"
        )
        + "\n"
        + GhostDiscoveryHint();

    public static string NoGhostFilterMatches() =>
        FormatSimple(
            "No Ghost battles match these filters. Try a different outcome or day filter.",
            "当前筛选下没有幽灵对战。请放宽胜负或天数筛选。"
        );

    public static string GhostHistoryReadFailed() =>
        FormatSimple(
            "Couldn't read saved Ghost battles. Reopen History to try again.",
            "无法读取已保存的幽灵对战。请重新打开历史记录后重试。"
        );

    // The status bar keeps the failure detail; the list only says its absence proves nothing.
    public static string GhostSyncIncomplete() =>
        FormatSimple(
            "Couldn't sync Ghost battles, so this list may be incomplete. Reopen History to try again.",
            "幽灵同步失败，列表可能不完整。请重新打开历史记录后重试。"
        );

    public static string GhostReplayDownloadRequired() =>
        FormatSimple(
            $"Click \"{DownloadReplay()}\" to view the board.",
            $"点击『{DownloadReplay()}』查看阵容。"
        );

    public static string GhostReplayExpired() =>
        FormatSimple(
            "This replay has expired and has no local copy. Select another battle.",
            "这场回放已过期，本地没有副本。请选择其他对战。"
        );

    public static string GhostLocalReplayUnreadable() =>
        FormatSimple(
            $"The saved replay data can't be read. Click \"{Replay()}\" to try fetching it again.",
            $"无法读取本地回放数据。点击『{Replay()}』尝试重新获取。"
        );

    public static string GhostDeleteUnavailable()
    {
        return FormatSimple(
            "Ghost battles cannot be deleted from this panel yet.",
            "暂时不能在这个面板里删除幽灵战斗。"
        );
    }

    public static string ReplayActionAlreadyRunning()
    {
        return FormatSimple("Replay is already being prepared.", "正在准备回放。");
    }

    public static string DownloadingGhostReplay()
    {
        return FormatSimple("Fetching replay data...", "正在获取回放数据...");
    }

    public static string StartingReplay()
    {
        return FormatSimple("Opening replay...", "正在启动回放...");
    }

    public static string ReplayFailed(string details)
    {
        return FormatSimple($"Couldn't start replay: {details}", $"回放失败：{details}");
    }

    public static string GhostSyncAlreadyRunning()
    {
        return FormatSimple("Ghost sync is already in progress.", "幽灵同步进行中。");
    }

    public static string SyncingGhostBattles()
    {
        return FormatSimple("Syncing ghost battles...", "正在同步幽灵对战...");
    }

    public static string SelectBattleToReplay()
    {
        return FormatSimple("Select a battle to replay.", "选择一场战斗进行回放。");
    }

    public static string CombatReplayRuntimeUnavailable()
    {
        return FormatSimple("Combat replay runtime is unavailable.", "战斗回放运行时不可用。");
    }

    public static string RecordingUnavailable()
    {
        return FormatSimple(
            "Native video recorder is unavailable.",
            "原生视频录制器不可用。",
            "原生影片錄製器不可用。"
        );
    }

    public static string GhostReplayPayloadUnavailable()
    {
        return FormatSimple(
            "This battle has no usable replay data. Select another battle.",
            "这场对战没有可用的回放数据。请选择其他对战。"
        );
    }

    public static string ReplayRejectedForBattle(string battleId)
    {
        return FormatSimple(
            $"Replay rejected for battle {battleId}.",
            $"战斗 {battleId} 的回放被拒绝。"
        );
    }

    public static string StartingReplayForBattle(string battleId)
    {
        return FormatSimple($"Starting replay for {battleId}.", $"正在为 {battleId} 启动回放。");
    }

    public static string CombatReplayDirectoryUnavailable()
    {
        return FormatSimple(
            "Combat replay directory path is unavailable.",
            "战斗回放目录路径不可用。"
        );
    }

    public static string GhostReplayDownloadUnavailable()
    {
        return FormatSimple("Ghost replay download is unavailable.", "幽灵回放下载不可用。");
    }

    public static string FailedToDownloadGhostReplay(string details)
    {
        return FormatSimple(
            $"Failed to download ghost replay: {details}. Try again later.",
            $"下载幽灵回放失败：{details}。请稍后重试。"
        );
    }

    public static string GhostManifestUnavailable(string battleId)
    {
        return FormatSimple(
            $"Ghost manifest for battle {battleId} is unavailable.",
            $"战斗 {battleId} 的 ghost manifest 不可用。"
        );
    }

    public static string ReplayPayloadUnavailable(string battleId)
    {
        return FormatSimple(
            $"Replay payload for battle {battleId} is unavailable.",
            $"战斗 {battleId} 的回放负载不可用。"
        );
    }

    public static string ReplayRejectedForGhostBattle(string battleId)
    {
        return FormatSimple(
            $"Replay rejected for ghost battle {battleId}.",
            $"幽灵战斗 {battleId} 的回放被拒绝。"
        );
    }

    public static string DownloadedAndStartingReplay(string battleId)
    {
        return FormatSimple(
            $"Downloaded and starting replay for {battleId}.",
            $"已下载并开始回放 {battleId}。"
        );
    }

    public static string NoLocallyRenderableCards()
    {
        return FormatSimple(
            "No locally renderable cards were recorded for this selection.",
            "这个选择没有记录可在本地渲染的卡牌。"
        );
    }

    public static string PreviewRendererInitFailed()
    {
        return FormatSimple("Preview renderer failed to initialize.", "预览渲染器初始化失败。");
    }

    public static string LoadingPreview()
    {
        return FormatSimple("Loading preview...", "正在加载预览...");
    }
}

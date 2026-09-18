#nullable enable

using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal static partial class HistoryPanelText
{
    private static readonly LocalizedTextSet UnknownOpponentText = new(
        "Unknown Opponent",
        "未知对手"
    );

    private static readonly LocalizedTextSet SelectedBattleText = new(
        "Selected",
        "当前战斗",
        "當前戰鬥"
    );

    internal static string UnknownOpponent() => Resolve(UnknownOpponentText);

    internal static string SelectedBattle() => Resolve(SelectedBattleText);

    // Board ownership. In Runs the lower board is the local player's; in Ghost the ONLY
    // board shown is the challenger's, because ghost payloads stay in recorder perspective
    // (ADR-0002) and NativeBoards renders BuildPlayer, which reads Snapshots.PlayerHand.
    internal static string BoardYou() => FormatSimple("You", "你", "你");

    internal static string BoardOpponent() => FormatSimple("Opponent", "对手", "對手");

    internal static string BoardChallenger() => FormatSimple("Challenger", "挑战者", "挑戰者");

    internal static string GhostSyncUnavailable()
    {
        return FormatSimple("Ghost sync is unavailable right now.", "幽灵同步暂不可用。");
    }

    internal static string GhostSyncFailed(string details)
    {
        return FormatSimple($"Couldn't sync ghost battles: {details}", $"幽灵同步失败：{details}");
    }

    internal static string GhostSyncSucceeded(int count, bool discoveryLimitReached)
    {
        var message = FormatSimple(
            $"{count} ghost battles synced.",
            $"已同步 {count} 场幽灵对战。"
        );
        return discoveryLimitReached
            ? message
                + " "
                + FormatSimple(
                    "The server returns up to 200 battles from the last 5 days. Saved local history remains browsable.",
                    "云端仅返回近 5 天最多 200 场；已保存的本地历史仍可继续浏览。"
                )
            : message;
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
            "Native video recorder is unavailable.",
            "原生视频录制器不可用。",
            "原生影片錄製器不可用。"
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
}

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BazaarPlusPlus.Game.Settings;
using TheBazaar;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal static class HistoryPanelText
{
    private static readonly Dictionary<string, string> FontAtlasSampleCache = new(
        StringComparer.OrdinalIgnoreCase
    );

    private static readonly LocalizedTextSet TitleText = new(
        "Game History",
        "对局历史",
        "對局歷史",
        "對局歷史"
    );

    private static readonly LocalizedTextSet SubtitleText = new(
        "Review runs, inspect ghost battles, and jump back into any replay you want. Support BazaarPlusPlus at bazaarplusplus.com. -- Xinyu YANG",
        "查看对局、检查幽灵战斗，并快速回放你想看的任意一场。欢迎前往 bazaarplusplus.com 支持 BazaarPlusPlus。-- Xinyu YANG",
        "檢視對局、檢查幽靈戰鬥，並快速重播你想看的任意一場。歡迎前往 bazaarplusplus.com 支持 BazaarPlusPlus。-- Xinyu YANG",
        "檢視對局、檢查幽靈戰鬥，並快速重播你想看的任意一場。歡迎前往 bazaarplusplus.com 支持 BazaarPlusPlus。-- Xinyu YANG"
    );

    private static readonly LocalizedTextSet RunsTabText = new("Runs", "对局", "對局", "對局");

    private static readonly LocalizedTextSet GhostTabText = new("Ghost", "幽灵", "幽靈", "幽靈");

    private static readonly LocalizedTextSet BattlesText = new("Battles", "战斗", "戰鬥", "戰鬥");

    private static readonly LocalizedTextSet CloseText = new("Close", "关闭", "關閉", "關閉");

    private static readonly LocalizedTextSet ReplayText = new("Replay", "回放", "重播", "重播");

    private static readonly LocalizedTextSet ReplayUnavailableText = new("Unavailable", "不可用");

    private static readonly LocalizedTextSet ReplayDisabledInRunText = new("In Run", "对局中禁用");

    private static readonly LocalizedTextSet DownloadReplayText = new(
        "Download Replay",
        "下载回放",
        "下載重播",
        "下載重播"
    );

    private static readonly LocalizedTextSet DeleteText = new("Delete", "删除");

    private static readonly LocalizedTextSet DeleteConfirmText = new("Sure?", "确认？");

    private static readonly LocalizedTextSet WorkingText = new("Working...", "处理中...");

    private static readonly LocalizedTextSet RefreshFinalBuildsText = new(
        "Pull Builds",
        "拉取阵容",
        "拉取陣容",
        "拉取陣容"
    );

    private static readonly LocalizedTextSet RunsSectionSubtitleText = new(
        "Choose one run to see its recorded battles.",
        "选择一个 run 查看记录到的战斗。",
        "選擇一個 run 檢視記錄到的戰鬥。",
        "選擇一個 run 檢視記錄到的戰鬥。"
    );

    private static readonly LocalizedTextSet SelectRunSubtitleText = new(
        "Select a run to inspect its recorded battles.",
        "选择一个 run 查看其记录战斗。",
        "選擇一個 run 檢視其記錄戰鬥。",
        "選擇一個 run 檢視其記錄戰鬥。"
    );

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

    private static readonly LocalizedTextSet NoRunsFoundText = new(
        "No runs found yet.",
        "还没有找到 runs。"
    );

    private static readonly LocalizedTextSet NoGhostBattlesText = new(
        "No ghost battles synced yet.",
        "还没有同步到幽灵战斗。",
        "還沒有同步到幽靈戰鬥。",
        "還沒有同步到幽靈戰鬥。"
    );

    private static readonly LocalizedTextSet SelectRunFirstText = new(
        "Select a run first.",
        "请先选择一个 run。"
    );

    private static readonly LocalizedTextSet NoRecordedBattlesText = new(
        "No recorded battles for this run.",
        "这个 run 没有记录到战斗。"
    );

    private static readonly LocalizedTextSet AllFilterText = new("All", "全部");

    private static readonly LocalizedTextSet IWonFilterText = new("I Won", "我赢了");

    private static readonly LocalizedTextSet ILostFilterText = new("I Lost", "我输了");

    private static readonly LocalizedTextSet DatabasePrefixText = new(
        "DB",
        "数据库",
        "資料庫",
        "資料庫"
    );

    private static readonly LocalizedTextSet DatabaseUnavailableText = new("Unavailable", "不可用");

    private static readonly LocalizedTextSet DatabaseConnectedText = new("Connected", "已连接");

    private static readonly LocalizedTextSet DatabaseMissingText = new("Missing", "缺失");

    private static readonly LocalizedTextSet UnrankedText = new(
        "Normal",
        "普通对局",
        "普通對局",
        "普通對局"
    );

    private static readonly LocalizedTextSet UnknownRunText = new("Unknown Run", "未知 Run");

    private static readonly LocalizedTextSet CompletedText = new(
        "Completed",
        "已完成",
        "已完成",
        "已完成"
    );

    private static readonly LocalizedTextSet AbandonedText = new(
        "Abandoned",
        "已放弃",
        "已放棄",
        "已放棄"
    );

    private static readonly LocalizedTextSet ActiveText = new(
        "Active",
        "进行中",
        "進行中",
        "進行中"
    );

    private static readonly LocalizedTextSet UnknownText = new("Unknown", "未知");

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

    internal static string Title() => Resolve(TitleText);

    internal static string Subtitle() => Resolve(SubtitleText);

    internal static string RunsTab() => Resolve(RunsTabText);

    internal static string GhostTab() => Resolve(GhostTabText);

    internal static string Battles() => Resolve(BattlesText);

    internal static string Close() => Resolve(CloseText);

    internal static string Replay() => Resolve(ReplayText);

    internal static string ReplayUnavailable() => Resolve(ReplayUnavailableText);

    internal static string ReplayDisabledInRun() => Resolve(ReplayDisabledInRunText);

    internal static string DownloadReplay() => Resolve(DownloadReplayText);

    internal static string Delete() => Resolve(DeleteText);

    internal static string DeleteConfirm() => Resolve(DeleteConfirmText);

    internal static string Working() => Resolve(WorkingText);

    internal static string RefreshFinalBuilds() => Resolve(RefreshFinalBuildsText);

    internal static string RunsSectionSubtitle() => Resolve(RunsSectionSubtitleText);

    internal static string SelectRunSubtitle() => Resolve(SelectRunSubtitleText);

    internal static string NoBattleSelected() => Resolve(NoBattleSelectedText);

    internal static string UnknownOpponent() => Resolve(UnknownOpponentText);

    internal static string SelectBattleForFooter() => Resolve(SelectBattleForFooterText);

    internal static string PreviewUnavailablePrefix() => Resolve(PreviewUnavailablePrefixText);

    internal static string PreviewSelectBattle() => Resolve(PreviewSelectBattleText);

    internal static string NoRunsFound() => Resolve(NoRunsFoundText);

    internal static string NoGhostBattles() => Resolve(NoGhostBattlesText);

    internal static string SelectRunFirst() => Resolve(SelectRunFirstText);

    internal static string NoRecordedBattles() => Resolve(NoRecordedBattlesText);

    internal static string FilterAll() => Resolve(AllFilterText);

    internal static string FilterIWon() => Resolve(IWonFilterText);

    internal static string FilterILost() => Resolve(ILostFilterText);

    internal static string DatabaseUnavailable() => Resolve(DatabaseUnavailableText);

    internal static string DatabaseConnected() => Resolve(DatabaseConnectedText);

    internal static string DatabaseMissing() => Resolve(DatabaseMissingText);

    internal static string Unranked() => Resolve(UnrankedText);

    internal static string UnknownRun() => Resolve(UnknownRunText);

    internal static string Completed() => Resolve(CompletedText);

    internal static string Abandoned() => Resolve(AbandonedText);

    internal static string Active() => Resolve(ActiveText);

    internal static string Unknown() => Resolve(UnknownText);

    internal static string Win() => Resolve(WinText);

    internal static string Loss() => Resolve(LossText);

    internal static string GhostOpponentEliminatedNotice() =>
        Resolve(GhostOpponentEliminatedNoticeText);

    internal static string GhostOpponentEliminatedShort() =>
        Resolve(GhostOpponentEliminatedShortText);

    internal static string CountGhost(int count) => FormatCount(count, GhostTab());

    internal static string CountRuns(int count) => FormatCount(count, RunsTab());

    internal static string CountBattles(int count) => FormatCount(count, Battles());

    internal static string DatabaseChip(string status) => $"{Resolve(DatabasePrefixText)} {status}";

    internal static string RunBattles(int count) => FormatCount(count, Battles());

    internal static string StatHealthShort() => FormatSimple("HP", "生命");

    internal static string StatPrestigeShort() => FormatSimple("PRE", "声望", "聲望", "聲望");

    internal static string StatLevelShort() => FormatSimple("LVL", "等级", "等級", "等級");

    internal static string StatIncomeShort() => FormatSimple("INC", "收入", "收入", "收入");

    internal static string StatGoldShort() => FormatSimple("GLD", "金币", "金幣", "金幣");

    internal static string PlayerSideShort() => FormatSimple("YOU", "我方", "我方", "我方");

    internal static string OpponentSideShort() => FormatSimple("OPP", "对手", "對手", "對手");

    internal static string HourBadge(int? hour) =>
        FormatSimple(
            hour.HasValue ? $"H{hour.Value}" : "H?",
            hour.HasValue ? $"{hour.Value}时" : "?时",
            hour.HasValue ? $"{hour.Value}時" : "?時",
            hour.HasValue ? $"{hour.Value}時" : "?時"
        );

    internal static string DayBadge(int? day) =>
        FormatSimple(
            day.HasValue ? $"D{day.Value}" : "D?",
            day.HasValue ? $"{day.Value}天" : "?天",
            day.HasValue ? $"{day.Value}天" : "?天",
            day.HasValue ? $"{day.Value}天" : "?天"
        );

    internal static string DayHourBadge(int? day, int? hour) =>
        $"{DayBadge(day)} {HourBadge(hour)}";

    internal static string RunOutcomeBubbleLabel(RunOutcomeTier tier)
    {
        return tier switch
        {
            RunOutcomeTier.Diamond => FormatSimple("DIA", "钻石"),
            RunOutcomeTier.Gold => FormatSimple("GLD", "黄金", "黃金", "黃金"),
            RunOutcomeTier.Silver => FormatSimple("SLV", "白银", "白銀", "白銀"),
            RunOutcomeTier.Bronze => FormatSimple("BRZ", "青铜", "青銅", "青銅"),
            _ => FormatSimple("MIS", "惨淡", "慘淡", "慘淡"),
        };
    }

    internal static string BoardSummary(int items, int skills)
    {
        var languageCode = GetLanguageCode();
        if (LanguageCodeMatcher.IsChinese(languageCode))
            return ResolveChinese(
                $"{items} 物品 · {skills} 技能",
                $"{items} 物品 · {skills} 技能",
                $"{items} 物品 · {skills} 技能"
            );

        return $"{items} {Pluralize(items, "item", "items")} · {skills} {Pluralize(skills, "skill", "skills")}";
    }

    internal static string RunRecord(int wins, int losses)
    {
        var languageCode = GetLanguageCode();
        if (LanguageCodeMatcher.IsChinese(languageCode))
            return ResolveChinese(
                $"{wins}胜 - {losses}负",
                $"{wins}勝 - {losses}負",
                $"{wins}勝 - {losses}負"
            );

        return $"{wins}W - {losses}L";
    }

    internal static string RankLabel(string? rank, int? rating = null)
    {
        if (string.IsNullOrWhiteSpace(rank))
            return Unranked();

        var normalized = rank.Trim();
        if (string.Equals(normalized, "Legendary", StringComparison.OrdinalIgnoreCase))
            return rating?.ToString() ?? FormatSimple("LEG", "传说", "傳說", "傳說");

        if (LanguageCodeMatcher.IsChinese(GetLanguageCode()))
        {
            return normalized switch
            {
                "Bronze" => FormatSimple("BRZ", "青铜", "青銅", "青銅"),
                "Silver" => FormatSimple("SLV", "白银", "白銀", "白銀"),
                "Gold" => FormatSimple("GLD", "黄金", "黃金", "黃金"),
                "Diamond" => FormatSimple("DIA", "钻石", "鑽石", "鑽石"),
                _ => normalized,
            };
        }

        return normalized.ToUpperInvariant();
    }

    internal static string RunWins(int count)
    {
        var languageCode = GetLanguageCode();
        if (LanguageCodeMatcher.IsChinese(languageCode))
            return ResolveChinese($"{count} 胜", $"{count} 勝", $"{count} 勝");

        return $"{count} wins";
    }

    internal static string PlayerHeroPill(string shortCode)
    {
        var languageCode = GetLanguageCode();
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
        var languageCode = GetLanguageCode();
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
        var languageCode = GetLanguageCode();
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

    internal static string RunLogDatabasePathUnavailable()
    {
        return FormatSimple("History data is unavailable.", "对局数据暂不可用。");
    }

    internal static string LoadedRuns(int count)
    {
        return FormatSimple($"{count} runs loaded.", $"已载入 {count} 场对局。");
    }

    internal static string DatabaseFileMissing()
    {
        return FormatSimple("No history data yet.", "暂未找到对局数据。");
    }

    internal static string HistoryLoadFailed(string details)
    {
        return FormatSimple($"Couldn't load history: {details}", $"载入对局失败：{details}");
    }

    internal static string CurrentPlayerAccountUnavailable()
    {
        return FormatSimple("Current player account is unavailable.", "当前玩家账号不可用。");
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

    internal static string PanelUnavailable()
    {
        return FormatSimple("History panel is unavailable.", "历史面板不可用。");
    }

    internal static string GhostDeleteUnavailable()
    {
        return FormatSimple(
            "Ghost battles cannot be deleted from this panel yet.",
            "暂时不能在这个面板里删除幽灵战斗。"
        );
    }

    internal static string SelectRunToDelete()
    {
        return FormatSimple("Select a run to delete.", "请选择要删除的对局。");
    }

    internal static string ActiveRunDeleteUnavailable()
    {
        return FormatSimple("Active runs cannot be deleted.", "进行中的 run 不能删除。");
    }

    internal static string CurrentGameplayRunDeleteUnavailable()
    {
        return FormatSimple(
            "The currently active gameplay run cannot be deleted.",
            "当前正在进行的对局 run 不能删除。"
        );
    }

    internal static string RunLogRepositoryUnavailable()
    {
        return FormatSimple("Run log repository is unavailable.", "Run log 仓库不可用。");
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

    internal static string DeleteRunConfirm(string shortRunId)
    {
        return FormatSimple(
            $"Press Delete again within 5s to remove {shortRunId}.",
            $"请在 5 秒内再次点击删除，以移除 {shortRunId}。"
        );
    }

    internal static string RunDeleteFailed(string details)
    {
        return FormatSimple($"Couldn't delete run: {details}", $"删除对局失败：{details}");
    }

    internal static string DeletedRun(string shortRunId, int battleCount)
    {
        if (battleCount > 0)
        {
            return FormatSimple(
                $"Removed run {shortRunId} and cleaned {battleCount} battle records.",
                $"已删除对局 {shortRunId}，并清理 {battleCount} 条战斗记录。"
            );
        }

        return FormatSimple($"Removed run {shortRunId}.", $"已删除对局 {shortRunId}。");
    }

    internal static string GhostSyncAlreadyRunning()
    {
        return FormatSimple("Ghost sync is already in progress.", "幽灵同步进行中。");
    }

    internal static string FinalBuildRefreshAlreadyRunning()
    {
        return FormatSimple("Build pull is already in progress.", "阵容拉取进行中。");
    }

    internal static string SyncingGhostBattles()
    {
        return FormatSimple("Syncing ghost battles...", "正在同步幽灵对战...");
    }

    internal static string RefreshingFinalBuilds()
    {
        return FormatSimple("Pulling ten-win builds...", "正在拉取十胜阵容...");
    }

    internal static string FinalBuildRefreshSucceeded()
    {
        return FormatSimple("Ten-win builds updated.", "十胜阵容已更新。");
    }

    internal static string FinalBuildRefreshFailed(string details)
    {
        return FormatSimple(
            $"Couldn't pull ten-win builds: {details}",
            $"拉取十胜阵容失败：{details}"
        );
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

    internal static string PreviewTuneStatus(string summary)
    {
        return FormatSimple("Preview tune: " + summary, "预览调参：" + summary);
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

    internal static string FontProbeSample()
    {
        return FormatSimple("Game History Replay", "对局历史回放");
    }

    internal static string FontAtlasSample()
    {
        var languageCode = GetLanguageCode();
        if (FontAtlasSampleCache.TryGetValue(languageCode, out var cachedSample))
            return cachedSample;

        var parts = new List<string>();
        foreach (
            var field in typeof(HistoryPanelText).GetFields(
                BindingFlags.Static | BindingFlags.NonPublic
            )
        )
        {
            if (field.FieldType != typeof(LocalizedTextSet))
                continue;

            if (field.GetValue(null) is not LocalizedTextSet set)
                continue;

            var resolved = set.Resolve(languageCode);
            if (!string.IsNullOrWhiteSpace(resolved))
                parts.Add(resolved);
        }

        parts.Add(
            "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz -_:/?()[]{}%+,.!|#"
        );

        var deduped = new HashSet<char>();
        foreach (var part in parts)
        {
            foreach (var character in part)
            {
                if (!char.IsControl(character))
                    deduped.Add(character);
            }
        }

        var sample = deduped.Count > 0 ? new string(deduped.ToArray()) : FontProbeSample();
        FontAtlasSampleCache[languageCode] = sample;
        return sample;
    }

    internal static string PreviewBuildFailed()
    {
        return FormatSimple(
            "Failed to build the selected battle preview.",
            "构建所选战斗预览失败。"
        );
    }

    internal static string PreviewTuneHelp()
    {
        return FormatSimple(
            "Ctrl+Left/Right board spacing, Ctrl+[ / ] card spacing, Ctrl+Up/Down zoom, Ctrl+PgUp/PgDn vertical, Ctrl+Q/E or Home/End card width, Ctrl+Alt+Q/E or Home/End card height, Ctrl+-/= FOV, Ctrl+Backspace reset.",
            "Ctrl+左右 调整棋盘间距，Ctrl+[ / ] 调整卡牌间距，Ctrl+上下 调整缩放，Ctrl+PgUp/PgDn 调整垂直位置，Ctrl+Q/E 或 Home/End 调整卡牌宽度，Ctrl+Alt+Q/E 或 Home/End 调整卡牌高度，Ctrl+-/= 调整视野，Ctrl+Backspace 重置。"
        );
    }

    private static string Resolve(LocalizedTextSet set) => set.Resolve(GetLanguageCode());

    private static string FormatCount(int count, string noun)
    {
        var languageCode = GetLanguageCode();
        if (LanguageCodeMatcher.IsChinese(languageCode))
            return $"{noun} {count}";

        return $"{count} {noun}";
    }

    private static string FormatSimple(string english, string chineseMainland)
    {
        return FormatSimple(english, chineseMainland, null, null);
    }

    private static string FormatSimple(
        string english,
        string chineseMainland,
        string? chineseTaiwan,
        string? chineseHongKong
    )
    {
        var languageCode = GetLanguageCode();
        if (LanguageCodeMatcher.IsChinese(languageCode))
        {
            return BppChineseLocalization.ResolveChineseText(
                chineseMainland,
                chineseTaiwan,
                chineseHongKong
            );
        }

        return english;
    }

    private static string ResolveChinese(
        string chineseMainland,
        string? chineseTaiwan,
        string? chineseHongKong
    )
    {
        return BppChineseLocalization.ResolveChineseText(
            chineseMainland,
            chineseTaiwan,
            chineseHongKong
        );
    }

    private static string Pluralize(int count, string singular, string plural)
    {
        return count == 1 ? singular : plural;
    }

    private static string GetLanguageCode()
    {
        try
        {
            return PlayerPreferences.Data.LanguageCode ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}

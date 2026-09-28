#nullable enable
using System.Reflection;
using BazaarPlusPlus.Localization;

internal static class GhostMessageTests
{
    public static void Run(Assembly assembly)
    {
        var decisions = Type("HistoryPanelDecisions");
        var stateType = Type("HistoryPanelState");
        var battleType = Type("Data.HistoryBattleRecord");
        try
        {
            foreach (var language in new[] { "en", "zh-Hans" })
            {
                L.Install(new Language(language), new Mainland());
                var chinese = language == "zh-Hans";
                var state = Activator.CreateInstance(stateType)!;
                Set(state, "SectionMode", Enum.Parse(Type("HistorySectionMode"), "Ghost"));
                Set(state, "PageLoading", true);
                Contains(
                    Empty(),
                    chinese ? "账号资料加载完成" : "profile to load",
                    "Missing identity must explain how to load the account, even while loading."
                );
                Contains(
                    Preview(null),
                    chinese ? "无法列出幽灵" : "cannot be listed",
                    "An empty board must explain account filtering too."
                );
                Set(state, "CachedAccountId", "local-account");
                Contains(
                    Empty(),
                    chinese ? "加载" : "Loading",
                    "Pending reads must not claim there are no records."
                );
                Set(state, "PageLoading", false);
                Set(state, "PageLoadFailed", true);
                Contains(
                    Empty(),
                    chinese ? "重新打开" : "Reopen History",
                    "Read failures must offer recovery."
                );
                Set(state, "PageLoadFailed", false);
                Set(state, "GhostSyncInProgress", true);
                Contains(
                    Empty(),
                    chinese ? "同步" : "Syncing",
                    "Pending discovery must be distinct from an empty result."
                );
                Set(state, "GhostSyncInProgress", false);
                Contains(
                    Empty(),
                    chinese ? "5 天" : "5 days",
                    "An empty account must explain the remote discovery window."
                );
                Set(state, "GhostBattleFilter", Enum.Parse(Type("GhostBattleFilter"), "IWon"));
                Contains(
                    Empty(),
                    chinese ? "筛选" : "filter",
                    "Outcome filters must suggest widening the filter."
                );
                Set(state, "GhostBattleFilter", Enum.Parse(Type("GhostBattleFilter"), "All"));
                Set(state, "GhostDayMin10", true);
                Contains(
                    Empty(),
                    chinese ? "筛选" : "filter",
                    "Day filters must suggest widening the filter."
                );
                Set(state, "GhostDayMin10", false);

                var remote = Battle("remote_available", true, false);
                Contains(
                    Preview(remote),
                    chinese ? "点击『下载回放』查看阵容" : "Click \"Download Replay\"",
                    "Undownloaded boards must invite download."
                );
                var rows = Array.CreateInstance(battleType, 1);
                rows.SetValue(remote, 0);
                var page = Activator.CreateInstance(
                    Type("Storage.HistoryPage`1").MakeGenericType(battleType),
                    rows,
                    null,
                    null,
                    false,
                    false
                )!;
                Set(state, "GhostPage", page);
                Equal(Empty(), "", "Nonempty lists must hide the empty label.");
                Set(state, "ReplayActionInProgress", true);
                Set(state, "ReplayActionBattleId", "battle");
                Contains(
                    Preview(remote),
                    chinese ? "获取" : "Fetching",
                    "Downloads must show progress for their own battle."
                );
                Set(state, "ReplayActionBattleId", "other-battle");
                Contains(
                    Preview(remote),
                    chinese ? "点击" : "Click",
                    "Other battles must not inherit progress."
                );
                Set(state, "ReplayActionInProgress", false);
                Set(state, "ReplayFailureMessage", "network failure with retry guidance");
                Contains(
                    Preview(remote),
                    chinese ? "点击" : "Click",
                    "Other battles must not inherit failures."
                );
                Set(state, "ReplayActionBattleId", "battle");
                Equal(
                    Preview(remote),
                    "network failure with retry guidance",
                    "Transient failure details must persist for the selected battle."
                );
                Contains(
                    Preview(Battle("expired", false, false)),
                    chinese ? "已过期" : "expired",
                    "Expiry must supersede a transient failure."
                );
                Contains(
                    Preview(Battle("unavailable_payload", false, false)),
                    chinese ? "没有可用" : "no usable",
                    "Unavailable payloads must be specific."
                );
                Set(state, "ReplayFailureMessage", null);
                var saved = Battle("local_ready", true, true);
                Set(state, "DetailBattleId", "battle");
                Set(
                    state,
                    "ReplayFailureMessage",
                    "network failure while replacing missing local data"
                );
                Equal(
                    Preview(saved),
                    "network failure while replacing missing local data",
                    "Re-fetching a missing saved replay must also expose download failures."
                );
                Set(state, "ReplayFailureMessage", null);
                Contains(
                    Preview(saved),
                    chinese ? "重新获取" : "fetching it again",
                    "A missing local payload must offer a recovery action."
                );
                Set(
                    state,
                    "DetailSnapshots",
                    Activator.CreateInstance(
                        assembly.GetType("BazaarPlusPlus.Game.PvpBattles.PvpBattleSnapshots")!
                    )
                );
                Equal(
                    Preview(saved),
                    null,
                    "Saved replay boards, including genuine empty captures, must preserve native rendering results."
                );
                Set(state, "DetailFailed", true);
                Contains(
                    Preview(saved),
                    chinese ? "重新获取" : "fetching it again",
                    "Unreadable local data must not blame the native renderer."
                );
                Set(state, "DetailLoading", true);
                Contains(
                    Preview(saved),
                    chinese ? "加载" : "Loading",
                    "A pending read must not present a stale failure."
                );
                Set(state, "DetailLoading", false);
                Set(state, "SectionMode", Enum.Parse(Type("HistorySectionMode"), "Runs"));
                Equal(Empty(), "", "Run lists must not receive Ghost guidance.");
                Contains(
                    Preview(Battle(null, true, true, "Local")),
                    chinese ? "初始化" : "initialize",
                    "Local-run renderer failures must keep their existing message."
                );
                Set(state, "DetailFailed", false);
                Equal(
                    Preview(Battle(null, true, true, "Local")),
                    null,
                    "Local boards must keep native rendering messages."
                );

                Contains(
                    Failure("ghost_replay_expired", "GhostDownloadFailed"),
                    chinese ? "已过期" : "expired",
                    "Expired downloads must not be generic errors."
                );
                foreach (var reason in new[] { "GhostArtifactInvalid", "GhostBattleMismatch" })
                    Contains(
                        Failure("ghost_bundle_invalid", reason),
                        chinese ? "没有可用" : "no usable",
                        "Invalid downloaded data must explain unavailability."
                    );
                Contains(
                    Failure("ghost_replay_unavailable_payload", "GhostDownloadFailed"),
                    chinese ? "没有可用" : "no usable",
                    "Persisted unavailable state must remain specific."
                );
                var network = Failure("network_unavailable", "GhostDownloadFailed");
                Contains(
                    network,
                    "network_unavailable",
                    "Transient failures must retain their diagnostic code."
                );
                Contains(
                    network,
                    chinese ? "稍后重试" : "Try again later",
                    "Transient failures must offer retry guidance."
                );

                string? Empty() => Call("GhostArchiveEmptyMessage", state);
                string? Preview(object? battle) => Call("PreviewStatusOverride", state, battle);
                string? Failure(string error, string reason) =>
                    Call(
                        "GhostDownloadFailureMessage",
                        error,
                        Enum.Parse(Type("HistoryPanelReplayReasonCode"), reason)
                    );
            }
        }
        finally
        {
            L.Install(new Language("en"), new Mainland());
        }

        Type Type(string name) =>
            assembly.GetType("BazaarPlusPlus.Game.HistoryPanel." + name, true)!;
        string? Call(string name, params object?[] args) =>
            (string?)decisions.GetMethod(name)!.Invoke(null, args);
        object Battle(string? replayState, bool available, bool downloaded, string source = "Ghost")
        {
            var value = Activator.CreateInstance(
                battleType,
                "battle",
                "run",
                DateTimeOffset.UtcNow.AddDays(-40),
                1,
                1,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                Activator.CreateInstance(Type("Data.HistoryBattleSnapshotCounts")),
                false,
                Enum.Parse(Type("Data.HistoryBattleSource"), source),
                available,
                downloaded
            )!;
            Set(value, "GhostReplayState", replayState);
            return value;
        }
    }

    private static void Set(object target, string property, object? value) =>
        target.GetType().GetProperty(property)!.SetValue(target, value);

    private static void Contains(string? actual, string expected, string message)
    {
        if (actual?.Contains(expected, StringComparison.Ordinal) != true)
            throw new InvalidOperationException(
                $"{message} Expected '{expected}', got '{actual}'."
            );
    }

    private static void Equal(string? actual, string? expected, string message)
    {
        if (actual != expected)
            throw new InvalidOperationException(
                $"{message} Expected '{expected}', got '{actual}'."
            );
    }

    private sealed class Language(string code) : ILanguageProvider
    {
        public string CurrentLanguageCode => code;
    }

    private sealed class Mainland : ILocaleModeProvider
    {
        public BppChineseLocaleMode CurrentMode => BppChineseLocaleMode.Mainland;
    }
}

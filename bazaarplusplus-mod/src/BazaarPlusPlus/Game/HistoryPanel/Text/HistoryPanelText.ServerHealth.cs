#nullable enable

using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal static partial class HistoryPanelText
{
    private static readonly LocalizedTextSet CheckServerHealthText = new(
        "Check Server",
        "检测连通",
        "檢測連通",
        "檢測連通"
    );

    private static readonly LocalizedTextSet CheckingServerHealthText = new(
        "Checking...",
        "检测中...",
        "檢測中...",
        "檢測中..."
    );

    private static readonly LocalizedTextSet CheckingServerConnectivityText = new(
        "Checking game-server connectivity...",
        "正在检测游戏与服务器联通性...",
        "正在檢測遊戲與伺服器連通性...",
        "正在檢測遊戲與伺服器連通性..."
    );

    private static readonly LocalizedTextSet ServerHealthUnavailableText = new(
        "Connectivity check is unavailable right now.",
        "连通性检测暂不可用。",
        "連通性檢測暫不可用。",
        "連通性檢測暫不可用。"
    );

    private static readonly LocalizedTextSet ServerHealthAlreadyRunningText = new(
        "Connectivity check is already in progress.",
        "连通性检测进行中。",
        "連通性檢測進行中。",
        "連通性檢測進行中。"
    );


    private static readonly LocalizedTextSet DatabasePrefixText = new(
        "DB",
        "数据库",
        "資料庫",
        "資料庫"
    );

    private static readonly LocalizedTextSet DatabaseUnavailableText = new("Unavailable", "不可用");

    private static readonly LocalizedTextSet DatabaseConnectedText = new("Connected", "已连接");

    private static readonly LocalizedTextSet DatabaseMissingText = new("Missing", "缺失");

    internal static string CheckServerHealth() => Resolve(CheckServerHealthText);

    internal static string CheckingServerHealth() => Resolve(CheckingServerHealthText);

    internal static string CheckingServerConnectivity() => Resolve(CheckingServerConnectivityText);

    internal static string ServerHealthUnavailable() => Resolve(ServerHealthUnavailableText);

    internal static string ServerHealthAlreadyRunning() => Resolve(ServerHealthAlreadyRunningText);

    internal static string DatabaseUnavailable() => Resolve(DatabaseUnavailableText);

    internal static string DatabaseConnected() => Resolve(DatabaseConnectedText);

    internal static string DatabaseMissing() => Resolve(DatabaseMissingText);

    internal static string DatabaseChip(string status) => $"{Resolve(DatabasePrefixText)} {status}";

    internal static string RunLogDatabasePathUnavailable()
    {
        return FormatSimple("History data is unavailable.", "对局数据暂不可用。");
    }

    internal static string LoadedRuns(int count)
    {
        return FormatSimple($"{count} runs loaded.", $"已载入 {count} 场对局。");
    }

    internal static string ServerHealthConnected(long roundTripMilliseconds)
    {
        return FormatSimple(
            $"Game and server connected in {roundTripMilliseconds} ms.",
            $"游戏与服务器已联通，耗时 {roundTripMilliseconds} ms。",
            $"遊戲與伺服器已連通，耗時 {roundTripMilliseconds} ms。",
            $"遊戲與伺服器已連通，耗時 {roundTripMilliseconds} ms。"
        );
    }

    internal static string ServerHealthFailed(long roundTripMilliseconds, string details)
    {
        return FormatSimple(
            $"Game-server check failed after {roundTripMilliseconds} ms: {details}",
            $"游戏与服务器联通性检测失败，耗时 {roundTripMilliseconds} ms：{details}",
            $"遊戲與伺服器連通性檢測失敗，耗時 {roundTripMilliseconds} ms：{details}",
            $"遊戲與伺服器連通性檢測失敗，耗時 {roundTripMilliseconds} ms：{details}"
        );
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

    internal static string PanelUnavailable()
    {
        return FormatSimple("History panel is unavailable.", "历史面板不可用。");
    }
}

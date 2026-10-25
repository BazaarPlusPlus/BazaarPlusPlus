#nullable enable

using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.HistoryPanel;

public static partial class HistoryPanelText
{
    private static readonly LocalizedTextSet FilterDayMin10Text = new("≥10d", "≥10天", "≥10天");

    private static readonly LocalizedTextSet UnrankedText = new("Normal", "普通对局", "普通對局");

    private static readonly LocalizedTextSet UnknownRunText = new("Unknown Run", "未知 Run");

    private static readonly LocalizedTextSet UnknownText = new("Unknown", "未知");

    public static string FilterDayMin10() => Resolve(FilterDayMin10Text);

    public static string Unranked() => Resolve(UnrankedText);

    public static string UnknownRun() => Resolve(UnknownRunText);

    public static string Unknown() => Resolve(UnknownText);

    // "34 min", "1 h 20 min", "2 d 10 h": a run left open overnight stays readable.
    public static string Duration(TimeSpan span)
    {
        var minutes = Math.Max(0, (int)span.TotalMinutes);
        if (minutes < 60)
            return FormatSimple($"{minutes} min", $"{minutes} 分钟", $"{minutes} 分鐘");
        var hours = minutes / 60;
        if (hours < 24)
        {
            var rest = minutes % 60;
            return rest == 0
                ? FormatSimple($"{hours} h", $"{hours} 小时", $"{hours} 小時")
                : FormatSimple(
                    $"{hours} h {rest} min",
                    $"{hours} 小时 {rest} 分",
                    $"{hours} 小時 {rest} 分"
                );
        }
        var days = hours / 24;
        var restHours = hours % 24;
        return restHours == 0
            ? FormatSimple($"{days} d", $"{days} 天", $"{days} 天")
            : FormatSimple(
                $"{days} d {restHours} h",
                $"{days} 天 {restHours} 小时",
                $"{days} 天 {restHours} 小時"
            );
    }

    public static string NoRuns() =>
        FormatSimple("No runs recorded yet.", "还没有对局记录。", "還沒有對局紀錄。");

    public static string NoBattlesInRun() =>
        FormatSimple(
            "No battles were recorded in this run.",
            "这一局没有记录到战斗。",
            "這一局沒有記錄到戰鬥。"
        );

    /// <summary>A stored rank under its localized name; an unknown rank stays as stored.</summary>
    public static string RankName(string rank) =>
        rank.Trim().ToLowerInvariant() switch
        {
            "bronze" => FormatSimple("Bronze", "青铜", "青銅"),
            "silver" => FormatSimple("Silver", "白银", "白銀"),
            "gold" => FormatSimple("Gold", "黄金", "黃金"),
            "diamond" => FormatSimple("Diamond", "钻石", "鑽石"),
            "legendary" or "legend" => FormatSimple("Legendary", "传奇", "傳奇"),
            _ => rank.Trim(),
        };

    public static string DayBadge(int? day) =>
        FormatSimple(
            day.HasValue ? $"D{day.Value}" : "D?",
            day.HasValue ? $"{day.Value}天" : "?天",
            day.HasValue ? $"{day.Value}天" : "?天"
        );

    public static string SelectRunToDelete()
    {
        return FormatSimple("Select a run to delete.", "请选择要删除的对局。");
    }

    public static string ActiveRunDeleteUnavailable()
    {
        return FormatSimple("Active runs cannot be deleted.", "进行中的 run 不能删除。");
    }

    public static string CurrentGameplayRunDeleteUnavailable()
    {
        return FormatSimple(
            "The currently active gameplay run cannot be deleted.",
            "当前正在进行的对局 run 不能删除。"
        );
    }

    public static string RunLogRepositoryUnavailable()
    {
        return FormatSimple("Run log repository is unavailable.", "Run log 仓库不可用。");
    }

    // A host that records no video (IReplayRecordingAvailability.Supported false, Android) has no
    // MP4 to keep, so its delete lines do not mention one.
    public static string DeleteRunConfirm(string shortRunId, bool recordingSupported)
    {
        return recordingSupported
            ? FormatSimple(
                $"Press Delete again within 5s to remove {shortRunId}. Completed MP4 recordings will be kept.",
                $"请在 5 秒内再次点击删除，以移除 {shortRunId}。已完成的 MP4 录像会保留。"
            )
            : FormatSimple(
                $"Press Delete again within 5s to remove {shortRunId}.",
                $"请在 5 秒内再次点击删除，以移除 {shortRunId}。"
            );
    }

    // A Seal-Eligible Run, or one whose seal job is still waiting (BundleQueueStore.ProtectedRunCondition).
    public static string SealProtectedRunDeleteUnavailable()
    {
        return FormatSimple(
            "This ranked run is waiting to upload to BazaarDB and cannot be deleted yet.",
            "这局排位还在等待上传 BazaarDB，暂时不能删除。"
        );
    }

    public static string RunDeleteFailed(string details)
    {
        return FormatSimple($"Couldn't delete run: {details}", $"删除对局失败：{details}");
    }

    public static string DeletedRun(string shortRunId, int battleCount, bool recordingSupported)
    {
        if (battleCount > 0)
        {
            return recordingSupported
                ? FormatSimple(
                    $"Removed run {shortRunId} and cleaned {battleCount} battle records. Completed MP4 recordings were kept.",
                    $"已删除对局 {shortRunId}，并清理 {battleCount} 条战斗记录。已完成的 MP4 录像已保留。"
                )
                : FormatSimple(
                    $"Removed run {shortRunId} and cleaned {battleCount} battle records.",
                    $"已删除对局 {shortRunId}，并清理 {battleCount} 条战斗记录。"
                );
        }

        return recordingSupported
            ? FormatSimple(
                $"Removed run {shortRunId}. Completed MP4 recordings were kept.",
                $"已删除对局 {shortRunId}。已完成的 MP4 录像已保留。"
            )
            : FormatSimple($"Removed run {shortRunId}.", $"已删除对局 {shortRunId}。");
    }
}

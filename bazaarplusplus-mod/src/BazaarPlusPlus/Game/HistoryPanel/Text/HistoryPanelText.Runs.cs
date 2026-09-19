#nullable enable

using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal static partial class HistoryPanelText
{
    private static readonly LocalizedTextSet FilterDayMin10Text = new("≥10d", "≥10天", "≥10天");

    private static readonly LocalizedTextSet UnrankedText = new("Normal", "普通对局", "普通對局");

    private static readonly LocalizedTextSet UnknownRunText = new("Unknown Run", "未知 Run");

    private static readonly LocalizedTextSet UnknownText = new("Unknown", "未知");

    internal static string FilterDayMin10() => Resolve(FilterDayMin10Text);

    internal static string Unranked() => Resolve(UnrankedText);

    internal static string UnknownRun() => Resolve(UnknownRunText);

    internal static string Unknown() => Resolve(UnknownText);

    internal static string DurationMinutes(int minutes) =>
        FormatSimple($"{minutes} min", $"{minutes} 分钟", $"{minutes} 分鐘");

    internal static string DayBadge(int? day) =>
        FormatSimple(
            day.HasValue ? $"D{day.Value}" : "D?",
            day.HasValue ? $"{day.Value}天" : "?天",
            day.HasValue ? $"{day.Value}天" : "?天"
        );

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

    internal static string DeleteRunConfirm(string shortRunId)
    {
        return FormatSimple(
            $"Press Delete again within 5s to remove {shortRunId}. Completed MP4 recordings will be kept.",
            $"请在 5 秒内再次点击删除，以移除 {shortRunId}。已完成的 MP4 录像会保留。"
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
                $"Removed run {shortRunId} and cleaned {battleCount} battle records. Completed MP4 recordings were kept.",
                $"已删除对局 {shortRunId}，并清理 {battleCount} 条战斗记录。已完成的 MP4 录像已保留。"
            );
        }

        return FormatSimple(
            $"Removed run {shortRunId}. Completed MP4 recordings were kept.",
            $"已删除对局 {shortRunId}。已完成的 MP4 录像已保留。"
        );
    }
}

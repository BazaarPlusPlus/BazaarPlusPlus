#nullable enable
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.CombatReplay;

internal static class CurrentReplayRecordingText
{
    internal static string Tooltip(CurrentReplayRecordingSnapshot snapshot)
    {
        var text = snapshot.Phase switch
        {
            CurrentReplayRecordingPhase.AwaitingBattlePersistence => T(
                "Saving",
                "正在保存",
                "正在儲存"
            ),
            CurrentReplayRecordingPhase.Preparing => T(
                "Preparing recording",
                "正在准备录制",
                "正在準備錄製"
            ),
            CurrentReplayRecordingPhase.Ready => T("Record video", "录制视频", "錄製影片"),
            CurrentReplayRecordingPhase.Armed => T(
                "Starting recording",
                "正在开始录制",
                "正在開始錄製"
            ),
            CurrentReplayRecordingPhase.Recording => T("Recording", "正在录制", "正在錄製"),
            CurrentReplayRecordingPhase.Finalizing => T("Exporting", "正在导出", "正在匯出"),
            CurrentReplayRecordingPhase.Succeeded or CurrentReplayRecordingPhase.Degraded => T(
                "Open recording",
                "打开录像",
                "開啟錄影"
            ),
            CurrentReplayRecordingPhase.Failed => snapshot.CanStart
                ? T("Recording unavailable", "录制不可用", "錄製不可用")
                : T("Recording failed", "录像失败", "錄影失敗"),
            CurrentReplayRecordingPhase.Unavailable => T(
                "Replay without recording",
                "普通回放（未录制）",
                "一般回放（未錄製）"
            ),
            _ => T("Recording unavailable", "录制不可用", "錄製不可用"),
        };

        return string.IsNullOrWhiteSpace(snapshot.Reason) ? text : $"{text}\n{snapshot.Reason}";
    }

    private static string T(string english, string simplified, string traditional) =>
        L.Resolve(new LocalizedTextSet(english, simplified, traditional));
}

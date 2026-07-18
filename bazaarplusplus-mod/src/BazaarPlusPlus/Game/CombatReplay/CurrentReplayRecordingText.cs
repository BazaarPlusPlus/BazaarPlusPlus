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
                "Saving battle data…",
                "正在保存战斗数据…",
                "正在儲存戰鬥資料…"
            ),
            CurrentReplayRecordingPhase.Preparing => T(
                "Preparing video recorder…",
                "正在准备视频录制…",
                "正在準備影片錄製…"
            ),
            CurrentReplayRecordingPhase.Ready => T(
                "Record and export battle video",
                "录制并导出战斗视频",
                "錄製並匯出戰鬥影片"
            ),
            CurrentReplayRecordingPhase.Armed => T(
                "Starting recording…",
                "正在开始录制…",
                "正在開始錄製…"
            ),
            CurrentReplayRecordingPhase.Recording => T(
                "Recording battle video",
                "正在录制战斗视频",
                "正在錄製戰鬥影片"
            ),
            CurrentReplayRecordingPhase.Finalizing => T(
                "Exporting video…",
                "正在导出视频…",
                "正在匯出影片…"
            ),
            CurrentReplayRecordingPhase.Succeeded => T(
                "Show recorded video",
                "查看已录制视频",
                "查看已錄製影片"
            ),
            CurrentReplayRecordingPhase.Degraded => T(
                "Show recorded video (completed with warnings)",
                "查看已录制视频（存在警告）",
                "查看已錄製影片（存在警告）"
            ),
            CurrentReplayRecordingPhase.Failed => T(
                "Recording unavailable — click to retry",
                "录制不可用，点击重试",
                "錄製不可用，點擊重試"
            ),
            _ => T("Video recording unavailable", "视频录制不可用", "影片錄製不可用"),
        };

        return string.IsNullOrWhiteSpace(snapshot.Reason) ? text : $"{text}\n{snapshot.Reason}";
    }

    private static string T(string english, string simplified, string traditional) =>
        L.Resolve(new LocalizedTextSet(english, simplified, traditional));
}

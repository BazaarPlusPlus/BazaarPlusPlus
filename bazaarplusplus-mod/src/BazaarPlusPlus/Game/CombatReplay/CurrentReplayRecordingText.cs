#nullable enable
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.CombatReplay;

internal static class CurrentReplayRecordingText
{
    internal static string Tooltip(CurrentReplayRecordingSnapshot snapshot)
    {
        return Tooltip(snapshot, L.Resolve);
    }

    internal static string Tooltip(
        CurrentReplayRecordingSnapshot snapshot,
        string languageCode,
        bool traditionalChinese
    )
    {
        var mode = traditionalChinese ? BppChineseLocaleMode.Taiwan : BppChineseLocaleMode.Mainland;
        return Tooltip(snapshot, set => set.Resolve(languageCode, mode));
    }

    private static string Tooltip(
        CurrentReplayRecordingSnapshot snapshot,
        Func<LocalizedTextSet, string> resolve
    )
    {
        string T(string english, string simplified, string traditional) =>
            resolve(new LocalizedTextSet(english, simplified, traditional));

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

        var status = Status(snapshot.StatusCode, resolve);
        return status == null ? text : $"{text}\n{status}";
    }

    internal static string StartFailure(CurrentReplayRecordingStatusCode statusCode) =>
        Status(statusCode, L.Resolve)
        ?? L.Resolve(
            new LocalizedTextSet("Recording could not start", "无法开始录制", "無法開始錄製")
        );

    private static string? Status(
        CurrentReplayRecordingStatusCode statusCode,
        Func<LocalizedTextSet, string> resolve
    )
    {
        string T(string english, string simplified, string traditional) =>
            resolve(new LocalizedTextSet(english, simplified, traditional));

        return statusCode switch
        {
            CurrentReplayRecordingStatusCode.None => null,
            CurrentReplayRecordingStatusCode.ReplayUnavailable => T(
                "Replay is unavailable",
                "回放不可用",
                "重播不可用"
            ),
            CurrentReplayRecordingStatusCode.ReplayInProgress => T(
                "Finish the current replay before recording it",
                "请先完成当前回放，再开始录制",
                "請先完成目前重播，再開始錄製"
            ),
            CurrentReplayRecordingStatusCode.BoardUnavailable => T(
                "The combat board is unavailable",
                "战斗棋盘不可用",
                "戰鬥棋盤不可用"
            ),
            CurrentReplayRecordingStatusCode.StorageMoving => T(
                "Wait for the board animation to finish",
                "请等待棋盘动画结束",
                "請等待棋盤動畫結束"
            ),
            CurrentReplayRecordingStatusCode.StorageOpen => T(
                "Close storage before recording",
                "请先关闭仓库界面",
                "請先關閉倉庫介面"
            ),
            CurrentReplayRecordingStatusCode.InputBlocked => T(
                "Wait until the game is ready",
                "请等待游戏恢复操作",
                "請等待遊戲恢復操作"
            ),
            CurrentReplayRecordingStatusCode.ConnectionLost => T(
                "Reconnect before recording",
                "请恢复连接后再录制",
                "請恢復連線後再錄製"
            ),
            CurrentReplayRecordingStatusCode.RecorderUnavailable => T(
                "Video recorder is unavailable",
                "视频录制器不可用",
                "影片錄製器不可用"
            ),
            CurrentReplayRecordingStatusCode.SessionChanged => T(
                "The replay session changed",
                "回放会话已变化",
                "重播工作階段已變更"
            ),
            CurrentReplayRecordingStatusCode.PromotionFailed => T(
                "The replay recording session changed before it could start",
                "录制开始前回放会话已变化",
                "錄製開始前重播工作階段已變更"
            ),
            CurrentReplayRecordingStatusCode.StartingPublishFailed => T(
                "Replay recording could not start",
                "无法开始回放录制",
                "無法開始重播錄製"
            ),
            CurrentReplayRecordingStatusCode.NativeInvokeFailed => T(
                "The replay action failed",
                "回放操作失败",
                "重播操作失敗"
            ),
            CurrentReplayRecordingStatusCode.NativeStartRejected => T(
                "The game did not start the replay",
                "游戏未能开始回放",
                "遊戲未能開始重播"
            ),
            CurrentReplayRecordingStatusCode.RecordingFailed => T(
                "No usable video was created",
                "未生成可用录像",
                "未產生可用錄影"
            ),
            _ => T("Recording could not start", "无法开始录制", "無法開始錄製"),
        };
    }
}

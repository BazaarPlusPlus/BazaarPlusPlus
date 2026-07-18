#nullable enable
using System.Reflection;

namespace BazaarPlusPlus;

internal static class PluginLogIdentity
{
    internal static PluginEventId EventId(Type eventType) => EventId(eventType?.Name);

    internal static PluginEventId EventId(string? typeName) =>
        typeName switch
        {
            "ChineseLocaleModeChanged" => PluginEventId.ChineseLocaleModeChanged,
            "CombatFrameAdvanced" => PluginEventId.CombatFrameAdvanced,
            "CombatReplayPersistenceDrained" => PluginEventId.CombatReplayPersistenceDrained,
            "CombatReplayPlaybackEnded" => PluginEventId.CombatReplayPlaybackEnded,
            "CombatReplayPlaybackStarting" => PluginEventId.CombatReplayPlaybackStarting,
            "CombatSimObserved" => PluginEventId.CombatSimObserved,
            "NetMessageObserved" => PluginEventId.NetMessageObserved,
            "PvpBattleRecorded" => PluginEventId.PvpBattleRecorded,
            "RunInitializedObserved" => PluginEventId.RunInitializedObserved,
            "RunLifecycleChanged" => PluginEventId.RunLifecycleChanged,
            _ => PluginEventId.Unknown,
        };

    internal static PluginHandlerId HandlerId(MethodInfo method) =>
        HandlerId(method?.DeclaringType?.FullName);

    internal static PluginHandlerId HandlerId(string? declaringTypeName)
    {
        if (OwnedBy(declaringTypeName, "BazaarPlusPlus.Game.Upload.BackgroundUploadPump"))
            return PluginHandlerId.BackgroundUploadPump;
        if (OwnedBy(declaringTypeName, "BazaarPlusPlus.Game.CollectionPanel.CollectionPanelMount"))
            return PluginHandlerId.CollectionPanelMount;
        if (OwnedBy(declaringTypeName, "BazaarPlusPlus.Game.CombatReplay.CombatReplayModule"))
            return PluginHandlerId.CombatReplayModule;
        if (
            OwnedBy(
                declaringTypeName,
                "BazaarPlusPlus.Game.CombatReplay.Video.CombatReplayVideoRecorder"
            )
        )
            return PluginHandlerId.CombatReplayVideoRecorder;
        if (OwnedBy(declaringTypeName, "BazaarPlusPlus.Game.CombatStatusBar.CombatStatusBarModule"))
            return PluginHandlerId.CombatStatusBarModule;
        if (OwnedBy(declaringTypeName, "BazaarPlusPlus.Game.Screenshots.EndOfRunCaptureDriver"))
            return PluginHandlerId.EndOfRunCaptureDriver;
        if (OwnedBy(declaringTypeName, "BazaarPlusPlus.Game.HistoryPanel.HistoryPanelMount"))
            return PluginHandlerId.HistoryPanelMount;
        if (OwnedBy(declaringTypeName, "BazaarPlusPlus.Game.RunLogging.Upload.RunBundleUploadFeed"))
            return PluginHandlerId.RunBundleUploadFeed;
        if (OwnedBy(declaringTypeName, "BazaarPlusPlus.Game.RunLifecycle.RunLifecycleModule"))
            return PluginHandlerId.RunLifecycleModule;
        if (OwnedBy(declaringTypeName, "BazaarPlusPlus.Game.RunLogging.RunLoggingModule"))
            return PluginHandlerId.RunLoggingModule;
        return PluginHandlerId.Unknown;
    }

    internal static PluginFeatureId FeatureId(Type featureType) => FeatureId(featureType?.FullName);

    internal static PluginFeatureId FeatureId(string? typeName) =>
        typeName switch
        {
            "BazaarPlusPlus.Game.RunLifecycle.RunLifecycleModule" => PluginFeatureId.RunLifecycle,
            "BazaarPlusPlus.Game.CombatReplay.CombatReplayModule" => PluginFeatureId.CombatReplay,
            "BazaarPlusPlus.Game.CombatStatusBar.CombatStatusBarModule" =>
                PluginFeatureId.CombatStatusBar,
            "BazaarPlusPlus.GameInterop.VoiceSubtitles.VoiceSubtitlesInteropModule" =>
                PluginFeatureId.VoiceSubtitlesInterop,
            "BazaarPlusPlus.Game.VoiceSubtitles.VoiceSubtitlesModule" =>
                PluginFeatureId.VoiceSubtitles,
            _ => PluginFeatureId.Unknown,
        };

    private static bool OwnedBy(string? candidate, string owner) =>
        string.Equals(candidate, owner, StringComparison.Ordinal)
        || (candidate?.StartsWith(owner + "+", StringComparison.Ordinal) ?? false);
}

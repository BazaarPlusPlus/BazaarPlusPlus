#nullable enable

using System.Reflection.Emit;
using BazaarPlusPlus.GameInterop.VoiceSubtitles;
using BazaarPlusPlus.Infrastructure;
using FMOD.Studio;
using FMODUnity;
using HarmonyLib;

namespace BazaarPlusPlus.Patches.VoiceSubtitles;

[HarmonyPatch(typeof(SoundManager), "OnSystemsInitialized")]
internal static class SoundManagerInitializedPatch
{
    [HarmonyPostfix]
    private static void Postfix(SoundManager __instance)
    {
        try
        {
            VoiceLineVoObserverBridge.Install(__instance.VOPlayer);
        }
        catch (Exception exception)
        {
            BppLog.ErrorEvent(
                VoicePatchLogEvents.ObserverFailed,
                exception,
                VoicePatchLogEvents.ObserverFailedReasonCode.Bind(
                    VoicePatchLogReasonCode.ObserverInstallFailed
                )
            );
        }
    }
}

[HarmonyPatch(typeof(VOPlayer), nameof(VOPlayer.PlayVO))]
internal static class VOPlayerPlayVOPatch
{
    private const int StoppedCallbackMask = 0x20;
    private const int ExpectedPatchCount = 1;

    [HarmonyPrefix]
    private static void Prefix(
        VOPlayer __instance,
        bool isHero,
        CardAudio.AudioHookType audioHookType
    )
    {
        if (!VoiceLineVoObserverBridge.IsSubtitleObservationEnabled())
            return;

        VoiceLineVoObserverBridge.BeginVoiceAttempt(
            VoiceLineVoObserverBridge.CreateVoiceAttempt(__instance, isHero, audioHookType)
        );
    }

    [HarmonyPostfix]
    private static void Postfix()
    {
        VoiceLineVoObserverBridge.ClearVoiceAttempt(VoiceObserverLogReasonCode.PlayVoCompleted);
    }

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions
    )
    {
        var codes = instructions.ToList();
        var setCallback = AccessTools.Method(
            typeof(EventInstance),
            nameof(EventInstance.setCallback),
            new[] { typeof(EVENT_CALLBACK), typeof(EVENT_CALLBACK_TYPE) }
        );
        if (setCallback == null)
        {
            ReportPatchDegraded(VoicePatchLogReasonCode.CallbackApiUnavailable, actualCount: 0);
            return codes;
        }

        var callbackMask = (int)(EVENT_CALLBACK_TYPE.STOPPED | EVENT_CALLBACK_TYPE.SOUND_PLAYED);
        var patched = 0;

        for (var i = 0; i < codes.Count; i++)
        {
            if (
                !codes[i].Calls(setCallback)
                || i == 0
                || !codes[i - 1].LoadsConstant(StoppedCallbackMask)
            )
                continue;

            var replacement = new CodeInstruction(OpCodes.Ldc_I4, callbackMask);
            replacement.labels.AddRange(codes[i - 1].labels);
            replacement.blocks.AddRange(codes[i - 1].blocks);
            codes[i - 1] = replacement;
            patched++;
        }

        if (patched == ExpectedPatchCount)
        {
#if DEBUG
            BppLog.DebugEvent(
                VoicePatchLogEvents.CallbackPatchReady,
                () =>
                    [
                        VoicePatchLogEvents.CallbackPatchReadyActualCount.Bind(patched),
                        VoicePatchLogEvents.CallbackPatchReadyExpectedCount.Bind(
                            ExpectedPatchCount
                        ),
                    ]
            );
#endif
        }
        else
        {
            ReportPatchDegraded(VoicePatchLogReasonCode.PatchCountMismatch, patched);
        }

        return codes;
    }

    private static void ReportPatchDegraded(VoicePatchLogReasonCode reasonCode, int actualCount)
    {
        BppLog.WarnEvent(
            VoicePatchLogEvents.CallbackPatchDegraded,
            VoicePatchLogEvents.CallbackPatchDegradedReasonCode.Bind(reasonCode),
            VoicePatchLogEvents.CallbackPatchDegradedActualCount.Bind(actualCount),
            VoicePatchLogEvents.CallbackPatchDegradedExpectedCount.Bind(ExpectedPatchCount)
        );
    }
}

[HarmonyPatch(typeof(VOPlayer), nameof(VOPlayer.PlayTutorialVO))]
internal static class VOPlayerPlayTutorialVOPatch
{
    [HarmonyPrefix]
    private static void Prefix(VOPlayer __instance, EventReference eventRef)
    {
        if (!VoiceLineVoObserverBridge.IsSubtitleObservationEnabled())
            return;

        VoiceLineVoObserverBridge.BeginVoiceAttempt(
            VoiceLineVoObserverBridge.CreateVoiceAttempt(
                __instance,
                "PlayTutorialVO",
                "Hero",
                "Tutorial",
                eventRef
            )
        );
    }

    [HarmonyPostfix]
    private static void Postfix()
    {
        VoiceLineVoObserverBridge.ClearVoiceAttempt(
            VoiceObserverLogReasonCode.PlayTutorialVoCompleted
        );
    }
}

[HarmonyPatch(typeof(VOPlayer), "OnVOStopInternal")]
internal static class VOPlayerOnVOStopInternalPatch
{
    [HarmonyPrefix]
    private static void Prefix(VOPlayer __instance, EVENT_CALLBACK_TYPE type, IntPtr parameters)
    {
        if (type == EVENT_CALLBACK_TYPE.SOUND_PLAYED)
            VoiceLineVoObserverBridge.OnVoSoundPlayed(__instance, parameters);
    }

    [HarmonyPostfix]
    private static void Postfix(VOPlayer __instance, EVENT_CALLBACK_TYPE type)
    {
        if (type == EVENT_CALLBACK_TYPE.STOPPED)
            VoiceLineVoObserverBridge.OnVoPlaybackStopped(__instance);
    }
}

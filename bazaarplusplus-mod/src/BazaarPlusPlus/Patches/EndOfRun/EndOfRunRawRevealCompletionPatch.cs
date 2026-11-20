#nullable enable
using HarmonyLib;
using TheBazaar.Game.Cards.Animation;
using UnityEngine.Playables;

namespace BazaarPlusPlus.Patches.EndOfRun;

[HarmonyPatch(typeof(BaseCardRevealAnimationDriver), "CreateRawGraph")]
internal static class EndOfRunRawRevealCompletionPatch
{
    [HarmonyPostfix]
    private static void Postfix(ref PlayableGraph __result)
    {
        if (!__result.IsValid())
            return;

        // The raw graph has only animation outputs. DelayPlayableBehavior needs a ScriptPlayableOutput
        // to run ProcessFrame and finish, or the native FaceUp finalization never runs.
        var rootCount = __result.GetRootPlayableCount();
        for (var index = 0; index < rootCount; index++)
        {
            var root = __result.GetRootPlayable(index);
            var completionOutput = ScriptPlayableOutput.Create(
                __result,
                $"BPP_RawRevealCompletion_{index}"
            );
            completionOutput.SetSourcePlayable(root, 1);
        }
    }
}

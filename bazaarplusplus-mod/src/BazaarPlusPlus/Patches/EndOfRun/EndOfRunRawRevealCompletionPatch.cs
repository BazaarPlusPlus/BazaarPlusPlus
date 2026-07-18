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

        // DelayPlayableBehavior marks itself done from ProcessFrame, and Unity only runs that
        // callback for branches connected to a ScriptPlayableOutput. The native raw builder
        // creates only AnimationPlayableOutputs, unlike its non-raw sibling, so end-of-run roots
        // otherwise play forever and the native FaceUp finalization is never reached.
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

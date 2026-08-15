#nullable enable

using System.Reflection;
using HarmonyLib;
using TheBazaar;
using UnityEngine;

namespace BazaarPlusPlus.Game.CombatReplay;

internal readonly record struct CurrentReplayPresentationReadinessSnapshot(
    bool ReplayActive,
    bool BoardUpdating,
    bool StorageMoving,
    bool BoardPresentationUpdating,
    bool CarpetUnrolling,
    bool BoardRevealing,
    bool HasCardsToReveal,
    bool PlayerSkillBoardUpdating,
    bool OpponentSkillBoardUpdating,
    int ExpectedItemCount,
    int VisibleItemCount,
    int FaceUpItemCount,
    int SettledItemCount,
    int ExpectedSkillCount,
    int RegisteredSkillCount,
    int ReadySkillCount
);

internal static class CurrentReplayPresentationReadiness
{
    internal const int RequiredStableFrames = 2;
    internal const float TimeoutSeconds = 10f;
    private const string CardIdleFaceUpStateName = "Card_Idle_Faceup_A";
    private static readonly FieldInfo? SkillIconCurrentTextureField = AccessTools.Field(
        typeof(SkillProxyRenderer),
        "_skillIconCurrentTexture"
    );

    internal static bool IsVisible(ItemController controller) =>
        controller.gameObject.activeInHierarchy
        && controller.IsCardVisible
        && controller.PositionedInSocket;

    internal static bool IsFaceUp(ItemController controller)
    {
        var animator = controller.Animator;
        return IsVisible(controller)
            && animator != null
            && animator.isActiveAndEnabled
            && animator.GetBool(AnimationParameterDefinitions.CardFaceUpParam);
    }

    internal static bool IsSettled(ItemController controller)
    {
        var animator = controller.Animator;
        return IsFaceUp(controller)
            && animator != null
            && !animator.IsInTransition(0)
            && animator.GetCurrentAnimatorStateInfo(0).IsName(CardIdleFaceUpStateName);
    }

    internal static bool IsSkillReady(SkillProxyRenderer renderer)
    {
        if (
            renderer == null
            || renderer.Card == null
            || !renderer.gameObject.activeInHierarchy
            || renderer.transform.localScale.sqrMagnitude <= Mathf.Epsilon
        )
        {
            return false;
        }

        return SkillIconCurrentTextureField?.GetValue(renderer) is Texture texture
            && texture != null;
    }

    internal static bool IsReady(CurrentReplayPresentationReadinessSnapshot snapshot) =>
        snapshot.ReplayActive
        && !snapshot.BoardUpdating
        && !snapshot.StorageMoving
        && !snapshot.BoardPresentationUpdating
        && !snapshot.CarpetUnrolling
        && !snapshot.BoardRevealing
        && !snapshot.HasCardsToReveal
        && !snapshot.PlayerSkillBoardUpdating
        && !snapshot.OpponentSkillBoardUpdating
        && snapshot.ExpectedItemCount >= 0
        && snapshot.VisibleItemCount == snapshot.ExpectedItemCount
        && snapshot.FaceUpItemCount == snapshot.ExpectedItemCount
        && snapshot.SettledItemCount == snapshot.ExpectedItemCount
        && snapshot.ExpectedSkillCount >= 0
        && snapshot.RegisteredSkillCount == snapshot.ExpectedSkillCount
        && snapshot.ReadySkillCount == snapshot.ExpectedSkillCount;

    internal static int AdvanceStableFrameCount(
        int previousStableFrameCount,
        CurrentReplayPresentationReadinessSnapshot snapshot
    ) => IsReady(snapshot) ? Math.Min(previousStableFrameCount + 1, RequiredStableFrames) : 0;

    internal static int AdvanceRecapStableFrameCount(
        int previousStableFrameCount,
        CurrentReplayPresentationReadinessSnapshot snapshot,
        int? recordedItemCount
    ) =>
        IsReadyForRecap(snapshot, recordedItemCount)
            ? Math.Min(previousStableFrameCount + 1, RequiredStableFrames)
            : 0;

    private static bool IsReadyForRecap(
        CurrentReplayPresentationReadinessSnapshot snapshot,
        int? recordedItemCount
    )
    {
        var requiredItemCount = recordedItemCount ?? snapshot.ExpectedItemCount;
        return !snapshot.ReplayActive
            && !snapshot.BoardUpdating
            && !snapshot.StorageMoving
            && !snapshot.BoardPresentationUpdating
            && !snapshot.CarpetUnrolling
            && !snapshot.BoardRevealing
            && !snapshot.HasCardsToReveal
            && !snapshot.PlayerSkillBoardUpdating
            && !snapshot.OpponentSkillBoardUpdating
            && snapshot.ExpectedItemCount == requiredItemCount
            && snapshot.VisibleItemCount == requiredItemCount
            && snapshot.FaceUpItemCount == requiredItemCount
            && snapshot.SettledItemCount == requiredItemCount
            && snapshot.ExpectedSkillCount >= 0
            && snapshot.RegisteredSkillCount == snapshot.ExpectedSkillCount
            && snapshot.ReadySkillCount == snapshot.ExpectedSkillCount;
    }
}

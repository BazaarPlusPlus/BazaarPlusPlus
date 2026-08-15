#nullable enable
using System.Collections;
using System.Reflection;
using TheBazaar.UI.EndOfRun;
using UnityEngine;

namespace BazaarPlusPlus.Game.Screenshots;

internal readonly record struct EndOfRunSummaryVisualSnapshot(
    int LoadedCardCount,
    ulong CardSetFingerprint,
    ulong PoseFingerprint
);

internal static class EndOfRunSummaryVisualSnapshotSampler
{
    private const string LoadedCardsFieldName = "loadedCards";
    private const string AnimatorMemberName = "Animator";
    private const ulong HashOffset = 14695981039346656037UL;
    private const ulong HashPrime = 1099511628211UL;

    public static bool TryCapture(
        EndOfRunSummaryController? summaryController,
        out EndOfRunSummaryVisualSnapshot snapshot
    )
    {
        var observation = CaptureCleanFrameVisual(summaryController, out snapshot);
        return observation.State == EndOfRunCleanFrameVisualState.Sampled;
    }

    internal static EndOfRunCleanFrameVisualObservation CaptureCleanFrameVisual(
        EndOfRunSummaryController? summaryController
    )
    {
        var observation = CaptureCleanFrameVisual(summaryController, out var snapshot);
        return observation.State == EndOfRunCleanFrameVisualState.Sampled
            ? EndOfRunCleanFrameVisualObservation.Sampled(
                snapshot.LoadedCardCount,
                snapshot.CardSetFingerprint,
                snapshot.PoseFingerprint
            )
            : observation;
    }

    private static EndOfRunCleanFrameVisualObservation CaptureCleanFrameVisual(
        EndOfRunSummaryController? summaryController,
        out EndOfRunSummaryVisualSnapshot snapshot
    )
    {
        snapshot = default;
        if (summaryController == null)
            return EndOfRunCleanFrameVisualObservation.Unavailable;
        if (!TryGetFieldValue(summaryController, LoadedCardsFieldName, out var loadedCardsValue))
            return EndOfRunCleanFrameVisualObservation.Unavailable;
        if (loadedCardsValue is not IEnumerable loadedCards)
            return EndOfRunCleanFrameVisualObservation.Unavailable;

        var loadedCardCount = 0;
        var cardSetFingerprint = HashOffset;
        var poseFingerprint = HashOffset;
        foreach (var loadedCard in loadedCards)
        {
            if (loadedCard == null)
                continue;
            if (loadedCard is UnityEngine.Object unityCard && unityCard == null)
                return EndOfRunCleanFrameVisualObservation.Unavailable;
            if (!TryGetMemberValue(loadedCard, AnimatorMemberName, out var animatorValue))
                return EndOfRunCleanFrameVisualObservation.Unavailable;
            if (animatorValue is not Animator animator || animator == null)
                return EndOfRunCleanFrameVisualObservation.Unavailable;

            var root = animator.transform;
            if (root == null)
                return EndOfRunCleanFrameVisualObservation.Unavailable;

            loadedCardCount++;
            AddInt(ref cardSetFingerprint, animator.GetInstanceID());
            AddTransformHierarchy(root, ref cardSetFingerprint, ref poseFingerprint);
        }

        if (loadedCardCount == 0)
            return EndOfRunCleanFrameVisualObservation.Empty;

        AddInt(ref cardSetFingerprint, loadedCardCount);
        snapshot = new EndOfRunSummaryVisualSnapshot(
            loadedCardCount,
            cardSetFingerprint,
            poseFingerprint
        );
        return EndOfRunCleanFrameVisualObservation.Sampled(
            loadedCardCount,
            cardSetFingerprint,
            poseFingerprint
        );
    }

    private static void AddTransformHierarchy(
        Transform transform,
        ref ulong cardSetFingerprint,
        ref ulong poseFingerprint
    )
    {
        AddInt(ref cardSetFingerprint, transform.GetInstanceID());

        AddInt(ref poseFingerprint, transform.gameObject.activeSelf ? 1 : 0);
        AddVector3(ref poseFingerprint, transform.localPosition);
        AddQuaternion(ref poseFingerprint, transform.localRotation);
        AddVector3(ref poseFingerprint, transform.localScale);
        if (transform is RectTransform rectTransform)
        {
            AddVector2(ref poseFingerprint, rectTransform.anchorMin);
            AddVector2(ref poseFingerprint, rectTransform.anchorMax);
            AddVector2(ref poseFingerprint, rectTransform.anchoredPosition);
            AddVector2(ref poseFingerprint, rectTransform.sizeDelta);
            AddVector2(ref poseFingerprint, rectTransform.pivot);
        }

        for (var index = 0; index < transform.childCount; index++)
        {
            var child = transform.GetChild(index);
            if (child == null || IsNonStructuralVisualSubtree(child))
                continue;

            AddTransformHierarchy(child, ref cardSetFingerprint, ref poseFingerprint);
        }
    }

    private static bool IsNonStructuralVisualSubtree(Transform transform)
    {
        var name = transform.name;
        return transform.GetComponent<ParticleSystem>() != null
            || name.IndexOf("vfx", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("particle", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool TryGetFieldValue(object instance, string fieldName, out object? value)
    {
        value = null;
        FieldInfo? field = null;
        for (var type = instance.GetType(); type != null && field == null; type = type.BaseType)
        {
            field = type.GetField(
                fieldName,
                BindingFlags.Instance
                    | BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.DeclaredOnly
            );
        }
        if (field == null)
            return false;

        value = field.GetValue(instance);
        return true;
    }

    private static bool TryGetMemberValue(object instance, string memberName, out object? value)
    {
        value = null;
        var property = instance
            .GetType()
            .GetProperty(
                memberName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );
        if (property != null)
        {
            value = property.GetValue(instance);
            return true;
        }

        var field = instance
            .GetType()
            .GetField(
                memberName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );
        if (field == null)
            return false;

        value = field.GetValue(instance);
        return true;
    }

    private static void AddVector2(ref ulong hash, Vector2 value)
    {
        AddFloat(ref hash, value.x);
        AddFloat(ref hash, value.y);
    }

    private static void AddVector3(ref ulong hash, Vector3 value)
    {
        AddFloat(ref hash, value.x);
        AddFloat(ref hash, value.y);
        AddFloat(ref hash, value.z);
    }

    private static void AddQuaternion(ref ulong hash, Quaternion value)
    {
        AddFloat(ref hash, value.x);
        AddFloat(ref hash, value.y);
        AddFloat(ref hash, value.z);
        AddFloat(ref hash, value.w);
    }

    private static void AddFloat(ref ulong hash, float value)
    {
        AddInt(ref hash, value.GetHashCode());
    }

    private static void AddInt(ref ulong hash, int value)
    {
        hash ^= unchecked((uint)value);
        hash *= HashPrime;
    }
}

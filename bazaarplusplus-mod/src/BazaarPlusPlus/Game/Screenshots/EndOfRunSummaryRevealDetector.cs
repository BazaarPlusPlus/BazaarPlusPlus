#nullable enable
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BazaarPlusPlus.Game.Screenshots;

internal enum EndOfRunSummaryRevealState
{
    TargetDetectionFailed,
    NotSummary,
    NoLoadedCards,
    RevealInProgress,
    RevealComplete,
    DetectionFailed,
}

internal readonly record struct EndOfRunSummaryRevealOutcome(
    EndOfRunSummaryRevealState State,
    ScreenshotCaptureReasonCode? ReasonCode,
    Exception? Exception,
    bool SkillsSettled
);

internal static class EndOfRunSummaryRevealDetector
{
    private const string SummaryControllerTypeName =
        "TheBazaar.UI.EndOfRun.EndOfRunSummaryController";
    private const string ActiveControllerFieldName = "_activeController";
    private const string LoadedCardsFieldName = "loadedCards";
    private const string SkillSequenceFieldName = "_skillSequence";
    private const string TweenDurationFieldName = "duration";
    private const string TweenIsCompleteFieldName = "isComplete";
    private const string GetBoolMethodName = "GetBool";
    private const string UnityAnimatorTypeName = "UnityEngine.Animator";
    private const string FaceUpParamName = "FaceUp";

    // Polled every frame while the capture waits for readiness, so member lookups are cached
    // per runtime type, misses included.
    private static readonly object MemberCacheGate = new();
    private static readonly Dictionary<(Type Type, string Name), FieldInfo?> Fields = new();
    private static readonly Dictionary<Type, FaceUpReader> FaceUpReaders = new();
    private static readonly object[] FaceUpArguments = [FaceUpParamName];
    private static int? _faceUpHash;

    public static EndOfRunSummaryRevealOutcome GetRevealOutcome(object? screenController)
    {
        try
        {
            var state = GetRevealStateCore(screenController, out var skillsSettled);
            return new EndOfRunSummaryRevealOutcome(
                state,
                state
                    is EndOfRunSummaryRevealState.TargetDetectionFailed
                        or EndOfRunSummaryRevealState.DetectionFailed
                    ? ScreenshotCaptureReasonCode.RevealProbeFailed
                    : null,
                null,
                skillsSettled
            );
        }
        catch (Exception ex)
        {
            return new EndOfRunSummaryRevealOutcome(
                EndOfRunSummaryRevealState.DetectionFailed,
                ScreenshotCaptureReasonCode.RevealProbeFailed,
                ex,
                SkillsSettled: false
            );
        }
    }

    private static EndOfRunSummaryRevealState GetRevealStateCore(
        object? screenController,
        out bool skillsSettled
    )
    {
        skillsSettled = false;
        if (!TryGetSummaryController(screenController, out var activeController))
            return EndOfRunSummaryRevealState.TargetDetectionFailed;
        if (activeController == null)
            return EndOfRunSummaryRevealState.NotSummary;
        if (!IsSummaryController(activeController))
            return EndOfRunSummaryRevealState.NotSummary;
        if (!TryGetFieldValue(activeController, LoadedCardsFieldName, out var loadedCardsValue))
        {
            return EndOfRunSummaryRevealState.DetectionFailed;
        }
        if (loadedCardsValue is not IEnumerable loadedCards)
            return EndOfRunSummaryRevealState.DetectionFailed;

        var loadedCardCount = 0;
        var allLoadedCardsFaceUp = true;
        foreach (var loadedCard in loadedCards)
        {
            if (loadedCard == null)
                continue;
            loadedCardCount++;
            var animatorAccessor = EndOfRunSummaryReflection.GetAnimatorAccessor(
                loadedCard.GetType()
            );
            if (animatorAccessor == null)
            {
                return EndOfRunSummaryRevealState.DetectionFailed;
            }
            var animator = animatorAccessor.GetValue(loadedCard);
            if (animator == null)
            {
                allLoadedCardsFaceUp = false;
                continue;
            }
            if (!TryReadFaceUp(animator, out var isFaceUp))
            {
                return EndOfRunSummaryRevealState.DetectionFailed;
            }
            if (!isFaceUp)
                allLoadedCardsFaceUp = false;
        }

        if (!TryGetFieldValue(activeController, SkillSequenceFieldName, out var skillSequence))
        {
            return EndOfRunSummaryRevealState.DetectionFailed;
        }

        // DisplaySkills runs immediately after DisplayCardsAsync is invoked. Until its sequence
        // exists, the summary display has started but has not reached a settled frame.
        if (skillSequence == null)
            return EndOfRunSummaryRevealState.RevealInProgress;

        if (
            !TryGetFieldValue(skillSequence, TweenDurationFieldName, out var durationValue)
            || durationValue is not float duration
        )
        {
            return EndOfRunSummaryRevealState.DetectionFailed;
        }

        if (duration > 0f)
        {
            if (
                !TryGetFieldValue(skillSequence, TweenIsCompleteFieldName, out var isCompleteValue)
                || isCompleteValue is not bool isComplete
            )
            {
                return EndOfRunSummaryRevealState.DetectionFailed;
            }

            if (!isComplete)
                return EndOfRunSummaryRevealState.RevealInProgress;
        }

        skillsSettled = true;
        if (!allLoadedCardsFaceUp)
            return EndOfRunSummaryRevealState.RevealInProgress;

        return loadedCardCount == 0
            ? EndOfRunSummaryRevealState.NoLoadedCards
            : EndOfRunSummaryRevealState.RevealComplete;
    }

    private static bool TryGetSummaryController(
        object? screenController,
        out object? activeController
    )
    {
        activeController = null;
        if (!TryGetFieldValue(screenController, ActiveControllerFieldName, out activeController))
        {
            return false;
        }

        return true;
    }

    private static bool IsSummaryController(object activeController)
    {
        return string.Equals(
            activeController.GetType().FullName,
            SummaryControllerTypeName,
            StringComparison.Ordinal
        );
    }

    internal static FieldInfo? FindCachedField(Type type, string fieldName)
    {
        lock (MemberCacheGate)
        {
            if (Fields.TryGetValue((type, fieldName), out var cached))
                return cached;

            var field = EndOfRunSummaryReflection.FindField(type, fieldName);
            Fields[(type, fieldName)] = field;
            return field;
        }
    }

    private static bool TryGetFieldValue(object? instance, string fieldName, out object? value)
    {
        value = null;
        if (instance == null)
            return false;

        var field = FindCachedField(instance.GetType(), fieldName);
        if (field == null)
            return false;

        value = field.GetValue(instance);
        return true;
    }

    private static bool TryReadFaceUp(object animator, out bool value)
    {
        value = false;
        var reader = GetFaceUpReader(animator.GetType());
        if (reader.IsUnityAnimator)
        {
            value = ReadUnityAnimatorFaceUp(animator);
            return true;
        }
        if (reader.GetBool == null)
            return false;

        value = (bool?)reader.GetBool.Invoke(animator, FaceUpArguments) == true;
        return true;
    }

    // Kept out of TryReadFaceUp so only a real Unity animator binds UnityEngine.Animator; the
    // reflection fallback then runs where that assembly is absent.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool ReadUnityAnimatorFaceUp(object animator)
    {
        _faceUpHash ??= Animator.StringToHash(FaceUpParamName);
        return ((Animator)animator).GetBool(_faceUpHash.Value);
    }

    private static FaceUpReader GetFaceUpReader(Type type)
    {
        lock (MemberCacheGate)
        {
            if (FaceUpReaders.TryGetValue(type, out var cached))
                return cached;

            var reader = string.Equals(
                type.FullName,
                UnityAnimatorTypeName,
                StringComparison.Ordinal
            )
                ? new FaceUpReader(IsUnityAnimator: true, GetBool: null)
                : new FaceUpReader(
                    IsUnityAnimator: false,
                    type.GetMethod(
                        GetBoolMethodName,
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        binder: null,
                        types: [typeof(string)],
                        modifiers: null
                    )
                );
            FaceUpReaders[type] = reader;
            return reader;
        }
    }

    private readonly record struct FaceUpReader(bool IsUnityAnimator, MethodInfo? GetBool);
}

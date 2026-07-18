#nullable enable
using System.Collections;
using System.Reflection;

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
    Exception? Exception
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
    private const string AnimatorPropertyName = "Animator";
    private const string GetBoolMethodName = "GetBool";
    private const string FaceUpParamName = "FaceUp";

    public static EndOfRunSummaryRevealState GetRevealState(object? screenController) =>
        GetRevealOutcome(screenController).State;

    public static EndOfRunSummaryRevealOutcome GetRevealOutcome(object? screenController)
    {
        try
        {
            var state = GetRevealStateCore(screenController);
            return new EndOfRunSummaryRevealOutcome(
                state,
                state
                    is EndOfRunSummaryRevealState.TargetDetectionFailed
                        or EndOfRunSummaryRevealState.DetectionFailed
                    ? ScreenshotCaptureReasonCode.RevealProbeFailed
                    : null,
                null
            );
        }
        catch (Exception ex)
        {
            return new EndOfRunSummaryRevealOutcome(
                EndOfRunSummaryRevealState.DetectionFailed,
                ScreenshotCaptureReasonCode.RevealProbeFailed,
                ex
            );
        }
    }

    private static EndOfRunSummaryRevealState GetRevealStateCore(object? screenController)
    {
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
        foreach (var loadedCard in loadedCards)
        {
            if (loadedCard == null)
                continue;
            loadedCardCount++;
            if (!TryGetMemberValue(loadedCard, AnimatorPropertyName, out var animator))
            {
                return EndOfRunSummaryRevealState.DetectionFailed;
            }
            if (animator == null)
                return EndOfRunSummaryRevealState.RevealInProgress;
            if (!TryInvokeAnimatorGetBool(animator, FaceUpParamName, out var isFaceUp))
            {
                return EndOfRunSummaryRevealState.DetectionFailed;
            }
            if (!isFaceUp)
                return EndOfRunSummaryRevealState.RevealInProgress;
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

    private static bool TryGetFieldValue(object? instance, string fieldName, out object? value)
    {
        value = null;
        if (instance == null)
            return false;

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
        if (field != null)
        {
            value = field.GetValue(instance);
            return true;
        }

        return false;
    }

    private static bool TryInvokeAnimatorGetBool(
        object animator,
        string parameterName,
        out bool value
    )
    {
        value = false;
        var method = animator
            .GetType()
            .GetMethod(
                GetBoolMethodName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: [typeof(string)],
                modifiers: null
            );
        if (method == null)
            return false;

        value = (bool?)method.Invoke(animator, [parameterName]) == true;
        return true;
    }
}

#nullable enable
using System;
using System.Collections;
using System.Reflection;

namespace BazaarPlusPlus.Game.Screenshots;

internal enum EndOfRunSummaryRevealState
{
    NotSummary,
    RevealInProgress,
    RevealComplete,
    DetectionFailed,
}

internal static class EndOfRunSummaryRevealDetector
{
    private const string SummaryControllerTypeName =
        "TheBazaar.UI.EndOfRun.EndOfRunSummaryController";
    private const string ActiveControllerFieldName = "_activeController";
    private const string LoadedCardsFieldName = "loadedCards";
    private const string CardRevealDelayFieldName = "cardRevealDelay";
    private const string AnimatorPropertyName = "Animator";
    private const string GetBoolMethodName = "GetBool";
    private const string FaceUpParamName = "FaceUp";
    private static bool _warnedMissingActiveControllerField;
    private static bool _warnedMissingLoadedCardsField;
    private static bool _warnedMissingAnimatorProperty;
    private static bool _warnedMissingAnimatorGetBool;

    public static bool IsSummaryRevealInProgress(object? screenController)
    {
        return GetRevealState(screenController) == EndOfRunSummaryRevealState.RevealInProgress;
    }

    public static EndOfRunSummaryRevealState GetRevealState(object? screenController)
    {
        if (!TryGetSummaryController(screenController, out var activeController))
            return EndOfRunSummaryRevealState.DetectionFailed;
        if (activeController == null)
            return EndOfRunSummaryRevealState.NotSummary;
        if (!IsSummaryController(activeController))
            return EndOfRunSummaryRevealState.NotSummary;
        if (
            !TryGetFieldValue(
                activeController,
                LoadedCardsFieldName,
                out var loadedCardsValue,
                ref _warnedMissingLoadedCardsField,
                "Failed to resolve EndOfRunSummaryController.loadedCards; end-of-run mouse blocking will fall back to the game's default behavior."
            )
        )
        {
            return EndOfRunSummaryRevealState.DetectionFailed;
        }
        if (loadedCardsValue is not IEnumerable loadedCards)
            return EndOfRunSummaryRevealState.DetectionFailed;

        foreach (var loadedCard in loadedCards)
        {
            if (loadedCard == null)
                continue;
            if (
                !TryGetMemberValue(
                    loadedCard,
                    AnimatorPropertyName,
                    out var animator,
                    ref _warnedMissingAnimatorProperty,
                    "Failed to resolve summary card Animator; end-of-run mouse blocking will fall back to the game's default behavior."
                )
            )
            {
                return EndOfRunSummaryRevealState.DetectionFailed;
            }
            if (animator == null)
                return EndOfRunSummaryRevealState.RevealInProgress;
            if (
                !TryInvokeAnimatorGetBool(
                    animator,
                    FaceUpParamName,
                    out var isFaceUp,
                    ref _warnedMissingAnimatorGetBool,
                    "Failed to resolve Animator.GetBool(string) for summary reveal detection; end-of-run mouse blocking will fall back to the game's default behavior."
                )
            )
            {
                return EndOfRunSummaryRevealState.DetectionFailed;
            }
            if (!isFaceUp)
                return EndOfRunSummaryRevealState.RevealInProgress;
        }

        return EndOfRunSummaryRevealState.RevealComplete;
    }

    public static bool TryGetRevealTimeoutSeconds(
        object? screenController,
        out float timeoutSeconds
    )
    {
        timeoutSeconds = 0f;
        if (
            !TryGetSummaryController(screenController, out var activeController)
            || activeController == null
        )
            return false;
        if (!IsSummaryController(activeController))
            return false;

        var field = activeController
            .GetType()
            .GetField(
                CardRevealDelayFieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );
        if (field == null)
            return false;

        var delayValue = field.GetValue(activeController);
        if (delayValue == null)
        {
            return false;
        }

        if (delayValue is int delayMilliseconds)
        {
            timeoutSeconds = Math.Max(0f, delayMilliseconds / 1000f);
            return true;
        }

        if (delayValue is float delaySeconds)
        {
            timeoutSeconds = Math.Max(0f, delaySeconds);
            return true;
        }

        return false;
    }

    private static bool TryGetSummaryController(
        object? screenController,
        out object? activeController
    )
    {
        activeController = null;
        if (
            !TryGetFieldValue(
                screenController,
                ActiveControllerFieldName,
                out activeController,
                ref _warnedMissingActiveControllerField,
                "Failed to resolve EndOfRunScreenController._activeController; end-of-run mouse blocking will fall back to the game's default behavior."
            )
        )
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

    private static bool TryGetFieldValue(
        object? instance,
        string fieldName,
        out object? value,
        ref bool warned,
        string warningMessage
    )
    {
        value = null;
        if (instance == null)
            return false;

        var field = instance
            .GetType()
            .GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );
        if (field == null)
        {
            WarnOnce(ref warned, warningMessage);
            return false;
        }

        value = field.GetValue(instance);
        return true;
    }

    private static bool TryGetMemberValue(
        object instance,
        string memberName,
        out object? value,
        ref bool warned,
        string warningMessage
    )
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

        WarnOnce(ref warned, warningMessage);
        return false;
    }

    private static bool TryInvokeAnimatorGetBool(
        object animator,
        string parameterName,
        out bool value,
        ref bool warned,
        string warningMessage
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
        {
            WarnOnce(ref warned, warningMessage);
            return false;
        }

        value = (bool?)method.Invoke(animator, [parameterName]) == true;
        return true;
    }

    private static void WarnOnce(ref bool warned, string message)
    {
        if (warned)
            return;

        warned = true;
        BppLog.Warn("EndOfRunScreenshot", message);
    }
}

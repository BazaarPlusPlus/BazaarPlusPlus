#nullable enable
using System.Reflection;

namespace BazaarPlusPlus.Game.Screenshots;

internal static class EndOfRunContinueStateEvaluator
{
    private const string TransitionCountFieldName = "_transitionCount";
    private static bool _warnedMissingTransitionField;

    public static bool ShouldAllowContinue(
        object? screenController,
        bool suppressWhileCaptureInFlight
    )
    {
        return TryShouldAllowContinue(
            screenController,
            suppressWhileCaptureInFlight,
            out var shouldAllowContinue
        )
            ? shouldAllowContinue
            : true;
    }

    public static bool TryShouldAllowContinue(
        object? screenController,
        bool suppressWhileCaptureInFlight,
        out bool shouldAllowContinue
    )
    {
        if (suppressWhileCaptureInFlight)
        {
            shouldAllowContinue = false;
            return true;
        }

        shouldAllowContinue = true;
        return true;
    }

    public static string DescribeState(object? screenController, bool suppressWhileCaptureInFlight)
    {
        if (screenController == null)
            return "screenController=null";

        var revealState = EndOfRunSummaryRevealDetector.GetRevealState(screenController);
        if (!TryGetTransitionCount(screenController, out var transitionCount))
        {
            return $"screenController={screenController.GetType().Name} suppressWhileCaptureInFlight={suppressWhileCaptureInFlight} transitionCount=<missing> revealState={revealState}";
        }

        var shouldAllowContinue = ShouldAllowContinue(
            screenController,
            suppressWhileCaptureInFlight
        );
        return $"screenController={screenController.GetType().Name} suppressWhileCaptureInFlight={suppressWhileCaptureInFlight} transitionCount={transitionCount} revealState={revealState} shouldAllowContinue={shouldAllowContinue}";
    }

    public static bool TryIsInteractionBlocked(
        object? screenController,
        out bool isInteractionBlocked
    )
    {
        isInteractionBlocked = false;
        if (screenController == null)
            return false;

        if (!TryGetTransitionCount(screenController, out var transitionCount))
            return false;

        isInteractionBlocked = transitionCount > 0;
        return true;
    }

    public static bool TryGetTransitionCount(object screenController, out int transitionCount)
    {
        transitionCount = 0;
        var transitionCountField = screenController
            .GetType()
            .GetField(
                TransitionCountFieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );
        if (transitionCountField == null)
        {
            WarnMissingTransitionFieldOnce();
            return false;
        }

        transitionCount = (int?)transitionCountField.GetValue(screenController) ?? 0;
        return true;
    }

    private static void WarnMissingTransitionFieldOnce()
    {
        if (_warnedMissingTransitionField)
            return;

        _warnedMissingTransitionField = true;
        BppLog.Warn(
            "EndOfRunScreenshot",
            "Failed to resolve EndOfRunScreenController._transitionCount; end-of-run mouse blocking will fall back to the game's default behavior."
        );
    }
}

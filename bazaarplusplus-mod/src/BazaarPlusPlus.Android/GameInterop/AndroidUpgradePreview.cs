using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using Il2CppBazaarGameClient.Domain.Models.Cards;
using Il2CppTheBazaar;

namespace BazaarPlusPlus.Android.GameInterop;

/// <summary>
/// The touch upgrade preview. The game already renders an item tooltip's next-tier values
/// ("current » next") when the item's <c>CardController</c> is in upgrade preview, as an
/// upgrade pedestal shows it; with the switch on, opening an upgradable item's tooltip enters
/// that state and closing the tooltip leaves it. At most one controller is held at a time, so
/// opening another card, closing the tooltip, or leaving the scene always releases the last one.
/// </summary>
internal static class AndroidUpgradePreview
{
    private const long LoggedTransitions = 20;
    private static Il2Cpp.CardController? _held;
    private static long _entered;
    private static long _exited;

    /// <summary>The switch, read on every tooltip; null before the host binds it.</summary>
    public static Func<bool>? IsEnabled { get; set; }

    public static long Entered => Interlocked.Read(ref _entered);

    public static long Exited => Interlocked.Read(ref _exited);

    /// <summary>Whether <paramref name="card"/>'s controller is the one held in preview.</summary>
    public static bool Holds(Card? card) =>
        card != null
        && _held != null
        && _held.CardData != null
        && _held.CardData.Pointer == card.Pointer;

    /// <summary>Before the game shows a card's tooltip: hold that card in preview, or none.</summary>
    public static void OnShowTooltips(Il2Cpp.CardController controller)
    {
        if (_held != null && (controller == null || _held.Pointer != controller.Pointer))
            Release("replaced");
        if (controller == null || IsEnabled?.Invoke() != true || !IsEligible(controller))
            return;
        if (_held != null)
            return;
        controller.EnterUpgradePreview();
        _held = controller;
        var count = Interlocked.Increment(ref _entered);
        if (count <= LoggedTransitions)
            BppLog.InfoEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.upgrade_preview.entered"),
                ("controller", controller.GetInstanceID()),
                ("count", count)
            );
    }

    /// <summary>The card tooltip closed, the switch turned off, or the scene changed.</summary>
    public static void Release(string reason)
    {
        var held = _held;
        _held = null;
        if (held == null)
            return;
        // A controller destroyed with its scene compares equal to null; there is nothing to undo.
        var alive = held != null && held.WasCollected == false && (UnityEngine.Object)held != null;
        if (alive)
            held!.ExitUpgradePreview();
        var count = Interlocked.Increment(ref _exited);
        if (count <= LoggedTransitions)
            BppLog.InfoEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.upgrade_preview.exited"),
                ("reason", reason),
                ("alive", alive),
                ("count", count)
            );
    }

    private static bool IsEligible(Il2Cpp.CardController controller)
    {
        if (Data.IsInCombat || AppState.CurrentState?.TryCast<ReplayState>() != null)
            return false;
        if (Il2Cpp.BoardManager.Instance?.IsRecapViewOpen == true)
            return false;
        var card = controller.CardData;
        return card?.TryCast<ItemCard>() != null
            && Il2CppTheBazaar.CardExtensions.CanCardUpgrade(card);
    }
}

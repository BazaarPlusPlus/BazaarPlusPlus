using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Mobile;
using Il2CppBazaarGameClient.Domain.Models.Cards;
using Il2CppTheBazaar.UI.Tooltips;

namespace BazaarPlusPlus.Android.GameInterop;

internal static class AndroidUpgradePreview
{
    private const long LoggedTransitions = 20;
    private static Il2Cpp.CardController? _held;
    private static bool _ownsPreview;
    private static bool _selected;
    private static long _entered;
    private static long _exited;

    public static Func<bool>? IsEnabled { get; set; }

    public static long Entered => Interlocked.Read(ref _entered);

    public static long Exited => Interlocked.Read(ref _exited);

    public static bool Holds(Card? card) =>
        card != null
        && _held != null
        && _selected
        && _held.CardData != null
        && _held.CardData.Pointer == card.Pointer;

    public static bool Selected => _selected;
    public static bool CanToggle => _held != null && (_ownsPreview || !_held._isInUpgradePreview);

    public static void Toggle()
    {
        if (!CanToggle || _held == null)
            return;
        _selected = !_selected;
        Apply();
    }

    private static void Apply()
    {
        if (_held == null)
            return;
        if (_selected && !_held._isInUpgradePreview)
        {
            _held.EnterUpgradePreview();
            _ownsPreview = true;
        }
        else if (!_selected && _ownsPreview)
        {
            _held.ExitUpgradePreview();
            _ownsPreview = false;
        }
    }

    public static void OnShowTooltips(Il2Cpp.CardController? controller)
    {
        if (_held != null && (controller == null || _held.Pointer != controller.Pointer))
            Release("replaced");
        if (controller == null || !IsEligible(controller))
        {
            Release("ineligible");
            return;
        }
        if (_held != null)
        {
            Apply();
            return;
        }
        _held = controller;
        _selected = IsEnabled?.Invoke() == true;
        Apply();
        var count = Interlocked.Increment(ref _entered);
        if (count <= LoggedTransitions)
            BppLog.InfoEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.upgrade_preview.entered"),
                ("controller", controller.GetInstanceID()),
                ("count", count)
            );
    }

    // Native hide can return early during a card transition or when the tooltip is locked.
    public static void OnTooltipHidden(TooltipParentComponent? parent)
    {
        var held = _held;
        if (held == null)
            return;
        var shown = parent?.CardTooltipController?.CurrentCard;
        var heldCard = held.WasCollected ? null : held.CardData;
        if (shown != null && heldCard != null && shown.Pointer == heldCard.Pointer)
            return;
        Release("tooltip_hidden");
    }

    public static void Release(string reason)
    {
        var held = _held;
        _held = null;
        if (held == null)
            return;
        // A controller destroyed with its scene compares equal to null; there is nothing to undo.
        var alive = held != null && held.WasCollected == false && (UnityEngine.Object)held != null;
        if (alive && _ownsPreview)
            held!.ExitUpgradePreview();
        _ownsPreview = false;
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
        if (!MobilePlatform.Port.PlayState().PreviewsUpgrade)
            return false;
        var card = controller.CardData;
        return card?.TryCast<ItemCard>() != null
            && Il2CppTheBazaar.CardExtensions.CanCardUpgrade(card);
    }
}

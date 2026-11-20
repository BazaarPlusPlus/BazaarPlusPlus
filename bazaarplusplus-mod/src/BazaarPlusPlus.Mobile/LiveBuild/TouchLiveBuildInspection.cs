using BazaarPlusPlus.Core.Cards.CardRental;
using BazaarPlusPlus.Core.Cards.ItemBoards;
using BazaarPlusPlus.Core.Ui;
using BazaarPlusPlus.Game.LiveBuildPanel.Ui;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Mobile.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Mobile.LiveBuild;

internal sealed class TouchLiveBuildInspection
{
    public const string RentalOwner = "live_build.inspection";

    private readonly GameObject _root;
    private readonly RectTransform _slot;
    private readonly TextMeshProUGUI _name;
    private readonly InspectionText _description;
    private readonly LiveBuildTouchLayout _layout;
    private CardRentalLease<GameCardPreview>? _lease;

    public TouchLiveBuildInspection(
        RectTransform parent,
        TMP_FontAsset font,
        LiveBuildTouchLayout layout,
        NativeSkin skin
    )
    {
        _layout = layout;
        var root = UiKit.Rect(
            "Inspection",
            parent,
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero
        );
        UiKit.Fill(root, InGameTheme.Scrim);
        var dismiss = root.gameObject.AddComponent<Button>();
        dismiss.transition = Selectable.Transition.None;
        UiActions.OnTap(dismiss, "live_build.inspection", () => Close("outside_tap"));
        _root = root.gameObject;

        var area = TouchScreen.Child("CardArea", root, layout.InspectCardArea);
        _slot = UiKit.Rect(
            "CardSlot",
            area,
            new(0.5f, 0.5f),
            new(0.5f, 0.5f),
            Vector2.zero,
            Vector2.zero
        );

        var info = TouchScreen.Child("Info", root, layout.InspectInfo);
        skin.Frame(info);
        // Swallows taps on the panel, which would otherwise reach the dismiss button behind it.
        var swallow = info.gameObject.AddComponent<Button>();
        swallow.transition = Selectable.Transition.None;
        var origin = layout.InspectInfo;
        _name = UiKit.Text(
            TouchScreen.Child("Name", info, Local(layout.InspectName, origin, layout.Gap)),
            font,
            "",
            layout.NameFont,
            TextAlignmentOptions.MidlineLeft,
            UiKit.TextColor
        );
        _name.overflowMode = TextOverflowModes.Ellipsis;
        _description = new InspectionText(
            TouchScreen.Child("Description", info, Local(layout.InspectDetail, origin, layout.Gap)),
            font,
            layout.BodyFont,
            UiKit.TextColor
        );
        var close = TouchScreen.Child("Close", info, Local(layout.InspectClose, origin, 0f));
        skin.Button(
            close,
            font,
            UiKit.CloseGlyph,
            layout.TitleFont,
            layout.LabelInset,
            () => Close("close_button")
        );
        _root.SetActive(false);
    }

    public bool IsOpen => _root.activeSelf;

    public int Leases => _lease != null ? 1 : 0;

    public bool SettingUp => _lease is { State: CardRentalLeaseState.Pending };

    public void Open(BppItemBoardCard card, string source)
    {
        Return();
        var subject = CardRentalSubject.Of(card);
        _name.text = MobilePlatform.Port.CardTitle(card.TemplateId) ?? string.Empty;
        _description.Set(MobilePlatform.Port.CardDescription(subject) ?? string.Empty);
        var scale = _layout.InspectScale(card.DisplaySpan);
        _slot.localScale = new Vector3(scale, scale, 1f);
        _root.SetActive(true);
        _root.transform.SetAsLastSibling();
        _lease = MobilePlatform.Port.CardRental.Rent(subject, _slot, RentalOwner);
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Touch, "touch.live_build.inspected"),
            ("source", source),
            ("size", card.Size),
            ("tier", card.Tier),
            ("scale", Math.Round(scale, 3))
        );
    }

    public bool Close(string reason)
    {
        if (!IsOpen)
            return false;
        Return();
        _root.SetActive(false);
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Touch, "touch.live_build.inspection_closed"),
            ("reason", reason),
            ("rental_leases", MobilePlatform.Port.CardRental.ActiveLeases)
        );
        return true;
    }

    private void Return()
    {
        MobilePlatform.Port.CardRental.Return(_lease);
        _lease = null;
    }

    private static TouchRect Local(TouchRect rect, TouchRect origin, float inset) =>
        new(
            rect.Left - origin.Left + inset,
            rect.Top - origin.Top,
            Math.Max(0f, rect.Width - inset * 2f),
            rect.Height
        );
}

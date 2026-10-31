using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Android.GameInterop.CardRental;
using BazaarPlusPlus.Android.Host;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.Cards.CardRental;
using BazaarPlusPlus.Core.Cards.LiveCards;
using BazaarPlusPlus.Game.OverlayPanels;
using BazaarPlusPlus.Mobile;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace BazaarPlusPlus.Android.Ui;

/// <summary>
/// The Android host's Mobile Platform Port (ADR-0025): the main-thread scope is the host's own
/// pump (<see cref="AndroidMainThread"/>), since IL2CPP installs no synchronization context; a
/// Unity object is alive only while its IL2CPP proxy is not collected; casts and listeners go
/// through Il2CppInterop; the native skin's sprites come from <see cref="AndroidNativeArt"/>.
/// Game reads are the host's own adapters; overlay panels register with
/// <see cref="AndroidOverlayPanels"/>, which exists once the melon has initialized.
/// </summary>
internal sealed class AndroidMobilePlatform(Func<AndroidOverlayPanels?> overlayPanels)
    : IMobilePlatformPort
{
    public IDisposable EnterMainThread() => AndroidMainThread.Host.Enter();

    public bool IsAlive(UnityEngine.Object? value) => value != null && !value.WasCollected;

    public T Cast<T>(UnityEngine.Object value)
        where T : UnityEngine.Object => value.Cast<T>();

    public void SetWordWrapping(TMP_Text label, bool wrap) => label.enableWordWrapping = wrap;

    public void OnClick(Button button, Action onTap) =>
        button.onClick.AddListener((UnityAction)onTap);

    public void OnValueChanged(Toggle toggle, Action<bool> onChanged) =>
        toggle.onValueChanged.AddListener((UnityAction<bool>)onChanged);

    public NativeSkinArt ReadNativeSkinArt()
    {
        var art = new AndroidNativeArt();
        return new NativeSkinArt(art.Button, art.Selected, art.Frame, art.TagFrame);
    }

    public bool IsMainThread => AndroidMainThread.Host.IsCurrentThread;

    public void PostToMainThread(Action action) => AndroidMainThread.Host.Post(_ => action(), null);

    public void RunUiAction(string control, Action handler) =>
        AndroidUiActions.Run(control, handler);

    public TMP_FontAsset? GameFont() => AndroidGameFont.Font;

    public void CoverText(TMP_Text label) => AndroidGameFont.Cover(label);

    public string HeroName(CardHero hero) => AndroidHeroNames.Display(hero);

    public string? CardTitle(Guid templateId) => AndroidCardTemplateText.Title(templateId);

    public string? CardDescription(CardRentalSubject subject) =>
        AndroidCardTemplateText.Description(subject);

    public CardRentalCore<GameCardPreview, RectTransform> CardRental => AndroidCardRental.Core;

    public LiveCardWalk? WalkLiveCards() => AndroidLiveCardSnapshotReader.Walk();

    public IOverlayPanelHandle RegisterOverlayPanel(OverlayPanelRegistration registration) =>
        (
            overlayPanels()
            ?? throw new InvalidOperationException(
                "A Mobile UI panel registered before the Overlay Panel Host existed."
            )
        ).Register(registration);
}

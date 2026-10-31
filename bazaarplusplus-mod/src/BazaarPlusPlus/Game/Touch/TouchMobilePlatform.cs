using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.Cards.CardRental;
using BazaarPlusPlus.Core.Cards.LiveCards;
using BazaarPlusPlus.Game.OverlayPanels;
using BazaarPlusPlus.GameInterop.LiveCards;
using BazaarPlusPlus.GameInterop.Touch;
using BazaarPlusPlus.Mobile;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.Touch;

/// <summary>
/// The Mono host's Mobile Platform Port (ADR-0025), installed on a mobile platform only, on the
/// main thread. Unity's own synchronization context already carries the main thread, so the
/// main-thread scope is empty and a pool-thread completion posts back through the context captured
/// here; liveness, casts and listeners are plain Unity and C#; the native skin's sprites come from
/// <see cref="TouchNativeArt"/>, and game reads from the host's own adapters. Overlay panels
/// register with <see cref="DesktopOverlayPanels"/> without a toggle hotkey: a touch screen has
/// no keyboard (ADR-0024).
/// </summary>
internal sealed class TouchMobilePlatform(Func<DesktopOverlayPanels?> overlayPanels)
    : IMobilePlatformPort
{
    private readonly System.Threading.SynchronizationContext? _mainContext = System
        .Threading
        .SynchronizationContext
        .Current;
    private readonly int _mainThreadId = Environment.CurrentManagedThreadId;

    public IDisposable EnterMainThread() => EmptyScope.Instance;

    public bool IsAlive(UnityEngine.Object? value) => value != null;

    public T Cast<T>(UnityEngine.Object value)
        where T : UnityEngine.Object => (T)value;

    public void SetWordWrapping(TMP_Text label, bool wrap) =>
        label.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;

    public void OnClick(Button button, Action onTap) =>
        button.onClick.AddListener(new UnityAction(onTap));

    public void OnValueChanged(Toggle toggle, Action<bool> onChanged) =>
        toggle.onValueChanged.AddListener(new UnityAction<bool>(onChanged));

    public NativeSkinArt ReadNativeSkinArt()
    {
        var art = new TouchNativeArt();
        return new NativeSkinArt(art.Button, art.Selected, art.Frame, art.TagFrame);
    }

    public bool IsMainThread => Environment.CurrentManagedThreadId == _mainThreadId;

    public void PostToMainThread(Action action)
    {
        if (_mainContext == null || IsMainThread)
        {
            action();
            return;
        }
        _mainContext.Post(_ => action(), null);
    }

    public void RunUiAction(string control, Action handler) => TouchUiActions.Run(control, handler);

    public TMP_FontAsset? GameFont() => TouchFonts.Body();

    public void CoverText(TMP_Text label) => TouchFonts.Cover(label);

    public string HeroName(CardHero hero) => TouchHeroNames.Display(hero);

    public string? CardTitle(Guid templateId) => TouchCardText.Title(templateId);

    public string? CardDescription(CardRentalSubject subject) => TouchCardText.Description(subject);

    public CardRentalCore<GameCardPreview, RectTransform> CardRental => TouchCardRental.Core;

    public LiveCardWalk? WalkLiveCards() => LiveCardSnapshotReader.Walk();

    public IOverlayPanelHandle RegisterOverlayPanel(OverlayPanelRegistration registration) =>
        (
            overlayPanels()
            ?? throw new InvalidOperationException(
                "A Mobile UI panel registered before the Overlay Panel Host mounted."
            )
        ).Register(registration, toggleHotkey: null);

    public void OnTextChanged(TMP_InputField field, Action<string> onChanged) =>
        field.onValueChanged.AddListener(new UnityAction<string>(onChanged));

    // The mobile build has no system switch for animations: they are always on.
    public bool AnimationsEnabled() => true;

    public string? GameText(string key)
    {
        try
        {
            return new TheBazaar.Utilities.LocalizableText(key).GetLocalizedText();
        }
        catch (Exception)
        {
            // The localization service may come up later.
            return null;
        }
    }

    public string? TooltipTag(string tag)
    {
        try
        {
            return TheBazaar.Data.TooltipTypography?.ColorTag(tag);
        }
        catch (Exception)
        {
            // Typography can be absent during a scene or locale change.
            return null;
        }
    }

    public string EncounterName(Guid templateId, string fallback) =>
        TouchEncounterNames.Display(templateId, fallback);

    public Task<Sprite?> HeroPortrait(CardHero hero) => TouchPortraits.Hero(hero);

    public Task<Sprite?> EncounterPortrait(Guid templateId) => TouchPortraits.Encounter(templateId);

    public (string? Text, float? Cooldown, float? Ammo) CardPreview(CardRentalSubject subject) =>
        TouchCardText.Preview(subject);

    public Func<CardRentalSubject, CardEnchantment, string?> EnchantmentEffects(Guid templateId)
    {
        var template = TouchCardText.Template(templateId);
        return (subject, enchantment) =>
            TouchCardText.EnchantmentEffect(template, subject, enchantment);
    }

    private sealed class EmptyScope : IDisposable
    {
        public static readonly EmptyScope Instance = new();

        public void Dispose() { }
    }
}

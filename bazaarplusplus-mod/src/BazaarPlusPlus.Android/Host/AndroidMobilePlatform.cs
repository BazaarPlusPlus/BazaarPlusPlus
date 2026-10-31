using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Android.GameInterop.CardRental;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.Cards.CardRental;
using BazaarPlusPlus.Core.Cards.LiveCards;
using BazaarPlusPlus.Core.Ui;
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.OverlayPanels;
using BazaarPlusPlus.Mobile;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace BazaarPlusPlus.Android.Host;

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
    private readonly AndroidVaultBounds _vault = new();

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

    public void OnTextChanged(TMP_InputField field, Action<string> onChanged) =>
        field.onValueChanged.AddListener((UnityAction<string>)onChanged);

    public bool AnimationsEnabled() => AndroidMotion.AnimationsEnabled();

    public string? GameText(string key)
    {
        try
        {
            return new Il2CppTheBazaar.Utilities.LocalizableText(key).GetLocalizedText();
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
            return Il2CppTheBazaar.Data.TooltipTypography?.ColorTag(tag);
        }
        catch (Exception)
        {
            // Typography can be absent during a scene or locale change.
            return null;
        }
    }

    public string EncounterName(Guid templateId, string fallback) =>
        AndroidEncounterNames.Display(templateId, fallback);

    public Task<Sprite?> HeroPortrait(CardHero hero) => AndroidPortraits.Hero(hero);

    public Task<Sprite?> EncounterPortrait(Guid templateId) =>
        AndroidPortraits.Encounter(templateId);

    public (string? Text, float? Cooldown, float? Ammo) CardPreview(CardRentalSubject subject) =>
        AndroidCardTemplateText.Preview(subject);

    public Func<CardRentalSubject, CardEnchantment, string?> EnchantmentEffects(Guid templateId)
    {
        var template = AndroidCardTemplateText.Template(templateId);
        return (subject, enchantment) =>
            AndroidCardTemplateText.EnchantmentEffect(template, subject, enchantment);
    }

    public Task<Sprite?>? RankBadge(string? rank) => AndroidRankArt.Rank(rank);

    public Task<Sprite?>? RunTrophy(RunOutcomeTier tier) => AndroidRankArt.Trophy(tier);

    public INativeSkillOverlay NativeSkillOverlay(int sortingOrder) =>
        new AndroidNativeSkillOverlay(sortingOrder);

    public string? OpenPanelId => overlayPanels()?.OpenPanelId;

    public Button? FindSettingsButton(out bool inRun) =>
        AndroidNativeHud.FindSettingsButton(out inRun);

    public TouchRect ScreenBounds(RectTransform rect, Canvas rootCanvas, float referenceHeight) =>
        AndroidNativeHud.ScreenBounds(rect, rootCanvas, referenceHeight);

    public NativeRoundArt ReadRoundArt(Button gear, RectTransform rect, Canvas rootCanvas)
    {
        var art = AndroidNativeArt.RoundArtFor(gear, rect, rootCanvas);
        return new NativeRoundArt(
            art.Layers.Select(layer => new NativeArtLayer(layer.Sprite, layer.Color, layer.Bounds))
                .ToArray(),
            art.Icon
        );
    }

    public bool NativeHudBusy() => AndroidNativeHud.HasPopup || AndroidNativeHud.IsInspectingCard;

    public bool IsPlaying() =>
        Il2CppTheBazaar.Data.IsInCombat
        || Il2CppTheBazaar.AppState.CurrentState?.TryCast<Il2CppTheBazaar.ReplayState>() != null;

    public bool TryReadVault(float referenceHeight, out TouchRect vault) =>
        _vault.TryRead(referenceHeight, out vault);
}

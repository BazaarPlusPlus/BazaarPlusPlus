using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.Cards.CardRental;
using BazaarPlusPlus.Core.Cards.LiveCards;
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.OverlayPanels;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Mobile;

/// <summary>
/// The Mobile Platform Port (the mod's GLOSSARY.md; ADR-0025 decision 3): what a host gives the
/// Mobile UI to reach its runtime. Code in this tree reaches the host only through it, never
/// through <c>#if</c>. Each host implements it once and installs it through
/// <see cref="MobilePlatform.Install"/> before any Mobile UI is built.
/// </summary>
internal interface IMobilePlatformPort
{
    /// <summary>
    /// Makes an <c>await</c> started in the scope resume on the main thread, until the scope is
    /// disposed. A host whose main thread already carries that context returns an empty scope.
    /// </summary>
    IDisposable EnterMainThread();

    /// <summary>
    /// Whether <paramref name="value"/> is a live Unity object: neither destroyed nor, on an
    /// IL2CPP host, collected under its managed proxy.
    /// </summary>
    bool IsAlive(UnityEngine.Object? value);

    /// <summary><paramref name="value"/> as <typeparamref name="T"/>, which it must be.</summary>
    T Cast<T>(UnityEngine.Object value)
        where T : UnityEngine.Object;

    /// <summary>Lets <paramref name="label"/> wrap at its rect's width, or keeps it on one line.</summary>
    void SetWordWrapping(TMP_Text label, bool wrap);

    /// <summary>Runs <paramref name="onTap"/> each time <paramref name="button"/> is clicked.</summary>
    void OnClick(Button button, Action onTap);

    /// <summary>Runs <paramref name="onChanged"/> with each new value of <paramref name="toggle"/>.</summary>
    void OnValueChanged(Toggle toggle, Action<bool> onChanged);

    /// <summary>
    /// The game sprites the native skin borrows, from a host read of the game's own popups and
    /// tooltips; a sprite the game has not loaded is null.
    /// </summary>
    NativeSkinArt ReadNativeSkinArt();

    /// <summary>Whether the caller runs on the main thread.</summary>
    bool IsMainThread { get; }

    /// <summary>Runs <paramref name="action"/> on the main thread: now when already there, else later.</summary>
    void PostToMainThread(Action action);

    /// <summary>
    /// Runs a control's handler as the host runs its own controls': inside the main-thread scope,
    /// with an exception reported instead of escaping into uGUI's event loop.
    /// </summary>
    void RunUiAction(string control, Action handler);

    /// <summary>The game's body font, or null while the game has not loaded it yet.</summary>
    TMP_FontAsset? GameFont();

    /// <summary>Adds the game's CJK fallbacks when <paramref name="label"/>'s text needs them.</summary>
    void CoverText(TMP_Text label);

    /// <summary>The game's localized name for <paramref name="hero"/>, or its English name.</summary>
    string HeroName(CardHero hero);

    /// <summary>A card template's localized title, or null when the game cannot name it.</summary>
    string? CardTitle(Guid templateId);

    /// <summary>A card's text as the game's tooltip renders it at its tier and enchantment.</summary>
    string? CardDescription(CardRentalSubject subject);

    /// <summary>
    /// The host's one native card rental. The host ticks it every frame and releases it on a
    /// scene load; the Mobile UI only rents and returns.
    /// </summary>
    CardRentalCore<GameCardPreview, RectTransform> CardRental { get; }

    /// <summary>One walk of the live run's cards (board, stash, shop), or null outside a run.</summary>
    LiveCardWalk? WalkLiveCards();

    /// <summary>
    /// Registers a Main Overlay Panel with the host's Overlay Panel Host (ADR-0022), which opens,
    /// ticks and closes it. Main thread.
    /// </summary>
    IOverlayPanelHandle RegisterOverlayPanel(OverlayPanelRegistration registration);

    /// <summary>Runs <paramref name="onChanged"/> with each new text of <paramref name="field"/>.</summary>
    void OnTextChanged(TMP_InputField field, Action<string> onChanged);

    /// <summary>Whether the system lets the UI animate; read on each interaction.</summary>
    bool AnimationsEnabled();

    /// <summary>
    /// The game's string table entry for the English source text <paramref name="key"/>, or null
    /// while the table cannot be read.
    /// </summary>
    string? GameText(string key);

    /// <summary>
    /// <paramref name="tag"/>'s label in the game's tooltip typography, with its colour, or null
    /// while the typography is absent.
    /// </summary>
    string? TooltipTag(string tag);

    /// <summary>A merchant's or trainer's localized name, or <paramref name="fallback"/>.</summary>
    string EncounterName(Guid templateId, string fallback);

    /// <summary>The hero's default portrait, or null when the game has none.</summary>
    Task<Sprite?> HeroPortrait(CardHero hero);

    /// <summary>The encounter template's portrait, or null when the game has none.</summary>
    Task<Sprite?> EncounterPortrait(Guid templateId);

    /// <summary>
    /// A card's text, out-of-combat cooldown and ammo at its tier, from one tooltip; each is null
    /// when the game cannot build or lacks it.
    /// </summary>
    (string? Text, float? Cooldown, float? Ammo) CardPreview(CardRentalSubject subject);

    /// <summary>
    /// The in-game enchant preview's renderer for one card template, looked up once: the
    /// enchantment-only effect at a subject's tier, or null when it cannot be rendered.
    /// </summary>
    Func<CardRentalSubject, CardEnchantment, string?> EnchantmentEffects(Guid templateId);

    /// <summary>
    /// The game's badge for a stored rank name, held in its Global asset scope; null when the
    /// name is no game rank.
    /// </summary>
    Task<Sprite?>? RankBadge(string? rank);

    /// <summary>The game's trophy for a finished run; null for an outcome that earns none.</summary>
    Task<Sprite?>? RunTrophy(RunOutcomeTier tier);

    /// <summary>
    /// The game's own mobile skill overlay, which sorts at <paramref name="sortingOrder"/> while
    /// open so it stands over the panel that opened it.
    /// </summary>
    INativeSkillOverlay NativeSkillOverlay(int sortingOrder);
}

/// <summary>
/// The game's own mobile skill overlay (<c>MobileSkillOverlayController</c>) over skills that are
/// on no live board, as the end-of-run summary opens it. A host adapter, since the controller is
/// a global-namespace game type. Main thread only.
/// </summary>
internal interface INativeSkillOverlay
{
    /// <summary>Requested or showing; the overlay owns input until it closes.</summary>
    bool IsOpen { get; }

    /// <summary>False when the game has no popup manager or none of the skills builds a card.</summary>
    bool Open(IReadOnlyList<CardRentalSubject> skills, string source);

    /// <summary>Every frame while the opener is visible: notices the overlay showing and closing.</summary>
    void Tick();

    /// <summary>False when it was not open.</summary>
    bool Close(string reason);
}

/// <summary>
/// The game's popup button at rest and clicked (<see cref="Button"/>, <see cref="Selected"/>), and
/// its tooltip frame and tag frame (<see cref="Frame"/>, <see cref="TagFrame"/>).
/// </summary>
internal sealed record NativeSkinArt(
    Sprite? Button,
    Sprite? Selected,
    Sprite? Frame,
    Sprite? TagFrame
);

/// <summary>The host's <see cref="IMobilePlatformPort"/>, installed once at startup.</summary>
internal static class MobilePlatform
{
    private static IMobilePlatformPort? _port;

    public static IMobilePlatformPort Port =>
        _port
        ?? throw new InvalidOperationException(
            "The host built Mobile UI before it installed its Mobile Platform Port."
        );

    public static void Install(IMobilePlatformPort port) =>
        _port = port ?? throw new ArgumentNullException(nameof(port));
}

using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.Cards.CardRental;
using BazaarPlusPlus.Core.Cards.LiveCards;
using BazaarPlusPlus.Core.Ui;
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.OverlayPanels;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Mobile;

internal interface IMobilePlatformPort
{
    // Awaits started in this scope resume on the main thread. A host whose main thread
    // already carries the context returns an empty scope.
    IDisposable EnterMainThread();

    NativeSkinArt ReadNativeSkinArt();

    bool IsMainThread { get; }

    // Runs inline when called on the main thread.
    void PostToMainThread(Action action);

    // Enter the main-thread scope and report exceptions before they can escape into uGUI.
    void RunUiAction(string control, Action handler);

    TMP_FontAsset? GameFont();

    void CoverText(TMP_Text label);

    string HeroName(CardHero hero);

    string? CardTitle(Guid templateId);

    string? CardDescription(CardRentalSubject subject);

    // The host ticks the rental and releases it on scene load.
    CardRentalCore<GameCardPreview, RectTransform> CardRental { get; }

    LiveCardWalk? WalkLiveCards();

    // Main thread only.
    IOverlayPanelHandle RegisterOverlayPanel(OverlayPanelRegistration registration);

    bool AnimationsEnabled();

    // The string table is keyed by English source text.
    string? GameText(string key);

    string? TooltipTag(string tag);

    string EncounterName(Guid templateId, string fallback);

    Task<Sprite?> HeroPortrait(CardHero hero);

    Task<Sprite?> EncounterPortrait(Guid templateId);

    (string? Text, float? Cooldown, float? Ammo) CardPreview(CardRentalSubject subject);

    Func<CardRentalSubject, CardEnchantment, string?> EnchantmentEffects(Guid templateId);

    // Rank badges live in the game's Global asset scope.
    Task<Sprite?>? RankBadge(string? rank);

    Task<Sprite?>? RunTrophy(RunOutcomeTier tier);

    INativeSkillOverlay NativeSkillOverlay(int sortingOrder);

    string? OpenPanelId { get; }

    Button? FindSettingsButton(out bool inRun);

    // Bounds are measured from the top in canvas units at referenceHeight.
    TouchRect ScreenBounds(RectTransform rect, Canvas rootCanvas, float referenceHeight);

    // Sample the button at rest and exclude its icon from the sprite layers.
    NativeRoundArt ReadRoundArt(Button gear, RectTransform rect, Canvas rootCanvas);

    // A settings button can remain active behind the scene loading screen.
    // Include scene transitions in the native HUD state so shortcuts stay hidden.
    NativeHudState ReadNativeHud();

    TouchPlayState PlayState();

    // The vault rect uses canvas units at referenceHeight.
    bool TryReadVault(float referenceHeight, out TouchRect vault);
}

// Layer and icon rects are fractions of the native button's bounds.
internal sealed record NativeRoundArt(IReadOnlyList<NativeArtLayer> Layers, TouchRect? Icon);

internal readonly record struct NativeArtLayer(Sprite Sprite, Color Color, TouchRect Bounds);

// Main thread only.
internal interface INativeSkillOverlay
{
    // Includes a pending request because the overlay already owns input.
    bool IsOpen { get; }

    bool Open(IReadOnlyList<CardRentalSubject> skills, string source);

    // Tick every frame while the opener is visible to track native show and close.
    void Tick();

    bool Close(string reason);
}

internal sealed record NativeSkinArt(
    Sprite? Button,
    Sprite? Selected,
    Sprite? Frame,
    Sprite? TagFrame
);

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

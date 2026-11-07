using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Android.Lobby;
using BazaarPlusPlus.Mobile.NativeAdjustment;
using BazaarPlusPlus.Mobile.Previews;
using Il2CppTheBazaar.Tooltips;

namespace BazaarPlusPlus.Android.Host;

internal sealed record AndroidInteractionFeatures(
    AndroidTooltipPreviewHost TooltipPreviewHost,
    TouchPreviewSwitches PreviewSwitches,
    TouchEnchantPreview<CardTooltipData> EnchantPreview,
    TouchScreenFill ScreenFill,
    Game.AndroidEncounterPreview EncounterPreview,
    Game.AndroidAggregateItemPreview AggregateItemPreview,
    Game.AndroidQuestPreview QuestPreview,
    Game.AndroidCombatStatus CombatStatus,
    AndroidVersionCheck VersionCheck,
    AndroidUpdatePrompt UpdatePrompt
);

#nullable enable
using System.Runtime.CompilerServices;
using BazaarPlusPlus.Game.ChineseTranslations;
using BazaarPlusPlus.Localization;
using HarmonyLib;
using TheBazaar.UI.MainMenu;

namespace BazaarPlusPlus.Patches.Lobby;

[HarmonyPatch(typeof(MainMenuBaseView), "UpdateHeroName")]
internal static class MainMenuHeroNameSizePatch
{
    private const float SimplifiedChineseScale = 0.75f;
    private static readonly ConditionalWeakTable<TMP_Text, OriginalSize> OriginalSizes = new();

    [HarmonyPostfix]
    private static void Postfix(BazaarButtonController ____heroSelectButton)
    {
        if (____heroSelectButton == null || ____heroSelectButton.ButtonText == null)
            return;

        var text = ____heroSelectButton.ButtonText;
        var original = OriginalSizes.GetValue(text, label => new OriginalSize(label));
        var scale = ChineseTranslationOverrides.AppliesTo(L.CurrentLanguageCode)
            ? SimplifiedChineseScale
            : 1f;
        text.fontSize = original.FontSize * scale;
        text.fontSizeMin = original.FontSizeMin * scale;
        text.fontSizeMax = original.FontSizeMax * scale;
    }

    private sealed class OriginalSize(TMP_Text text)
    {
        public float FontSize { get; } = text.fontSize;
        public float FontSizeMin { get; } = text.fontSizeMin;
        public float FontSizeMax { get; } = text.fontSizeMax;
    }
}

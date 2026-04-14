#nullable enable
using System.Globalization;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.Input;
using BazaarPlusPlus.Game.Settings;
using TheBazaar;

namespace BazaarPlusPlus.Game.ItemEnchantPreview;

internal static class ItemEnchantPreviewDisplayState
{
    private const int SectionTitleSizePercent = 62;
    private const int HintSizePercent = 48;
    private const string SectionTitleColorHex = "D0AF73";
    private const string HintColorHex = "A99167";

    private static readonly LocalizedTextSet SectionTitle = new(
        "Enchant Preview",
        "附魔预览",
        "Verzauberungsvorschau",
        "Previa de encantamento",
        "마법부여 미리보기",
        "Anteprima incantamento"
    );

    private static readonly LocalizedTextSet HoldToShowHint = new(
        "Hold {0} to show",
        "按住 {0} 显示",
        "Halte {0}, um sie anzuzeigen",
        "Segure {0} para mostrar",
        "{0} 키를 눌러 표시",
        "Tieni premuto {0} per mostrare"
    );

    private static readonly LocalizedTextSet ReleaseToShowHint = new(
        "Release {0} to show",
        "松开 {0} 显示",
        "Lasse {0} los, um sie anzuzeigen",
        "Solte {0} para mostrar",
        "{0} 키를 놓아 표시",
        "Rilascia {0} per mostrare"
    );

    private static readonly LocalizedTextSet ReleaseToHideHint = new(
        "Release {0} to hide",
        "松开 {0} 隐藏",
        "Lasse {0} los, um sie auszublenden",
        "Solte {0} para ocultar",
        "{0} 키를 놓아 숨기기",
        "Rilascia {0} per nascondere"
    );

    private static readonly LocalizedTextSet ToggleHint = new(
        "{0} hides this section",
        "{0} 可隐藏这部分",
        "{0} blendet diesen Abschnitt aus",
        "{0} oculta esta secao",
        "{0} 키로 이 섹션 숨기기",
        "{0} nasconde questa sezione"
    );

    internal static bool IsDefaultVisible()
    {
        return BppRuntimeHost.Config.EnchantPreviewAlwaysShowConfig?.Value ?? true;
    }

    internal static bool IsToggleHeld()
    {
        return BppHotkeyService.IsHeld(BppHotkeyActionId.HoldEnchantPreview);
    }

    internal static bool IsPreviewSuppressed()
    {
        return Data.IsInCombat || BppHotkeyService.IsHeld(BppHotkeyActionId.HoldUpgradePreview);
    }

    internal static bool IsPreviewVisible()
    {
        if (IsPreviewSuppressed())
            return false;

        return IsDefaultVisible() ^ IsToggleHeld();
    }

    internal static string BuildInlineHintMarkup(bool previewVisible)
    {
        var bindingDisplay = BppHotkeyService.GetBindingDisplay(BppHotkeyActionId.HoldEnchantPreview);
        var languageCode = PlayerPreferences.Data.LanguageCode;
        var highlightedBinding = $"<b>{bindingDisplay}</b>";
        var pattern = previewVisible
            ? IsDefaultVisible()
                ? ToggleHint.Resolve(languageCode)
                : ReleaseToHideHint.Resolve(languageCode)
            : IsDefaultVisible()
                ? ReleaseToShowHint.Resolve(languageCode)
                : HoldToShowHint.Resolve(languageCode);
        var title = SectionTitle.Resolve(languageCode);
        var text = string.Format(CultureInfo.InvariantCulture, pattern, highlightedBinding);
        return
            $"<size={SectionTitleSizePercent}%><color=#{SectionTitleColorHex}>{title}</color></size> "
            + $"<size={HintSizePercent}%><color=#{HintColorHex}>({text})</color></size>";
    }
}

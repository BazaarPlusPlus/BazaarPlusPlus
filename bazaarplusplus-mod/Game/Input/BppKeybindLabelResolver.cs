using BazaarPlusPlus.Game.Settings;

namespace BazaarPlusPlus.Game.Input;

internal static class BppKeybindLabelResolver
{
    private static readonly LocalizedTextSet EnchantPreviewLabel = new(
        "Show Enchant Preview",
        "显示附魔预览",
        "Verzauberungsvorschau anzeigen",
        "Mostrar previa de encantamento",
        "마법부여 미리보기 표시",
        "Mostra anteprima incantamento"
    );

    private static readonly LocalizedTextSet UpgradePreviewLabel = new(
        "Show Upgrade Preview",
        "显示升级预览",
        "Upgrade-Vorschau anzeigen",
        "Mostrar previa de upgrade",
        "강화 미리보기 표시",
        "Mostra anteprima upgrade"
    );

    private static readonly LocalizedTextSet RebindPrompt = new(
        "Press a key or mouse button",
        "按下一个键或鼠标按钮",
        "Taste oder Maustaste druecken",
        "Pressione uma tecla ou botao do mouse",
        "키 또는 마우스 버튼을 누르세요",
        "Premi un tasto o un pulsante del mouse"
    );

    private static readonly LocalizedTextSet UnsupportedKey = new(
        "Unsupported key",
        "不支持该按键",
        "Nicht unterstuetzte Taste",
        "Tecla nao suportada",
        "지원되지 않는 키",
        "Tasto non supportato"
    );

    internal static string ResolveActionLabel(BppHotkeyActionId actionId, string languageCode)
    {
        return actionId switch
        {
            BppHotkeyActionId.HoldEnchantPreview => EnchantPreviewLabel.Resolve(languageCode),
            BppHotkeyActionId.HoldUpgradePreview => UpgradePreviewLabel.Resolve(languageCode),
            _ => actionId.ToString(),
        };
    }

    internal static string ResolveRebindPrompt(string languageCode)
    {
        return RebindPrompt.Resolve(languageCode);
    }

    internal static string ResolveUnsupportedKey(string languageCode)
    {
        return UnsupportedKey.Resolve(languageCode);
    }
}

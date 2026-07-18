#nullable enable
using BazaarPlusPlus.Localization;

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

    private static readonly LocalizedTextSet ToggleCollectionPanelLabel = new(
        "Toggle Card Collection",
        "开关卡牌图鉴",
        "Kartensammlung umschalten",
        "Alternar colecao de cartas",
        "카드 도감 열기/닫기",
        "Mostra/nascondi collezione carte"
    );

    private static readonly LocalizedTextSet ToggleLiveBuildPanelLabel = new(
        "Toggle Final Build",
        "开关终局阵容",
        "Endaufstellung umschalten",
        "Alternar build final",
        "최종 빌드 열기/닫기",
        "Mostra/nascondi build finale"
    );

    private static readonly LocalizedTextSet ToggleHistoryPanelLabel = new(
        "Toggle Game History",
        "开关对局历史",
        "Spielverlauf umschalten",
        "Alternar historico de partidas",
        "게임 전적 열기/닫기",
        "Mostra/nascondi cronologia partite"
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

    private static readonly LocalizedTextSet ConflictWarningFormat = new(
        "{0} conflicts with {1}",
        "{0} 与 {1} 冲突",
        "{0} steht in Konflikt mit {1}",
        "{0} conflita com {1}",
        "{0}이(가) {1}과(와) 충돌합니다",
        "{0} in conflitto con {1}"
    );

    internal static string ResolveActionLabel(BppHotkeyActionId actionId, string languageCode)
    {
        return actionId switch
        {
            BppHotkeyActionId.HoldEnchantPreview => EnchantPreviewLabel.Resolve(
                languageCode,
                L.CurrentMode
            ),
            BppHotkeyActionId.HoldUpgradePreview => UpgradePreviewLabel.Resolve(
                languageCode,
                L.CurrentMode
            ),
            BppHotkeyActionId.ToggleCollectionPanel => ToggleCollectionPanelLabel.Resolve(
                languageCode,
                L.CurrentMode
            ),
            BppHotkeyActionId.ToggleLiveBuildPanel => ToggleLiveBuildPanelLabel.Resolve(
                languageCode,
                L.CurrentMode
            ),
            BppHotkeyActionId.ToggleHistoryPanel => ToggleHistoryPanelLabel.Resolve(
                languageCode,
                L.CurrentMode
            ),
            _ => actionId.ToString(),
        };
    }

    internal static string ResolveRebindPrompt(string languageCode)
    {
        return RebindPrompt.Resolve(languageCode, L.CurrentMode);
    }

    internal static string ResolveUnsupportedKey(string languageCode)
    {
        return UnsupportedKey.Resolve(languageCode, L.CurrentMode);
    }

    internal static string ResolveConflictWarning(
        BppHotkeyActionId actionId,
        BppHotkeyActionId conflictingActionId,
        string languageCode
    )
    {
        return string.Format(
            ConflictWarningFormat.Resolve(languageCode, L.CurrentMode),
            ResolveActionLabel(actionId, languageCode),
            ResolveActionLabel(conflictingActionId, languageCode)
        );
    }
}

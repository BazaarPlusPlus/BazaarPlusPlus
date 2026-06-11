#nullable enable

namespace BazaarPlusPlus.Game.Settings;

internal enum BppSettingsDockSide
{
    LeftOfAnchor,
    RightOfAnchor,
    AboveAnchor,
}

internal enum BppSettingsDockPanelDirection
{
    UpLeft,
    UpRight,
}

internal readonly struct BppSettingsDockPlacement
{
    private const float DefaultSiblingGap = 18f;

    private BppSettingsDockPlacement(
        string key,
        BppSettingsDockSide side,
        BppSettingsDockPanelDirection panelDirection,
        float siblingGap,
        BppDockButtonIconKind buttonIconKind
    )
    {
        Key = key;
        Side = side;
        PanelDirection = panelDirection;
        SiblingGap = siblingGap;
        ButtonIconKind = buttonIconKind;
    }

    internal string Key { get; }

    internal BppSettingsDockSide Side { get; }

    internal BppSettingsDockPanelDirection PanelDirection { get; }

    internal float SiblingGap { get; }

    internal BppDockButtonIconKind ButtonIconKind { get; }

    internal string DockButtonObjectName => $"BPP_SettingsDockButton_{Key}";

    internal string PanelObjectName => $"BPP_SettingsDockPanel_{Key}";

    internal static BppSettingsDockPlacement LeftOfSettingButton(
        string key,
        BppDockButtonIconKind buttonIconKind
    ) =>
        new(
            key,
            BppSettingsDockSide.LeftOfAnchor,
            BppSettingsDockPanelDirection.UpLeft,
            DefaultSiblingGap,
            buttonIconKind
        );

    internal static BppSettingsDockPlacement AboveSettingButton(
        string key,
        BppDockButtonIconKind buttonIconKind
    ) =>
        new(
            key,
            BppSettingsDockSide.AboveAnchor,
            BppSettingsDockPanelDirection.UpLeft,
            DefaultSiblingGap,
            buttonIconKind
        );
}

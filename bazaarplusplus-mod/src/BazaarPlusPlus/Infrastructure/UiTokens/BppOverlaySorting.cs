#nullable enable

namespace BazaarPlusPlus.Infrastructure.UiTokens;

internal static class BppOverlaySorting
{
    public const int PanelUiToolkit = 26;
    public const int NativeCardPreview = 27;
    public const int PanelForeground = 28;

    // Used only by BppOverlayPanelMutex to group mutually exclusive full-screen panels.
    public const int MainOverlayPanelBand = NativeCardPreview;
}

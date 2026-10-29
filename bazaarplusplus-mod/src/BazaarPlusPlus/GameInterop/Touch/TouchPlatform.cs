#nullable enable
using TheBazaar.Utilities;

namespace BazaarPlusPlus.GameInterop.Touch;

/// <summary>
/// Whether the game runs its mobile build's behaviour (ADR-0024). The game decides it from
/// <c>Application.platform</c> (<see cref="ApplicationUtils.IsMobilePlatform"/>), so the Mac build
/// on the iOS engine already takes its mobile branches; BPP follows the same answer.
/// </summary>
internal static class TouchPlatform
{
    private static bool? _isMobile;

    public static bool IsMobile => _isMobile ??= Read();

    private static bool Read()
    {
        try
        {
            return ApplicationUtils.IsMobilePlatform();
        }
        catch (Exception)
        {
            return false;
        }
    }
}

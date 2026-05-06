#nullable enable
namespace BazaarPlusPlus.Game.MonsterPreview;

internal static class MonsterPreviewFeature
{
    public static bool IsEnabled => true;

    public static bool UseNativePreview => true;

    public static bool UseBoardOnlyNativePreview => !UseNativePreview;
}

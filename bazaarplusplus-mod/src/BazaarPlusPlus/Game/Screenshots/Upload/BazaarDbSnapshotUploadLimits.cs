#nullable enable
namespace BazaarPlusPlus.Game.Screenshots.Upload;

internal static class BazaarDbSnapshotUploadLimits
{
    public const int MaxUploadImageBytes = 2 * 1024 * 1024;
    public const int ServerMaxJsonBodyBytes = 4 * 1024 * 1024;

    public static int Base64Length(int byteLength)
    {
        return byteLength <= 0 ? 0 : ((byteLength + 2) / 3) * 4;
    }
}

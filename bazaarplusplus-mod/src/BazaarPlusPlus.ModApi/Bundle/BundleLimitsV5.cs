#nullable enable
namespace BazaarPlusPlus.ModApi.Bundle;

public static class BundleLimitsV5
{
    public const int BundleVersion = 5;
    public const int RunFormatVersion = 5;
    public const int PrefixBytes = 16;
    public const int MaxBundleBytes = 8_388_607;
    public const int MaxManifestBytes = 2_097_152;
    public const int MaxRunBytes = 2_097_151;
    public const int MaxScreenshotBytes = 1_048_576;
    public const int MaxProjectionBytes = 524_288;
    public const int MaxBattlesPerBundle = 30;
    public const long MaxSafeInteger = 9_007_199_254_740_991;
    public const long MaxCreatedAtMs = 8_640_000_000_000_000;

    public const string BundleContentType = "application/x-bpp-bundle-v5";
    public const string RunContentType = "application/x-bpp-run-v5";
    public const string JpegContentType = "image/jpeg";
    public const string WebpContentType = "image/webp";
}

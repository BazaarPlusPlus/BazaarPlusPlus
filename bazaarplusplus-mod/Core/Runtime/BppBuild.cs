namespace BazaarPlusPlus.Core.Runtime;

internal static class BppBuild
{
#if DEBUG
    internal static bool IsDebug => true;
#else
    internal static bool IsDebug => false;
#endif
}

#nullable enable
namespace BazaarPlusPlus.Core.Runtime;

public static class BppBuild
{
#if DEBUG
    public static bool IsDebug => true;
#else
    public static bool IsDebug => false;
#endif
}

#nullable enable
using TheBazaar;

namespace BazaarPlusPlus.Core.Runtime;

/// <summary>
/// <c>Data.GetStatic()</c> returns <c>Task.FromResult(manager)</c>, so reading the
/// underlying value is synchronous. This helper makes that intent explicit at the
/// call sites and centralises the pattern so a future change to the upstream API
/// only needs updating in one place.
/// </summary>
internal static class BppStaticDataAccess
{
    public static object? TryGet()
    {
        if (!Data.IsManagerCreated())
            return null;

        var task = Data.GetStatic();
        return task.IsCompletedSuccessfully ? task.Result : null;
    }
}

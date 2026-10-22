#nullable enable
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Storage.Paths;

namespace BazaarPlusPlus.Game.BundlePipeline;

/// <summary>
/// Binds the desktop composer and screenshot encoder, which read game-typed PvP evidence and
/// ImageSharp, into the shared <see cref="BundleSealCoordinator"/>.
/// </summary>
internal static class DesktopBundleSealCoordinator
{
    // Tests inject failures at named seal stages; production passes null.
    internal static BundleSealCoordinator Create(
        IBppServices services,
        Action<BundleSealStage, string>? faultProbe = null
    )
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));
        var dataRoot = services.Paths.RequireDataRoot();
        return new BundleSealCoordinator(
            services,
            new RunPayloadComposer(
                PathConstants.RunLogDatabase(dataRoot),
                PathConstants.CombatReplays(dataRoot)
            ),
            new BundleScreenshotEncoder(),
            faultProbe
        );
    }
}

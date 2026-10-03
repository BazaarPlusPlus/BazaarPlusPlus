#nullable enable
using Xunit;
using static Architecture.Tests.ArchitectureRules;

namespace Architecture.Tests;

/// <summary>
/// Keeps the shared native-tooltip adapters feature-neutral and their native ownership out of
/// features. Gate transitions, geometry settling, retry behavior, and teardown are executable
/// behavior in NativePairedTooltipHost.Tests.
/// </summary>
public sealed class NativePairedTooltipArchitectureTests
{
    private const string TooltipParent = "TheBazaar.UI.Tooltips.TooltipParentComponent";

    [Fact]
    public void Shared_tooltip_adapters_do_not_import_features_or_patches()
    {
        Holds(
            "Shared tooltip adapters must remain feature-neutral.",
            build =>
            {
                _ = build.TypesIn("BazaarPlusPlus.Game");
                _ = build.TypesIn("BazaarPlusPlus.Patches");
                return References(
                    build.TypesIn("BazaarPlusPlus.GameInterop.Tooltips"),
                    InNamespace("BazaarPlusPlus.Game", "BazaarPlusPlus.Patches")
                );
            }
        );
    }

    [Fact]
    public void Screenshot_and_replay_features_do_not_own_native_tooltip_suppression()
    {
        var universe = CompiledArtifacts.Universe;
        var nativeOwnership = new[]
        {
            universe.RequireMember(TooltipParent, "UnlockAllLockedTooltipControllers"),
            universe.RequireMember(TooltipParent, "HideCardTooltipController"),
            universe.RequireMember(TooltipParent, "HideSecondaryCardTooltipController"),
            universe.RequireMember(TooltipParent, "HideAuxiliaryTooltipController"),
            universe.RequireMember(
                "TheBazaar.SequenceFramework.CanvasHiderComponent",
                "SetVisibility"
            ),
            universe.RequireMember("TheBazaar.UI.Tooltips.AuxiliaryTooltipController", "auxParent"),
        };
        Holds(
            "Screenshot and replay features suppress tooltips through GameInterop.Tooltips.NativeTooltipSuppression.",
            build =>
            {
                var features = build
                    .TypesIn("BazaarPlusPlus.Game.Screenshots")
                    .Concat(build.TypesIn("BazaarPlusPlus.Game.CombatReplay"))
                    .ToArray();
                return Accesses(features, nativeOwnership)
                    .Concat(Literals(features, StringComparison.Ordinal, "auxParent"));
            }
        );
    }
}

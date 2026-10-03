#nullable enable
using Xunit;
using static Architecture.Tests.ArchitectureRules;

namespace Architecture.Tests;

public sealed class NativeAssetLoadingArchitectureTests
{
    [Fact]
    public void Combat_controls_load_persistent_art_through_the_global_asset_seam()
    {
        var addressables = CompiledArtifacts.Universe.RequireNamespace(
            "UnityEngine.AddressableAssets"
        );
        Holds(
            "Combat status bar art loads through GameInterop/AssetLoading, not Addressables directly.",
            build =>
                References(
                    build.TypesIn("BazaarPlusPlus.Game.CombatStatusBar"),
                    InNamespace(addressables)
                )
        );
    }
}

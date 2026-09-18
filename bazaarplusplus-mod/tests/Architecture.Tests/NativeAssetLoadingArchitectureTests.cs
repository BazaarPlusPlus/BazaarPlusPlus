#nullable enable
using Xunit;

namespace Architecture.Tests;

public sealed class NativeAssetLoadingArchitectureTests
{
    [Fact]
    public void Combat_controls_load_persistent_art_through_the_global_asset_seam()
    {
        var root = Path.Combine(MainSourceRoot(RepoRoot()), "Game", "CombatStatusBar");
        var skin = File.ReadAllText(Path.Combine(root, "CombatStatusBarNativeSkin.cs"));
        Assert.Contains("NativeGlobalAssetLoader.LoadByAddressAsync", skin);
        foreach (var file in Directory.EnumerateFiles(root, "*.cs"))
        {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain("Addressables.LoadAssetAsync", source);
            Assert.DoesNotContain("UnityEngine.AddressableAssets", source);
        }
    }

    [Fact]
    public void Cross_build_argument_adaptation_has_one_shared_owner()
    {
        var sourceRoot = MainSourceRoot(RepoRoot());
        var invocationPath = Path.Combine(
            sourceRoot,
            "GameInterop",
            "AssetLoading",
            "NativeAssetLoaderInvocation.cs"
        );
        var globalLoaderPath = Path.Combine(
            sourceRoot,
            "GameInterop",
            "AssetLoading",
            "NativeGlobalAssetLoader.cs"
        );
        var cardPreviewLoaderPath = Path.Combine(
            sourceRoot,
            "GameInterop",
            "AssetLoading",
            "NativeCardPrefabLoader.cs"
        );
        var obsoleteCardPreviewHelperPath = Path.Combine(
            sourceRoot,
            "GameInterop",
            "CardPreview",
            "NativeCardPreviewAssetInvocation.cs"
        );

        Assert.True(File.Exists(invocationPath));
        Assert.False(File.Exists(obsoleteCardPreviewHelperPath));
        Assert.Contains(
            "NativeAssetLoaderInvocation.TryBuildArguments",
            File.ReadAllText(globalLoaderPath)
        );
        Assert.Contains(
            "NativeAssetLoaderInvocation.SupportsSignature",
            File.ReadAllText(globalLoaderPath)
        );
        Assert.Contains(
            "NativeAssetLoaderInvocation.TryBuildArguments",
            File.ReadAllText(cardPreviewLoaderPath)
        );
    }

    private static string RepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (
                File.Exists(Path.Combine(current.FullName, "CLAUDE.md"))
                && Directory.Exists(Path.Combine(current.FullName, "src"))
            )
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private static string MainSourceRoot(string repoRoot) =>
        Path.Combine(repoRoot, "src", "BazaarPlusPlus");
}

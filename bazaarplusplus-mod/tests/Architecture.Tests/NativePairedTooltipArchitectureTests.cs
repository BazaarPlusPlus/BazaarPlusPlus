using Xunit;

namespace Architecture.Tests;

/// <summary>
/// Pins ownership of the shared native paired-tooltip seam. Gate transitions, geometry settling,
/// retry behavior, and teardown are executable behavior in NativePairedTooltipHost.Tests.
/// </summary>
public sealed class NativePairedTooltipArchitectureTests
{
    [Fact]
    public void Paired_tooltip_runtime_has_one_shared_GameInterop_owner()
    {
        var root = RepoRoot();
        var sourceRoot = Path.Combine(root, "src", "BazaarPlusPlus");
        var sharedRoot = Path.Combine(sourceRoot, "GameInterop", "Tooltips");
        Assert.True(File.Exists(Path.Combine(sharedRoot, "NativePairedTooltipHost.cs")));
        Assert.True(File.Exists(Path.Combine(sharedRoot, "NativePairedTooltipContracts.cs")));

        var forbiddenOwners = Directory
            .EnumerateFiles(Path.Combine(sourceRoot, "Game"), "*.cs", SearchOption.AllDirectories)
            .Where(file =>
                Path.GetFileName(file).Contains("NativePairedTooltip", StringComparison.Ordinal)
            )
            .Select(file => Path.GetRelativePath(sourceRoot, file).Replace('\\', '/'))
            .ToArray();
        Assert.True(
            forbiddenOwners.Length == 0,
            "Feature directories must consume the shared host instead of owning native plumbing:\n"
                + string.Join("\n", forbiddenOwners)
        );
    }

    [Fact]
    public void Shared_tooltip_adapters_do_not_import_features_or_patches()
    {
        var root = Path.Combine(RepoRoot(), "src", "BazaarPlusPlus", "GameInterop", "Tooltips");
        var violations = Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .SelectMany(file =>
                File.ReadLines(file)
                    .Select((line, index) => (line, index))
                    .Where(hit =>
                        hit.line.Contains("using BazaarPlusPlus.Game.", StringComparison.Ordinal)
                        || hit.line.Contains(
                            "using BazaarPlusPlus.Patches",
                            StringComparison.Ordinal
                        )
                    )
                    .Select(hit => $"{Path.GetFileName(file)}:{hit.index + 1}: {hit.line.Trim()}")
            )
            .ToArray();
        Assert.True(
            violations.Length == 0,
            "Shared tooltip adapters must remain feature-neutral:\n" + string.Join("\n", violations)
        );
    }

    [Fact]
    public void Native_tooltip_suppression_has_one_shared_owner_and_all_show_gates()
    {
        var sourceRoot = Path.Combine(RepoRoot(), "src", "BazaarPlusPlus");
        var sharedSuppression = Path.Combine(
            sourceRoot,
            "GameInterop",
            "Tooltips",
            "NativeTooltipSuppression.cs"
        );
        var patches = Path.Combine(
            sourceRoot,
            "Patches",
            "Tooltips",
            "NativeTooltipSuppressionPatches.cs"
        );

        Assert.True(File.Exists(sharedSuppression));
        Assert.True(File.Exists(patches));
        Assert.False(
            File.Exists(
                Path.Combine(
                    sourceRoot,
                    "Game",
                    "CombatReplay",
                    "Video",
                    "ReplayRecordingHoverSuppression.cs"
                )
            )
        );
        Assert.False(
            File.Exists(
                Path.Combine(sourceRoot, "Patches", "Combat", "ReplayRecordingHoverPatches.cs")
            )
        );

        var suppressionSource = File.ReadAllText(sharedSuppression);
        Assert.Contains("NativeTooltipSuppressionOwnershipCore", suppressionSource);
        Assert.Contains("NativeTooltipSuppressionOwner.ReplayPresentation", suppressionSource);
        Assert.Contains("NativeTooltipSuppressionOwner.ReplayVideoRecording", suppressionSource);
        Assert.Contains("NativeTooltipSuppressionOwner.EndOfRunCapture", suppressionSource);
        Assert.Contains("auxParent", suppressionSource);
        Assert.Contains("CanvasHiderComponent", suppressionSource);
        Assert.Contains("UnlockAllLockedTooltipControllers", suppressionSource);
        Assert.Contains("HideCardTooltipController", suppressionSource);
        Assert.Contains("HideSecondaryCardTooltipController", suppressionSource);
        Assert.Contains("HideAuxiliaryTooltipController", suppressionSource);
        Assert.Contains("SetLockedFlag(false)", suppressionSource);
        Assert.Contains("DisableLockModeCanvasPublic", suppressionSource);
        Assert.Contains("ClearCurrentCard", suppressionSource);
        Assert.Contains("SetVisibility(false)", suppressionSource);
        Assert.Contains("AreRequiredShowGatesInstalled", suppressionSource);
        Assert.Contains("CapturePatchCapabilities", suppressionSource);
        Assert.Contains("NativeTooltipControllerSnapshot", suppressionSource);
        Assert.Contains("Harmony.GetPatchInfo", suppressionSource);
        Assert.Contains("NativeTooltipControllerTopologyGeneration", suppressionSource);

        var patchSource = File.ReadAllText(patches);
        Assert.Contains("TooltipParentComponent.ShowCardTooltipController", patchSource);
        Assert.Contains("TooltipParentComponent.ShowSecondaryCardTooltipController", patchSource);
        Assert.Contains("TooltipParentComponent.ShowAuxiliaryTooltipController", patchSource);
        Assert.Contains("CardTooltipController.ShowTooltipController", patchSource);
        Assert.Contains("AuxiliaryTooltipController.ShowAuxiliaryTooltipController", patchSource);
        Assert.Contains("NativeTooltipSuppression.NotifyControllerLifecycleChanged", patchSource);
        Assert.Equal(
            4,
            patchSource.Split("NativeTooltipSuppression.NotifyControllerLifecycleChanged").Length
                - 1
        );

        var featureOwnedCopies = Directory
            .EnumerateFiles(Path.Combine(sourceRoot, "Game"), "*.cs", SearchOption.AllDirectories)
            .Where(file =>
                Path.GetFileName(file)
                    .Contains("NativeTooltipSuppression", StringComparison.Ordinal)
                || File.ReadAllText(file)
                    .Contains("class ReplayRecordingHoverSuppression", StringComparison.Ordinal)
            )
            .Select(file => Path.GetRelativePath(sourceRoot, file).Replace('\\', '/'))
            .ToArray();
        Assert.True(
            featureOwnedCopies.Length == 0,
            "Features must consume the shared native-tooltip suppression adapter:\n"
                + string.Join("\n", featureOwnedCopies)
        );

        var nativeOwnershipTokens = new[]
        {
            "UnlockAllLockedTooltipControllers",
            "HideCardTooltipController",
            "HideSecondaryCardTooltipController",
            "HideAuxiliaryTooltipController",
            "CanvasHiderComponent.SetVisibility",
            ".auxParent",
        };
        var duplicateOwnership = new[]
        {
            Path.Combine(sourceRoot, "Game", "Screenshots"),
            Path.Combine(sourceRoot, "Game", "CombatReplay"),
        }
            .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(file =>
            {
                var source = File.ReadAllText(file);
                return nativeOwnershipTokens.Any(token =>
                    source.Contains(token, StringComparison.Ordinal)
                );
            })
            .Select(file => Path.GetRelativePath(sourceRoot, file).Replace('\\', '/'))
            .ToArray();
        Assert.True(
            duplicateOwnership.Length == 0,
            "Screenshot and replay features must not duplicate native tooltip ownership:\n"
                + string.Join("\n", duplicateOwnership)
        );
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
            directory = directory.Parent;
        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate repository root.");
    }
}

#nullable enable
using Xunit;

namespace Architecture.Tests;

public sealed class NativeCardPreviewArchitectureTests
{
    [Fact]
    public void Concrete_native_card_preview_ownership_stays_inside_owning_module()
    {
        var sourceRoot = MainSourceRoot(RepoRoot());
        var moduleRoot = Path.Combine(sourceRoot, "GameInterop", "CardPreview");
        var forbidden = new[]
        {
            "NativeCardPreviewRuntime",
            "NativeCardPreviewReflection",
            "NativeCardPreviewPool",
            "NativeCardPreviewFactory",
            "NativeCardPreviewResource",
            "NativeCardPreviewAssetLoader",
            "NativeCardPreviewPresentation",
            "NativePreviewPresentationTransaction",
        };
        var violations = new List<string>();

        foreach (
            var file in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
        )
        {
            if (file.StartsWith(moduleRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                continue;

            var source = File.ReadAllText(file);
            foreach (var token in forbidden.Where(source.Contains))
                violations.Add($"{Path.GetRelativePath(sourceRoot, file)}: {token}");
        }

        Assert.True(
            violations.Count == 0,
            "Native preview runtime/reflection/pool/resource types must stay behind the host seam:\n"
                + string.Join("\n", violations)
        );
    }

    [Fact]
    public void Consumers_retain_only_host_scope_and_opaque_sessions()
    {
        var sourceRoot = MainSourceRoot(RepoRoot());
        var collection = File.ReadAllText(
            Path.Combine(
                sourceRoot,
                "Game",
                "CollectionPanel",
                "Grid",
                "CollectionGridVirtualizer.cs"
            )
        );

        Assert.Contains("INativeCardPreviewScope", collection);
        Assert.Contains("INativeCardPreviewSession", collection);
        Assert.DoesNotContain("SetUpTask", collection);
        Assert.DoesNotContain("NativeCardPreviewKind", collection);
        Assert.DoesNotContain("Component Card", collection);
    }

    [Fact]
    public void Legacy_handle_lease_and_global_hover_registry_are_removed()
    {
        var moduleRoot = Path.Combine(MainSourceRoot(RepoRoot()), "GameInterop", "CardPreview");

        Assert.False(File.Exists(Path.Combine(moduleRoot, "NativeCardPreviewHandle.cs")));
        Assert.False(File.Exists(Path.Combine(moduleRoot, "NativeCardPreviewLease.cs")));
        Assert.False(File.Exists(Path.Combine(moduleRoot, "NativeCardPreviewHoverTracker.cs")));
        Assert.False(File.Exists(Path.Combine(moduleRoot, "NativeCardPreviewHoverRelay.cs")));
    }

    [Fact]
    public void Native_destroy_lifecycle_is_owned_by_the_native_preview_module()
    {
        var modulePatch = File.ReadAllText(
            Path.Combine(
                MainSourceRoot(RepoRoot()),
                "GameInterop",
                "CardPreview",
                "NativeCardPreviewLifecycle.cs"
            )
        );
        var collectionPatch = File.ReadAllText(
            Path.Combine(
                MainSourceRoot(RepoRoot()),
                "Patches",
                "CollectionPanel",
                "CollectionCardPreviewDestroyPatch.cs"
            )
        );

        Assert.Contains("[HarmonyPatch(typeof(CardPreviewBase), \"OnDestroy\")]", modulePatch);
        Assert.Contains("NativeCardPreviewLifecycle.NotifyDestroyed", modulePatch);
        Assert.DoesNotContain("CollectionPanel", modulePatch);

        Assert.Contains("PreviewOwner?.OnNativeDestroyed", collectionPatch);
        Assert.DoesNotContain("NativeCardPreviewLifecycle", collectionPatch);
    }

    [Fact]
    public void Host_and_pool_wire_the_tested_lifecycle_settlement_seams()
    {
        var moduleRoot = Path.Combine(MainSourceRoot(RepoRoot()), "GameInterop", "CardPreview");
        var host = File.ReadAllText(Path.Combine(moduleRoot, "NativeCardPreviewHost.cs"));
        var pool = File.ReadAllText(Path.Combine(moduleRoot, "NativeCardPreviewPool.cs"));
        var factory = File.ReadAllText(Path.Combine(moduleRoot, "NativeCardPreviewFactory.cs"));

        Assert.Contains("NativeCardPreviewScopeLifetime<NativeCardPreviewResource>", host);
        Assert.Contains("NativeCardPreviewScopeDisposal", host);
        Assert.Contains("NativeCardPreviewHoverState<Scope, NativeCardPreviewResource>", host);
        Assert.Contains("NativeCardPreviewLifecycle.Bind", host);
        Assert.Contains("NativePreviewPresentationTransaction.Apply", host);
        Assert.Contains("NativeCardPreviewPresentation.Create", factory);
        Assert.Contains("NativeCardPreviewPoolSettlement.Prepare", pool);
    }

    [Fact]
    public void Premium_visual_intent_is_enabled_only_for_collection_previews()
    {
        var sourceRoot = MainSourceRoot(RepoRoot());
        var collectionOwner = File.ReadAllText(
            Path.Combine(
                sourceRoot,
                "Game",
                "CollectionPanel",
                "Grid",
                "CollectionNativeCardPreviewOwner.cs"
            )
        );
        var postCombatPreview = File.ReadAllText(
            Path.Combine(
                sourceRoot,
                "Patches",
                "PostCombatImpact",
                "NativePostCombatImpactTooltipView.cs"
            )
        );
        var factory = File.ReadAllText(
            Path.Combine(sourceRoot, "GameInterop", "CardPreview", "NativeCardPreviewFactory.cs")
        );
        var runtime = File.ReadAllText(
            Path.Combine(sourceRoot, "GameInterop", "CardPreview", "NativeCardPreviewRuntime.cs")
        );

        Assert.Contains("public bool UsePremiumVisuals => true;", collectionOwner);
        Assert.Contains("public bool UsePremiumVisuals => false;", postCombatPreview);
        Assert.Contains("owner.UsePremiumVisuals", factory);
        Assert.Contains("usePremiumVisuals", runtime);
        Assert.DoesNotContain("template, false, instance", runtime);
    }

    [Fact]
    public void Post_combat_preview_batch_policy_stays_in_the_feature_layer()
    {
        var sourceRoot = MainSourceRoot(RepoRoot());
        var batchPath = Path.Combine(
            sourceRoot,
            "Game",
            "PostCombatImpact",
            "Ui",
            "NativePreviewFitBatch.cs"
        );
        var viewPath = Path.Combine(
            sourceRoot,
            "Patches",
            "PostCombatImpact",
            "NativePostCombatImpactTooltipView.cs"
        );

        Assert.True(File.Exists(batchPath));
        var batch = File.ReadAllText(batchPath);
        Assert.DoesNotContain("UnityEngine", batch);
        Assert.DoesNotContain("Canvas.", batch);

        var view = File.ReadAllText(viewPath);
        Assert.Contains("NativePreviewFitBatch<PendingNativePreviewFit>", view);
        Assert.DoesNotContain("List<PendingNativePreviewFit>", view);
    }

    [Fact]
    public void Same_card_modifier_refresh_keeps_the_native_tooltip_host_alive()
    {
        var sourceRoot = MainSourceRoot(RepoRoot());
        var tooltipRoot = Path.Combine(sourceRoot, "Game", "Tooltips");
        var refreshPaths = new[]
        {
            Path.Combine(tooltipRoot, "TooltipModifierRefreshController.cs"),
            Path.Combine(tooltipRoot, "UpgradeTooltipScheduler.cs"),
        };

        foreach (var path in refreshPaths)
        {
            var source = File.ReadAllText(path);
            Assert.Contains("TooltipPreviewContentRefresh.TryApply", source);
            Assert.DoesNotContain("HideCardTooltipController", source);
            Assert.DoesNotContain("ShowCardTooltipController", source);
        }

        var contentRefresh = File.ReadAllText(
            Path.Combine(tooltipRoot, "TooltipPreviewContentRefresh.cs")
        );
        Assert.Contains("NativeCardTooltipContentRefresher.TryApply", contentRefresh);

        var nativeHost = File.ReadAllText(
            Path.Combine(sourceRoot, "GameInterop", "CardPreview", "NativeCardPreviewHost.cs")
        );
        Assert.Contains("NativeCardTooltipContentRefresher.TryApply", nativeHost);
        Assert.DoesNotContain("HideCardTooltipController", nativeHost);
        Assert.DoesNotContain("ShowCardTooltipController", nativeHost);

        var nativeContentRefresh = File.ReadAllText(
            Path.Combine(
                sourceRoot,
                "GameInterop",
                "Tooltips",
                "NativeCardTooltipContentRefresher.cs"
            )
        );
        Assert.Contains("ApplyPreviewTooltip", nativeContentRefresh);
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
                return current.FullName;
            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private static string MainSourceRoot(string repoRoot) =>
        Path.Combine(repoRoot, "src", "BazaarPlusPlus");
}

#nullable enable
using Xunit;
using static Architecture.Tests.ArchitectureRules;

namespace Architecture.Tests;

public sealed class NativeCardPreviewArchitectureTests
{
    private const string CardPreview = "BazaarPlusPlus.GameInterop.CardPreview";
    private const string TooltipParent = "TheBazaar.UI.Tooltips.TooltipParentComponent";

    [Fact]
    public void Concrete_native_card_preview_ownership_stays_inside_owning_module()
    {
        var concrete = new[]
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
        Holds(
            "Native preview runtime/reflection/pool/resource types must stay behind the host seam.",
            build =>
            {
                var owned = concrete
                    .Select(name => build.Type(CardPreview + "." + name).FullName)
                    .ToArray();
                return References(
                    build.Types.Where(type =>
                        !CompiledBuild.IsInNamespace(type.Namespace, CardPreview)
                    ),
                    Is(owned)
                );
            }
        );
    }

    [Fact]
    public void Post_combat_preview_batch_policy_stays_in_the_feature_layer()
    {
        Holds(
            "The post-combat preview fit batch is pure policy with no Unity dependency.",
            build =>
                References(
                    build.Type("BazaarPlusPlus.Game.PostCombatImpact.Ui.NativePreviewFitBatch`1"),
                    FromAssembly(RequireAssemblyPrefix(build, "UnityEngine"))
                )
        );
    }

    [Fact]
    public void Same_card_modifier_refresh_keeps_the_native_tooltip_host_alive()
    {
        var universe = CompiledArtifacts.Universe;
        var reopen = new[]
        {
            universe.RequireMember(TooltipParent, "HideCardTooltipController"),
            universe.RequireMember(TooltipParent, "ShowCardTooltipController"),
        };
        Holds(
            "Modifier refresh replaces tooltip content in place; it never hides and re-shows it.",
            build =>
            {
                var refreshers = new[]
                {
                    build.Type("BazaarPlusPlus.Game.Tooltips.TooltipModifierRefreshController"),
                    build.Type("BazaarPlusPlus.Game.Tooltips.UpgradeTooltipScheduler"),
                };
                // The host also owns borrowed-preview teardown, which may hide its own tooltip;
                // the rule covers the host's in-place refresh path.
                var contentRefresh = build
                    .Type("BazaarPlusPlus.GameInterop.Tooltips.NativeCardTooltipContentRefresher")
                    .FullName;
                var hostRefresh = build
                    .Type(CardPreview + ".NativeCardPreviewHost")
                    .Methods.Where(method =>
                        method.Instructions.Any(op =>
                            op.IsMemberAccess && op.Targets(contentRefresh, "TryApply")
                        )
                    )
                    .ToArray();
                Assert.NotEmpty(hostRefresh);
                return Accesses(refreshers, reopen).Concat(Accesses(hostRefresh, reopen));
            }
        );
    }
}

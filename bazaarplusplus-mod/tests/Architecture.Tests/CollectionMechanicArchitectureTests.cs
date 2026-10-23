#nullable enable
using Xunit;
using static Architecture.Tests.ArchitectureRules;

namespace Architecture.Tests;

public sealed class CollectionMechanicArchitectureTests
{
    private const string CollectionData = "BazaarPlusPlus.Game.CollectionPanel.Data";

    [Fact]
    public void Collection_mechanic_projection_stays_at_the_catalog_VM_boundary()
    {
        Holds(
            "Only CollectionCardVmProjection projects mechanic facts; every other path reads the cached flag.",
            build =>
            {
                var facts = build.Type(CollectionData + ".CollectionMechanicFacts").FullName;
                var projection = build
                    .Type(CollectionData + ".CollectionCardVmProjection")
                    .FullName;
                var callers = build
                    .Types.SelectMany(type => type.Methods)
                    .Where(method =>
                        method.Instructions.Any(op =>
                            op.IsMemberAccess && op.Targets(facts, "Project")
                        )
                    )
                    .ToArray();
                Assert.NotEmpty(callers);
                return callers
                    .Where(method => method.Owner.FullName != projection)
                    .Select(method => $"{method} calls {facts}::Project");
            }
        );
    }

    // The filter engine lives in Core, which references no game assembly at all
    // (CoreAssemblyBoundaryTests); the host refresh path is checked here.
    [Fact]
    public void Collection_refresh_path_does_not_walk_game_effect_graphs()
    {
        var universe = CompiledArtifacts.Universe;
        var effectTypes = new[]
        {
            universe.RequireType("BazaarGameShared.Domain.Effect.TCardAbility"),
            universe.RequireType("BazaarGameShared.Domain.Effect.TCardAura"),
        };
        var actions = universe.RequireNamespace("BazaarGameShared.Domain.Effect.Actions");
        var tier = "BazaarGameShared.Domain.Cards.TCardTier";
        var effectIds = new[]
        {
            universe.RequireMember(tier, "get_AbilityIds"),
            universe.RequireMember(tier, "get_AuraIds"),
        };
        Holds(
            "Refresh consumes the cached mechanic flag, never the effect graph.",
            build =>
            {
                var paths = new[]
                {
                    build.Type("BazaarPlusPlus.Game.CollectionPanel.CollectionPanel"),
                };
                return References(paths, Is(effectTypes))
                    .Concat(References(paths, InNamespace(actions)))
                    .Concat(Accesses(paths, effectIds));
            }
        );
    }
}

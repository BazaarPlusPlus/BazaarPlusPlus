#nullable enable
using Xunit;
using static Architecture.Tests.ArchitectureRules;

namespace Architecture.Tests;

public sealed class NativeMonsterBoardArchitectureTests
{
    private const string MonsterBoardTooltip = "TheBazaar.UI.Tooltips.MonsterBoardTooltip";

    [Fact]
    public void Features_use_owned_boards_without_reaching_into_native_components()
    {
        var universe = CompiledArtifacts.Universe;
        var nativeBoardMembers = new[]
        {
            universe.RequireMember(MonsterBoardTooltip, "HandleBoardState"),
            universe.RequireMember(MonsterBoardTooltip, "HandlePooling"),
            universe.RequireMember(MonsterBoardTooltip, "_activeCards"),
            universe.RequireMember(MonsterBoardTooltip, "_sockets"),
        };
        var item = universe.RequireType("BazaarGameShared.Domain.Cards.Item.TCardInstanceItem");
        var uiElements = universe.RequireNamespace("UnityEngine.UIElements");
        Holds(
            "History and live-build panels drive native boards only through OwnedMonsterBoardPreview.",
            build =>
            {
                var features = build
                    .TypesIn("BazaarPlusPlus.Game.HistoryPanel")
                    .Concat(build.TypesIn("BazaarPlusPlus.Game.LiveBuildPanel"))
                    .ToArray();
                var constructions = features.SelectMany(type =>
                    type.Methods.SelectMany(method =>
                        method
                            .Instructions.Where(op =>
                                op.OpCode == "newobj" && op.TargetType?.FullName == item
                            )
                            .Select(op => $"{method} {op}")
                    )
                );
                return Accesses(features, nativeBoardMembers)
                    .Concat(
                        Literals(
                            features,
                            StringComparison.Ordinal,
                            nativeBoardMembers.Select(member => member.Member).ToArray()
                        )
                    )
                    .Concat(constructions)
                    .Concat(
                        References(
                            build.TypesIn("BazaarPlusPlus.GameInterop.MonsterBoardPreview"),
                            InNamespace(uiElements)
                        )
                    );
            }
        );
    }

    [Fact]
    public void Native_panels_share_supporter_attribution_through_the_supporters_module()
    {
        Holds(
            "Supporter attribution is shared by the panels and depends on neither.",
            build =>
            {
                var panels = new[]
                {
                    "BazaarPlusPlus.Game.HistoryPanel",
                    "BazaarPlusPlus.Game.LiveBuildPanel",
                };
                foreach (var panel in panels)
                    _ = build.TypesIn(panel);
                return References(
                    build.TypesIn("BazaarPlusPlus.Game.Supporters"),
                    InNamespace(panels)
                );
            }
        );
    }
}

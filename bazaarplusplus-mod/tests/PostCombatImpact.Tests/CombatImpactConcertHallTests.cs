using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Effect;
using BazaarGameShared.Domain.Effect.Actions;
using BazaarGameShared.Domain.Effect.Trigger;
using BazaarGameShared.Domain.Prerequisites;
using BazaarGameShared.Domain.Prerequisites.Conditionals;
using BazaarGameShared.Domain.Targeting;
using BazaarGameShared.Domain.Values;
using BazaarGameShared.Infra.Messages.CombatSimEvents;
using BazaarPlusPlus.Game.PostCombatImpact.Data;
using Xunit;

namespace PostCombatImpact.Tests;

public sealed partial class CombatImpactProjectorTests
{
    [Fact]
    public void Ghost_concert_hall_notes_do_not_create_a_second_guzheng_tempo_row()
    {
        using var resource = typeof(CombatImpactProjectorTests).Assembly.GetManifestResourceStream(
            "PostCombatImpact.Tests.Fixtures.ghost-concert-hall-tempo-20260907.combat.mpack.gz"
        )!;
        using var gzip = new System.IO.Compression.GZipStream(
            resource,
            System.IO.Compression.CompressionMode.Decompress
        );
        using var bytes = new MemoryStream();
        gzip.CopyTo(bytes);
        var simulation = MessagePack.MessagePackSerializer.Deserialize<CombatSim>(bytes.ToArray());
        var hallTemplate = Guid.Parse("8bf6a0b7-52f0-498c-bc45-21e47c8ea76f");
        var entities = Entities().ToDictionary(entry => entry.Key, entry => entry.Value);
        using var spawnResource =
            typeof(CombatImpactProjectorTests).Assembly.GetManifestResourceStream(
                "PostCombatImpact.Tests.Fixtures.ghost-concert-hall-tempo-20260907.spawn.mpack.gz"
            )!;
        using var spawnGzip = new System.IO.Compression.GZipStream(
            spawnResource,
            System.IO.Compression.CompressionMode.Decompress
        );
        using var spawnBytes = new MemoryStream();
        spawnGzip.CopyTo(spawnBytes);
        var spawn =
            MessagePack.MessagePackSerializer.Deserialize<BazaarGameShared.Infra.Messages.GameSimEvents.GameSim>(
                spawnBytes.ToArray()
            );
        foreach (
            var card in spawn.Events.OfType<BazaarGameShared.Infra.Messages.GameSimEvents.GameSimEventCardSpawned>()
        )
            entities[card.InstanceId] = new CombatImpactEntity(
                card.InstanceId,
                card.InstanceId,
                card.Type.ToString(),
                null,
                10,
                Guid.Parse(card.TemplateId),
                CombatantId: card.CombatantId,
                Section: card.Section,
                SocketId: card.Socket
            );

        entities["Ar6igzD"] = new CombatImpactEntity(
            "Ar6igzD",
            "Concert Hall",
            "Item",
            null,
            0,
            hallTemplate,
            CombatantId: ECombatantId.Opponent,
            Section: EInventorySection.Hand
        );
        entities["diJBoIu"] = new CombatImpactEntity(
            "diJBoIu",
            "Guzheng",
            "Item",
            null,
            1,
            DisplaySpan: 3,
            CombatantId: ECombatantId.Opponent,
            Section: EInventorySection.Hand,
            SocketId: EContainerSocketId.Socket_5,
            Tags: [ECardTag.Instrument, ECardTag.Weapon],
            HiddenTags: [EHiddenTag.Tempo, EHiddenTag.Burn]
        );
        entities["VU5NPyp"] = new CombatImpactEntity(
            "VU5NPyp",
            "Sound Engineer",
            "Item",
            null,
            2,
            CombatantId: ECombatantId.Opponent
        );
        foreach (
            var (id, socket, condition) in new (string, EContainerSocketId, ITCardConditional)[]
            {
                (
                    "3GGfGZ_",
                    EContainerSocketId.Socket_5,
                    new TCardConditionalHiddenTag
                    {
                        Tags = [EHiddenTag.Tempo],
                        Operator = EListComparisonOperator.Any,
                    }
                ),
                (
                    "o1TDZin",
                    EContainerSocketId.Socket_6,
                    new TCardConditionalTag
                    {
                        Tags = [ECardTag.Weapon],
                        Operator = EListComparisonOperator.Any,
                    }
                ),
                (
                    "5bJAEC-",
                    EContainerSocketId.Socket_7,
                    new TCardConditionalHiddenTag
                    {
                        Tags = [EHiddenTag.Burn],
                        Operator = EListComparisonOperator.Any,
                    }
                ),
            }
        )
        {
            var ability = ConcertHallNoteAbility(hallTemplate, condition);
            Assert.True(CombatImpactAttributionRuleReader.TryReadUseRule(ability, out var rule));
            entities[id] = new CombatImpactEntity(
                id,
                id,
                "SocketEffect",
                null,
                3,
                CombatantId: ECombatantId.Opponent,
                Section: EInventorySection.Hand,
                SocketId: socket,
                PrerequisiteSkillSourceRulesByEffectId: CombatImpactAttributionRuleReader.ReadSourceRules(
                    [ability],
                    []
                ),
                UseAttributionRules: [rule]
            );
        }
        var report = CombatImpactProjector.Project(simulation, entities);
        var guz = report.Sources.Single(source => source.Entity.Id == "diJBoIu");
        var tempo = Assert.Single(
            guz.Groups,
            group => group.NativeAttributeKey.StartsWith("Tempo", StringComparison.Ordinal)
        );
        Assert.Equal("TempoApplyAmount", tempo.NativeAttributeKey);
        Assert.Equal(9, tempo.Count);
        Assert.Equal(27, tempo.AuthoritativeMetric!.Value);
        Assert.DoesNotContain(guz.Groups, group => group.NativeAttributeKey == "Tempo");
        var hall = report.Sources.Single(source => source.Entity.Id == "Ar6igzD");
        var notes = Assert.Single(
            hall.Groups,
            group => group.NativeAttributeKey == "TempoApplyAmount"
        );
        Assert.Equal(27, notes.Count);
        Assert.Equal(27, notes.ObservedValue);
    }

    [Fact]
    public void Item_prerequisite_requires_matching_board_and_every_note_condition()
    {
        var id = Guid.NewGuid();
        var ability = ConcertHallNoteAbility(
            id,
            new TCardConditionalHiddenTag
            {
                Tags = [EHiddenTag.Burn],
                Operator = EListComparisonOperator.Any,
            }
        );
        Assert.True(CombatImpactAttributionRuleReader.TryReadUseRule(ability, out var rule));
        var owner = new CombatImpactEntity(
            "hall",
            "Hall",
            "Item",
            null,
            0,
            id,
            Section: EInventorySection.Hand
        );
        Assert.True(rule.SourceRule.Matches(owner));
        Assert.False(rule.SourceRule.Matches(owner with { Section = EInventorySection.Stash }));
        Assert.False(rule.SourceRule.Matches(owner with { TypeLabel = "Skill" }));
        var item = owner with { Tags = [ECardTag.Instrument], HiddenTags = [EHiddenTag.Burn] };
        Assert.True(rule.ItemCondition.Matches(item));
        Assert.False(rule.ItemCondition.Matches(item with { Tags = [] }));
        Assert.False(rule.ItemCondition.Matches(item with { HiddenTags = [] }));
    }

    private static TCardAbility ConcertHallNoteAbility(Guid owner, ITCardConditional matchingTag) =>
        new()
        {
            Id = "5",
            Trigger = new TTriggerOnItemUsed
            {
                Subject = new TTargetCardOccupying
                {
                    Conditions = new TCardConditionalAnd
                    {
                        Conditions =
                        [
                            new TCardConditionalTag
                            {
                                Tags = [ECardTag.Instrument],
                                Operator = EListComparisonOperator.Any,
                            },
                            matchingTag,
                        ],
                    },
                },
            },
            Action = new TActionPlayerModifyAttribute
            {
                AttributeType = EPlayerAttributeType.Tempo,
                Operation = EAttributeModifierOperation.Add,
                Value = new TFixedValue { Value = 1 },
                Target = new TTargetPlayerRelative
                {
                    TargetMode = ETargetPlayerRelativeTargetMode.Self,
                },
            },
            Prerequisites =
            [
                new TPrerequisiteCardCount
                {
                    Comparison = EComparisonOperator.GreaterThanOrEqual,
                    Amount = 1,
                    Subject = new TTargetCardSection
                    {
                        TargetSection = ETargetCardSectionTargetSection.SelfHand,
                        Conditions = new TCardConditionalId { Id = owner },
                    },
                },
            ],
        };
}

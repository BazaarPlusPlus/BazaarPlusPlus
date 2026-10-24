using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Enchantments;
using BazaarGameShared.Domain.Cards.Item;
using BazaarGameShared.Domain.Cards.Quests;
using BazaarGameShared.Domain.Cards.Skill;
using BazaarGameShared.Domain.Core;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Effect;
using BazaarGameShared.Domain.Effect.Actions;
using BazaarGameShared.Domain.Effect.AuraActions;
using BazaarGameShared.Domain.Effect.Trigger;
using BazaarGameShared.Domain.Tooltips;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.GameInterop.Cards;
using BazaarPlusPlus.GameInterop.Heroes;

// Vocabulary pin (ADR-0015): every member of the game enums this snapshot defines maps to a card
// facet, so a game update that adds one fails here instead of silently dropping it from cards.
foreach (
    var (gameEnum, unmapped) in new (string, IReadOnlyList<string>)[]
    {
        (nameof(EHero), DesktopCardFacets.Hero.UnmappedSourceNames),
        (nameof(ETier), DesktopCardFacets.Tier.UnmappedSourceNames),
        (nameof(ECardSize), DesktopCardFacets.Size.UnmappedSourceNames),
        (nameof(ECardType), DesktopCardFacets.Type.UnmappedSourceNames),
        (nameof(ECardTag), DesktopCardFacets.Tag.UnmappedSourceNames),
        (nameof(EHiddenTag), DesktopCardFacets.HiddenTag.UnmappedSourceNames),
        (nameof(EEnchantmentType), DesktopCardFacets.Enchantment.UnmappedSourceNames),
        (nameof(EInventorySection), DesktopCardFacets.Section.UnmappedSourceNames),
        (nameof(EContainerSocketId), DesktopCardFacets.Socket.UnmappedSourceNames),
    }
)
    AssertTrue(
        unmapped.Count == 0,
        $"{gameEnum} defines {string.Join(", ", unmapped)} with no card facet member."
    );
AssertTrue(
    DesktopCardFacets.Hero.TryToFacet(
        TheDragonsHeroIdentity.TryResolve(TheDragonsHeroIdentity.CanonicalId, out var dragons)
            ? dragons
            : throw new InvalidOperationException("This snapshot defines no The Dragons hero."),
        out var dragonsFacet
    )
        && dragonsFacet == CardHero.TheDragons,
    "This snapshot's The Dragons member should map to the canonical facet."
);

// The desktop host's projection of game card templates into the Core CollectionCardVm
// (CollectionCardVmProjection, CollectionCardClassifier, DesktopCardFacets): mechanics, derived
// keywords, catalog classification, and the search corpus built from game localization.

var derivedLifestealTemplate = new TCardItem
{
    Id = Guid.NewGuid(),
    Type = ECardType.Item,
    StartingTier = ETier.Bronze,
    Size = ECardSize.Medium,
    InternalName = "Derived Lifesteal Weapon",
    ArtKey = "Assets/Cards/DerivedLifestealWeapon.png",
    Heroes = new HashSet<EHero> { EHero.Common },
    Tags = new HashSet<ECardTag> { ECardTag.Weapon },
    HiddenTags = new HashSet<EHiddenTag>(),
    Tiers = new Dictionary<ETier, TCardTier>
    {
        [ETier.Bronze] = new TCardTier
        {
            Attributes = new Dictionary<ECardAttributeType, int>
            {
                [ECardAttributeType.Lifesteal] = 100,
            },
        },
    },
};
var derivedLifestealVm = Project(derivedLifestealTemplate);
AssertTrue(
    derivedLifestealVm.HiddenTags.Contains(CardHiddenTag.Lifesteal),
    "CollectionCardVm.From should derive Lifesteal from positive item attributes."
);
AssertFalse(
    derivedLifestealTemplate.HiddenTags.Contains(EHiddenTag.Lifesteal),
    "Derived Lifesteal projection should not mutate the source game template HiddenTags."
);
AssertValues(
    CollectionFacetAvailability
        .SnapshotFor(new[] { derivedLifestealVm })
        .ItemKeywords.Select(tag => tag.ToString())
        .ToArray(),
    new[] { nameof(EHiddenTag.Lifesteal) },
    "Available item keywords should include Lifesteal derived from item attributes."
);
var questTemplate = new TCardItem
{
    Id = Guid.NewGuid(),
    Type = ECardType.Item,
    StartingTier = ETier.Bronze,
    Size = ECardSize.Medium,
    InternalName = "Quest Item Without Quest HiddenTag",
    ArtKey = "Assets/Cards/QuestItemWithoutQuestHiddenTag.png",
    Heroes = new HashSet<EHero> { EHero.Common },
    HiddenTags = new HashSet<EHiddenTag> { EHiddenTag.Charge },
    Quests = new List<TQuestGroup> { new() { Entries = new List<TQuestEntry> { new() } } },
};
var questVm = Project(questTemplate);
AssertTrue(
    questVm.HiddenTags.Contains(CardHiddenTag.Quest),
    "CollectionCardVm.From should derive Quest from a non-empty item Quests graph."
);
AssertTrue(
    questVm.HiddenTags.Contains(CardHiddenTag.Charge),
    "Quest projection should preserve the item's native hidden tags."
);
AssertFalse(
    questTemplate.HiddenTags.Contains(EHiddenTag.Quest),
    "Quest projection should not mutate the source game template HiddenTags."
);
var questFilter = new CollectionFilterState();
questFilter.Keywords.Add(CardHiddenTag.Quest);
AssertSequence(
    CollectionFilterEngine.Apply(new[] { questVm, derivedLifestealVm }, questFilter),
    new[] { questVm.Id },
    "Quest keyword filtering should include items whose Quests graph lacks a native Quest hidden tag."
);
var noLifestealTemplate = new TCardItem
{
    Id = Guid.NewGuid(),
    Type = ECardType.Item,
    StartingTier = ETier.Bronze,
    Size = ECardSize.Medium,
    InternalName = "No Lifesteal Weapon",
    ArtKey = "Assets/Cards/NoLifestealWeapon.png",
    Heroes = new HashSet<EHero> { EHero.Common },
    Tags = new HashSet<ECardTag> { ECardTag.Weapon },
    HiddenTags = new HashSet<EHiddenTag>(),
    Tiers = new Dictionary<ETier, TCardTier>
    {
        [ETier.Bronze] = new TCardTier
        {
            Attributes = new Dictionary<ECardAttributeType, int>
            {
                [ECardAttributeType.Lifesteal] = 0,
            },
        },
    },
};
var noLifestealVm = Project(noLifestealTemplate);
var lifestealFilter = new CollectionFilterState();
lifestealFilter.Keywords.Add(CardHiddenTag.Lifesteal);
AssertSequence(
    CollectionFilterEngine.Apply(new[] { noLifestealVm, derivedLifestealVm }, lifestealFilter),
    new[] { derivedLifestealVm.Id },
    "Lifesteal keyword filtering should include derived Lifesteal VMs and exclude non-positive attributes."
);
var allLifestealFilter = new CollectionFilterState
{
    KeywordMatchMode = CollectionFacetMatchMode.All,
};
allLifestealFilter.Keywords.Add(CardHiddenTag.Lifesteal);
AssertSequence(
    CollectionFilterEngine.Apply(new[] { noLifestealVm, derivedLifestealVm }, allLifestealFilter),
    new[] { derivedLifestealVm.Id },
    "All keyword mode should still match a VM with derived Lifesteal when Lifesteal is the only selected keyword."
);
var attributeOnlyMulticastTemplate = new TCardItem
{
    Id = Guid.NewGuid(),
    Type = ECardType.Item,
    StartingTier = ETier.Bronze,
    Size = ECardSize.Medium,
    InternalName = "Attribute Only Multicast Item",
    ArtKey = "Assets/Cards/AttributeOnlyMulticastItem.png",
    HiddenTags = new HashSet<EHiddenTag>(),
    Tiers = new Dictionary<ETier, TCardTier>
    {
        [ETier.Bronze] = new TCardTier
        {
            Attributes = new Dictionary<ECardAttributeType, int>
            {
                [ECardAttributeType.Multicast] = 3,
            },
        },
    },
};
var attributeOnlyMulticastVm = Project(attributeOnlyMulticastTemplate);
AssertFalse(
    attributeOnlyMulticastVm.HiddenTags.Contains(CardHiddenTag.Multicast),
    "Structured Multicast projection should not mutate the native hidden-keyword facts."
);
AssertTrue(
    attributeOnlyMulticastVm.Mechanics.Has(CollectionMechanic.Multicast),
    "A supported tier with base Multicast greater than one should project the Multicast mechanic."
);

var baseOneMulticastTemplate = MechanicItemTemplate(
    new Dictionary<ETier, TCardTier>
    {
        [ETier.Bronze] = Tier(attributes: new() { [ECardAttributeType.Multicast] = 1 }),
    }
);
AssertFalse(
    Project(baseOneMulticastTemplate).Mechanics.Has(CollectionMechanic.Multicast),
    "Base Multicast one without an active modifier should not project Multicast."
);

var crossTierMulticastTemplate = MechanicItemTemplate(
    new Dictionary<ETier, TCardTier>
    {
        [ETier.Bronze] = Tier(attributes: new() { [ECardAttributeType.Multicast] = 1 }),
        [ETier.Silver] = Tier(attributes: new() { [ECardAttributeType.Multicast] = 3 }),
    }
);
AssertTrue(
    Project(crossTierMulticastTemplate).Mechanics.Has(CollectionMechanic.Multicast),
    "Multicast projection should inspect every supported tier and observe later-tier changes."
);

var activeAbilityMulticastTemplate = MechanicItemTemplate(
    new Dictionary<ETier, TCardTier>
    {
        [ETier.Bronze] = Tier(abilityIds: new[] { "active-multicast" }),
    },
    abilities: new() { ["active-multicast"] = Ability(MulticastModifier()) }
);
AssertTrue(
    Project(activeAbilityMulticastTemplate).Mechanics.Has(CollectionMechanic.Multicast),
    "An active base ability modifier should project Multicast."
);

var nestedAbilityMulticastTemplate = MechanicItemTemplate(
    new Dictionary<ETier, TCardTier>
    {
        [ETier.Bronze] = Tier(abilityIds: new[] { "nested-multicast" }),
    },
    abilities: new()
    {
        ["nested-multicast"] = Ability(
            new TActionAnd
            {
                Actions = new List<ITAction>
                {
                    new TActionAnd { Actions = new List<ITAction> { MulticastModifier() } },
                },
            }
        ),
    }
);
AssertTrue(
    Project(nestedAbilityMulticastTemplate).Mechanics.Has(CollectionMechanic.Multicast),
    "A Multicast modifier nested in combined actions should project Multicast."
);

var activeAuraMulticastTemplate = MechanicItemTemplate(
    new Dictionary<ETier, TCardTier>
    {
        [ETier.Bronze] = Tier(auraIds: new[] { "active-multicast-aura" }),
    },
    auras: new() { ["active-multicast-aura"] = Aura(MulticastAuraModifier()) }
);
AssertTrue(
    Project(activeAuraMulticastTemplate).Mechanics.Has(CollectionMechanic.Multicast),
    "An active base aura modifier should project Multicast."
);

var orphanMulticastTemplate = MechanicItemTemplate(
    new Dictionary<ETier, TCardTier> { [ETier.Bronze] = Tier() },
    abilities: new() { ["orphan-multicast"] = Ability(MulticastModifier()) },
    auras: new() { ["orphan-multicast-aura"] = Aura(MulticastAuraModifier()) }
);
AssertFalse(
    Project(orphanMulticastTemplate).Mechanics.Has(CollectionMechanic.Multicast),
    "Unreferenced base abilities and auras should not project Multicast."
);

var enchantmentMulticastTemplate = MechanicItemTemplate(
    new Dictionary<ETier, TCardTier> { [ETier.Bronze] = Tier() },
    enchantments: new()
    {
        [EEnchantmentType.Shiny] = new TEnchantment
        {
            Attributes = new Dictionary<ECardAttributeType, int>
            {
                [ECardAttributeType.Multicast] = 2,
            },
            Abilities = new Dictionary<string, TCardAbility>
            {
                ["enchanted-multicast"] = Ability(MulticastModifier()),
            },
            Auras = new Dictionary<string, TCardAura>
            {
                ["enchanted-multicast-aura"] = Aura(MulticastAuraModifier()),
            },
        },
    }
);
AssertFalse(
    Project(enchantmentMulticastTemplate).Mechanics.Has(CollectionMechanic.Multicast),
    "Enchantment attributes, abilities, and auras should not pollute base Multicast facts."
);

var nativeAndDerivedMulticastTemplate = MechanicItemTemplate(
    new Dictionary<ETier, TCardTier>
    {
        [ETier.Bronze] = Tier(attributes: new() { [ECardAttributeType.Multicast] = 2 }),
    },
    hiddenTags: new() { EHiddenTag.Multicast }
);
var nativeAndDerivedMulticastVm = Project(nativeAndDerivedMulticastTemplate);
var nativeOnlyMulticastVm = Project(
    MechanicItemTemplate(
        new Dictionary<ETier, TCardTier> { [ETier.Bronze] = Tier() },
        hiddenTags: new() { EHiddenTag.Multicast }
    )
);
AssertTrue(
    nativeOnlyMulticastVm.Mechanics.Has(CollectionMechanic.Multicast),
    "A native Multicast hidden tag should map into the same structured mechanic fact."
);
var duplicateMulticastAvailability = CollectionFacetAvailability.SnapshotFor(
    new[] { nativeAndDerivedMulticastVm }
);
AssertFalse(
    duplicateMulticastAvailability.ItemKeywords.Contains(CardHiddenTag.Multicast),
    "Native Multicast hidden tags should be normalized out of the native keyword availability."
);
AssertValues(
    duplicateMulticastAvailability.ItemMechanics.ToArray(),
    new[] { CollectionMechanic.Multicast },
    "Native and derived Multicast facts should collapse into one available mechanic."
);
AssertEqual(
    1,
    duplicateMulticastAvailability
        .KeywordOptionsFor(CardType.Item)
        .Count(option => option.Mechanic == CollectionMechanic.Multicast),
    "Native and derived Multicast facts should render one Multicast chip."
);

var multicastSkillTemplate = new TCardSkill
{
    Id = Guid.NewGuid(),
    Type = ECardType.Skill,
    StartingTier = ETier.Bronze,
    InternalName = "Multicast Skill",
    HiddenTags = new HashSet<EHiddenTag>(),
    Tiers = new Dictionary<ETier, TCardTier>
    {
        [ETier.Bronze] = Tier(abilityIds: new[] { "skill-multicast" }),
    },
    Abilities = new Dictionary<string, TCardAbility>
    {
        ["skill-multicast"] = Ability(MulticastModifier()),
    },
};
var multicastSkillVm = Project(multicastSkillTemplate);
var independentMulticastAvailability = CollectionFacetAvailability.SnapshotFor(
    new[] { attributeOnlyMulticastVm, multicastSkillVm }
);
AssertTrue(
    independentMulticastAvailability
        .MechanicsFor(CardType.Item)
        .Contains(CollectionMechanic.Multicast),
    "Item Multicast availability should be computed from item catalog facts."
);
AssertTrue(
    independentMulticastAvailability
        .MechanicsFor(CardType.Skill)
        .Contains(CollectionMechanic.Multicast),
    "Skill Multicast availability should be computed independently from skill catalog facts."
);

var primaryMulticastOrder = CollectionFacetAvailability.SnapshotFor(
    new[]
    {
        new CollectionCardVm
        {
            Id = Guid.NewGuid(),
            Type = CardType.Item,
            Size = CardSize.Medium,
            StartingTier = CardTier.Bronze,
            DisplayName = "Damage Multicast Related",
            HiddenTags = new[] { CardHiddenTag.Damage, CardHiddenTag.DamageReference },
            Mechanics = CollectionMechanic.Multicast,
        },
    }
);
AssertValues(
    primaryMulticastOrder
        .KeywordOptionsFor(CardType.Item)
        .Select(option => option.ToString())
        .ToArray(),
    new[]
    {
        nameof(CardHiddenTag.Damage),
        nameof(CollectionMechanic.Multicast),
        nameof(CardHiddenTag.DamageReference),
    },
    "Multicast should sort in the primary keyword area before the Related subsection."
);
AssertFalse(
    CollectionKeywordWhitelist.IsRelatedKeyword(CardHiddenTag.Multicast),
    "The legacy Multicast hidden tag should no longer be classified as Related."
);

var rootDestroyTemplate = MechanicItemTemplate(
    new Dictionary<ETier, TCardTier>
    {
        [ETier.Bronze] = Tier(abilityIds: new[] { "root-destroy" }),
    },
    abilities: new() { ["root-destroy"] = Ability(new TActionCardDestroy()) }
);
var rootDestroyVm = Project(rootDestroyTemplate);
AssertTrue(
    rootDestroyVm.Mechanics.Has(CollectionMechanic.Destroy),
    "A root TActionCardDestroy in an active base ability should project Destroy."
);

var nestedDestroyTemplate = MechanicItemTemplate(
    new Dictionary<ETier, TCardTier>
    {
        [ETier.Bronze] = Tier(abilityIds: new[] { "nested-destroy" }),
    },
    abilities: new()
    {
        ["nested-destroy"] = Ability(
            new TActionAnd
            {
                Actions = new List<ITAction>
                {
                    new TActionAnd { Actions = new List<ITAction> { new TActionCardDestroy() } },
                },
            }
        ),
    }
);
var nestedDestroyVm = Project(nestedDestroyTemplate);
AssertTrue(
    nestedDestroyVm.Mechanics.Has(CollectionMechanic.Destroy),
    "A TActionCardDestroy nested in combined actions should project Destroy."
);

var orphanDestroyTemplate = MechanicItemTemplate(
    new Dictionary<ETier, TCardTier> { [ETier.Bronze] = Tier() },
    abilities: new() { ["orphan-destroy"] = Ability(new TActionCardDestroy()) }
);
AssertFalse(
    Project(orphanDestroyTemplate).Mechanics.Has(CollectionMechanic.Destroy),
    "An unreferenced base destroy ability should not project Destroy."
);

var enchantmentDestroyTemplate = MechanicItemTemplate(
    new Dictionary<ETier, TCardTier> { [ETier.Bronze] = Tier() },
    enchantments: new()
    {
        [EEnchantmentType.Shiny] = new TEnchantment
        {
            Abilities = new Dictionary<string, TCardAbility>
            {
                ["enchanted-destroy"] = Ability(new TActionCardDestroy()),
            },
        },
    }
);
AssertFalse(
    Project(enchantmentDestroyTemplate).Mechanics.Has(CollectionMechanic.Destroy),
    "Enchantment-provided destroy abilities should not pollute base Destroy facts."
);

var transformDestroyedTemplate = MechanicItemTemplate(
    new Dictionary<ETier, TCardTier>
    {
        [ETier.Bronze] = Tier(abilityIds: new[] { "transform-destroyed" }),
    },
    abilities: new()
    {
        ["transform-destroyed"] = Ability(
            new TActionCardTransformDestroyed
            {
                Abilities = new Dictionary<string, TCardAbility>
                {
                    ["derived-destroy"] = Ability(new TActionCardDestroy()),
                },
            }
        ),
    }
);
AssertTrue(
    Project(transformDestroyedTemplate).Mechanics.Has(CollectionMechanic.Destroy),
    "TActionCardTransformDestroyed on an active base ability should project Destroy; nested spawn-template abilities remain unwalked."
);

// Destruction-reaction triggers (#156): each of the three destroy-cluster triggers matches
// Destroy on its own, independent of the ability's action. Non-destroy actions below keep the
// assertion focused on the trigger surface.
var beforeDestroyedTriggerTemplate = MechanicItemTemplate(
    new Dictionary<ETier, TCardTier>
    {
        [ETier.Bronze] = Tier(abilityIds: new[] { "before-destroyed" }),
    },
    abilities: new()
    {
        ["before-destroyed"] = Ability(
            new TActionCardModifyAttribute { AttributeType = ECardAttributeType.DamageAmount },
            new TTriggerOnBeforeCardDestroyed()
        ),
    }
);
AssertTrue(
    Project(beforeDestroyedTriggerTemplate).Mechanics.Has(CollectionMechanic.Destroy),
    "An active base ability triggered by TTriggerOnBeforeCardDestroyed should project Destroy."
);

var cardDestroyedTriggerTemplate = MechanicItemTemplate(
    new Dictionary<ETier, TCardTier>
    {
        [ETier.Bronze] = Tier(abilityIds: new[] { "card-destroyed" }),
    },
    abilities: new()
    {
        ["card-destroyed"] = Ability(
            new TActionCardModifyAttribute { AttributeType = ECardAttributeType.DamageAmount },
            new TTriggerOnCardDestroyed()
        ),
    }
);
AssertTrue(
    Project(cardDestroyedTriggerTemplate).Mechanics.Has(CollectionMechanic.Destroy),
    "An active base ability triggered by TTriggerOnCardDestroyed should project Destroy."
);

var performedDestructionTriggerTemplate = MechanicItemTemplate(
    new Dictionary<ETier, TCardTier>
    {
        [ETier.Bronze] = Tier(abilityIds: new[] { "performed-destruction" }),
    },
    abilities: new()
    {
        ["performed-destruction"] = Ability(
            new TActionCardModifyAttribute { AttributeType = ECardAttributeType.DamageAmount },
            new TTriggerOnCardPerformedDestruction()
        ),
    }
);
AssertTrue(
    Project(performedDestructionTriggerTemplate).Mechanics.Has(CollectionMechanic.Destroy),
    "An active base ability triggered by TTriggerOnCardPerformedDestruction should project Destroy."
);

var orNestedDestructionTriggerTemplate = MechanicItemTemplate(
    new Dictionary<ETier, TCardTier>
    {
        [ETier.Bronze] = Tier(abilityIds: new[] { "or-destroyed" }),
    },
    abilities: new()
    {
        ["or-destroyed"] = Ability(
            new TActionCardModifyAttribute { AttributeType = ECardAttributeType.DamageAmount },
            new TTriggerOr
            {
                Triggers = new List<TTriggerBase>
                {
                    new TTriggerOnCardFired(),
                    new TTriggerOnCardDestroyed(),
                },
            }
        ),
    }
);
AssertTrue(
    Project(orNestedDestructionTriggerTemplate).Mechanics.Has(CollectionMechanic.Destroy),
    "A destruction trigger nested inside TTriggerOr should project Destroy even when other branches are unrelated."
);

var orphanDestructionTriggerTemplate = MechanicItemTemplate(
    new Dictionary<ETier, TCardTier> { [ETier.Bronze] = Tier() },
    abilities: new()
    {
        ["orphan-destroyed"] = Ability(
            new TActionCardModifyAttribute { AttributeType = ECardAttributeType.DamageAmount },
            new TTriggerOnCardDestroyed()
        ),
    }
);
AssertFalse(
    Project(orphanDestructionTriggerTemplate).Mechanics.Has(CollectionMechanic.Destroy),
    "An unreferenced base destruction-reaction ability should not project Destroy."
);

var enchantmentDestructionTriggerTemplate = MechanicItemTemplate(
    new Dictionary<ETier, TCardTier> { [ETier.Bronze] = Tier() },
    enchantments: new()
    {
        [EEnchantmentType.Shiny] = new TEnchantment
        {
            Abilities = new Dictionary<string, TCardAbility>
            {
                ["enchanted-destroyed"] = Ability(
                    new TActionCardModifyAttribute
                    {
                        AttributeType = ECardAttributeType.DamageAmount,
                    },
                    new TTriggerOnCardDestroyed()
                ),
            },
        },
    }
);
AssertFalse(
    Project(enchantmentDestructionTriggerTemplate).Mechanics.Has(CollectionMechanic.Destroy),
    "Enchantment-provided destruction-reaction abilities should not pollute base Destroy facts."
);

var repairedTriggerTemplate = MechanicItemTemplate(
    new Dictionary<ETier, TCardTier> { [ETier.Bronze] = Tier(abilityIds: new[] { "on-repaired" }) },
    abilities: new()
    {
        ["on-repaired"] = Ability(
            new TActionCardModifyAttribute { AttributeType = ECardAttributeType.DamageAmount },
            new TTriggerOnCardRepaired()
        ),
    }
);
AssertFalse(
    Project(repairedTriggerTemplate).Mechanics.Has(CollectionMechanic.Destroy),
    "TTriggerOnCardRepaired should not project Destroy; only destruction-reaction triggers are in this slice."
);

// Remaining destroy-cluster action / tag / attribute surfaces (#157).
var rootRepairTemplate = MechanicItemTemplate(
    new Dictionary<ETier, TCardTier> { [ETier.Bronze] = Tier(abilityIds: new[] { "root-repair" }) },
    abilities: new() { ["root-repair"] = Ability(new TActionCardRepair()) }
);
AssertTrue(
    Project(rootRepairTemplate).Mechanics.Has(CollectionMechanic.Destroy),
    "A root TActionCardRepair in an active base ability should project Destroy."
);

var nestedRepairTemplate = MechanicItemTemplate(
    new Dictionary<ETier, TCardTier>
    {
        [ETier.Bronze] = Tier(abilityIds: new[] { "nested-repair" }),
    },
    abilities: new()
    {
        ["nested-repair"] = Ability(
            new TActionAnd
            {
                Actions = new List<ITAction>
                {
                    new TActionAnd { Actions = new List<ITAction> { new TActionCardRepair() } },
                },
            }
        ),
    }
);
AssertTrue(
    Project(nestedRepairTemplate).Mechanics.Has(CollectionMechanic.Destroy),
    "A TActionCardRepair nested in combined actions should project Destroy."
);

var absorbDestroyTemplate = MechanicItemTemplate(
    new Dictionary<ETier, TCardTier> { [ETier.Bronze] = Tier() },
    hiddenTags: new() { EHiddenTag.AbsorbDestroy }
);
AssertTrue(
    Project(absorbDestroyTemplate).Mechanics.Has(CollectionMechanic.Destroy),
    "The native AbsorbDestroy hidden tag should project Destroy."
);
AssertFalse(
    CollectionKeywordWhitelist.Ordered.Contains(CardHiddenTag.AbsorbDestroy),
    "AbsorbDestroy must not appear as its own keyword chip; it surfaces only via Destroy."
);

var destroyImmunityTemplate = MechanicItemTemplate(
    new Dictionary<ETier, TCardTier>
    {
        [ETier.Bronze] = Tier(attributes: new() { [ECardAttributeType.DestroyImmunity] = 1 }),
    }
);
AssertTrue(
    Project(destroyImmunityTemplate).Mechanics.Has(CollectionMechanic.Destroy),
    "A positive base DestroyImmunity value at any supported tier should project Destroy."
);

var zeroDestroyImmunityTemplate = MechanicItemTemplate(
    new Dictionary<ETier, TCardTier>
    {
        [ETier.Bronze] = Tier(attributes: new() { [ECardAttributeType.DestroyImmunity] = 0 }),
    }
);
AssertFalse(
    Project(zeroDestroyImmunityTemplate).Mechanics.Has(CollectionMechanic.Destroy),
    "A zero base DestroyImmunity value should not project Destroy."
);

var enchantmentRepairTemplate = MechanicItemTemplate(
    new Dictionary<ETier, TCardTier> { [ETier.Bronze] = Tier() },
    enchantments: new()
    {
        [EEnchantmentType.Shiny] = new TEnchantment
        {
            Abilities = new Dictionary<string, TCardAbility>
            {
                ["enchanted-repair"] = Ability(new TActionCardRepair()),
            },
        },
    }
);
AssertFalse(
    Project(enchantmentRepairTemplate).Mechanics.Has(CollectionMechanic.Destroy),
    "Enchantment-provided repair abilities should not pollute base Destroy facts."
);

var enchantmentReplaceDestroyedTemplate = MechanicItemTemplate(
    new Dictionary<ETier, TCardTier> { [ETier.Bronze] = Tier() },
    enchantments: new()
    {
        [EEnchantmentType.Shiny] = new TEnchantment
        {
            Abilities = new Dictionary<string, TCardAbility>
            {
                ["enchanted-transform-destroyed"] = Ability(new TActionCardTransformDestroyed()),
            },
        },
    }
);
AssertFalse(
    Project(enchantmentReplaceDestroyedTemplate).Mechanics.Has(CollectionMechanic.Destroy),
    "Enchantment-provided replace-destroyed abilities should not pollute base Destroy facts."
);

var radiantDestroyImmunityTemplate = MechanicItemTemplate(
    new Dictionary<ETier, TCardTier> { [ETier.Bronze] = Tier() },
    enchantments: new()
    {
        [EEnchantmentType.Radiant] = new TEnchantment
        {
            Attributes = new Dictionary<ECardAttributeType, int>
            {
                [ECardAttributeType.DestroyImmunity] = 1,
            },
        },
    }
);
AssertFalse(
    Project(radiantDestroyImmunityTemplate).Mechanics.Has(CollectionMechanic.Destroy),
    "Radiant destroy immunity on an enchantment must not project base Destroy facts."
);

var tooltipOnlyDestroyTemplate = MechanicItemTemplate(
    new Dictionary<ETier, TCardTier> { [ETier.Bronze] = Tier() }
) with
{
    InternalName = "Destroy Tooltip Card",
    InternalDescription = "Destroy another item when this tooltip is rendered.",
};
AssertFalse(
    Project(tooltipOnlyDestroyTemplate).Mechanics.Has(CollectionMechanic.Destroy),
    "Destroy-related name or description text alone should not project Destroy."
);

var destroySkillTemplate = new TCardSkill
{
    Id = Guid.NewGuid(),
    Type = ECardType.Skill,
    StartingTier = ETier.Bronze,
    InternalName = "Destroy Skill",
    HiddenTags = new HashSet<EHiddenTag>(),
    Tiers = new Dictionary<ETier, TCardTier>
    {
        [ETier.Bronze] = Tier(abilityIds: new[] { "skill-destroy" }),
    },
    Abilities = new Dictionary<string, TCardAbility>
    {
        ["skill-destroy"] = Ability(new TActionCardDestroy()),
    },
};
var destroySkillVm = Project(destroySkillTemplate);
var itemDestroyAvailability = CollectionFacetAvailability.SnapshotFor(new[] { rootDestroyVm });
AssertTrue(
    itemDestroyAvailability.MechanicsFor(CardType.Item).Contains(CollectionMechanic.Destroy),
    "Item Destroy availability should be derived from item catalog facts."
);
AssertFalse(
    itemDestroyAvailability.MechanicsFor(CardType.Skill).Contains(CollectionMechanic.Destroy),
    "Item Destroy facts should not leak into Skill availability."
);
var skillDestroyAvailability = CollectionFacetAvailability.SnapshotFor(new[] { destroySkillVm });
AssertTrue(
    skillDestroyAvailability.MechanicsFor(CardType.Skill).Contains(CollectionMechanic.Destroy),
    "Skill Destroy availability should be derived independently from skill catalog facts."
);
AssertFalse(
    skillDestroyAvailability.MechanicsFor(CardType.Item).Contains(CollectionMechanic.Destroy),
    "Skill Destroy facts should not leak into Item availability."
);

// #158: related-only Destroy facts (no TActionCardDestroy) still make the chip available.
// Item/Skill stay independent; packages never contribute.
var relatedOnlyRepairVm = Project(rootRepairTemplate);
var relatedOnlyItemAvailability = CollectionFacetAvailability.SnapshotFor(
    new[] { relatedOnlyRepairVm }
);
AssertTrue(
    relatedOnlyItemAvailability.MechanicsFor(CardType.Item).Contains(CollectionMechanic.Destroy),
    "A card that matches Destroy only through a related mechanism should make the Destroy chip available on its tab."
);
AssertFalse(
    relatedOnlyItemAvailability.MechanicsFor(CardType.Skill).Contains(CollectionMechanic.Destroy),
    "Related-only Item Destroy facts should not leak into Skill availability."
);

var relatedOnlySkillTemplate = new TCardSkill
{
    Id = Guid.NewGuid(),
    Type = ECardType.Skill,
    StartingTier = ETier.Bronze,
    InternalName = "Repair Skill",
    HiddenTags = new HashSet<EHiddenTag>(),
    Tiers = new Dictionary<ETier, TCardTier>
    {
        [ETier.Bronze] = Tier(abilityIds: new[] { "skill-repair" }),
    },
    Abilities = new Dictionary<string, TCardAbility>
    {
        ["skill-repair"] = Ability(new TActionCardRepair()),
    },
};
var relatedOnlySkillVm = Project(relatedOnlySkillTemplate);
var relatedOnlySkillAvailability = CollectionFacetAvailability.SnapshotFor(
    new[] { relatedOnlySkillVm }
);
AssertTrue(
    relatedOnlySkillAvailability.MechanicsFor(CardType.Skill).Contains(CollectionMechanic.Destroy),
    "Skill related-only Destroy availability should be derived independently from skill catalog facts."
);
AssertFalse(
    relatedOnlySkillAvailability.MechanicsFor(CardType.Item).Contains(CollectionMechanic.Destroy),
    "Related-only Skill Destroy facts should not leak into Item availability."
);

var debugTemplateClassification = CollectionCardClassifier.Classify(
    ECardType.Item,
    ESpawnEligibility.Always,
    "Assets/Cards/Debug.png",
    "[DEBUG] Item"
);
AssertFalse(
    debugTemplateClassification.IsCatalogCard,
    "Debug-marked templates do not enter the catalog even when they have art."
);
AssertEqual(
    CollectionCardEligibilityReason.DebugTemplate,
    debugTemplateClassification.EligibilityReason,
    "Debug-marked templates should report a reasoned rejection."
);
var templateNameClassification = CollectionCardClassifier.Classify(
    ECardType.Item,
    ESpawnEligibility.Always,
    "Assets/Cards/Template.png",
    "[TEMPLATE] Item"
);
AssertFalse(
    templateNameClassification.IsCatalogCard,
    "Template-marked entries do not enter the catalog even when they have art."
);
AssertEqual(
    CollectionCardEligibilityReason.TemplateInternalName,
    templateNameClassification.EligibilityReason,
    "Template-marked entries should report a reasoned rejection."
);
var acceptedSkillClassification = CollectionCardClassifier.Classify(
    ECardType.Skill,
    ESpawnEligibility.Always,
    "Assets/Cards/Skill.png",
    "Real Skill"
);
AssertTrue(
    acceptedSkillClassification.IsCatalogCard,
    "Normal Item/Skill cards with art are catalog cards."
);
AssertEqual(
    CollectionCardEligibilityReason.Accepted,
    acceptedSkillClassification.EligibilityReason,
    "Accepted catalog cards should report the accepted reason."
);
var placeholderSkillClassification = CollectionCardClassifier.Classify(
    ECardType.Skill,
    ESpawnEligibility.Always,
    "Icon_Skill_Placeholder.png",
    "Aggressive Mutations"
);
AssertFalse(
    placeholderSkillClassification.IsCatalogCard,
    "Placeholder skill art is not a catalog-ready skill."
);
AssertEqual(
    CollectionCardEligibilityReason.PlaceholderArtKey,
    placeholderSkillClassification.EligibilityReason,
    "Placeholder art should report a reasoned rejection."
);
var musicNoteSocketClassification = CollectionCardClassifier.Classify(
    ECardType.SocketEffect,
    ESpawnEligibility.Always,
    "Assets/Cards/MusicNote.png",
    "Music Note A"
);
AssertFalse(
    musicNoteSocketClassification.IsCatalogCard,
    "Music Note socket effects should remain outside the Item/Skill catalog."
);
AssertEqual(
    CollectionCardEligibilityReason.UnsupportedType,
    musicNoteSocketClassification.EligibilityReason,
    "Music Note socket effects should remain excluded by their non-Item/Skill card type."
);
var musicNotePlaceholderClassification = CollectionCardClassifier.Classify(
    ECardType.Item,
    ESpawnEligibility.Always,
    "Icon_Music_Note_Placeholder.png",
    "Music Note Placeholder"
);
AssertFalse(
    musicNotePlaceholderClassification.IsCatalogCard,
    "Music Note placeholder art should not enter the catalog even if misclassified as an Item."
);
AssertEqual(
    CollectionCardEligibilityReason.PlaceholderArtKey,
    musicNotePlaceholderClassification.EligibilityReason,
    "Music Note placeholder art should remain excluded by the placeholder-art rule."
);
AssertFalse(
    CollectionCardClassifier
        .Classify(ECardType.Skill, ESpawnEligibility.Always, "Placeholder", "[SKILL TEMPLATE]")
        .IsCatalogCard,
    "Skill template placeholders do not enter the catalog."
);
var materialArtClassification = CollectionCardClassifier.Classify(
    ECardType.Item,
    ESpawnEligibility.Always,
    "Assets/Cards/LegacyItem.mat",
    "Legacy Material Item"
);
AssertFalse(
    materialArtClassification.IsCatalogCard,
    "Legacy material art keys do not enter the catalog."
);
AssertEqual(
    CollectionCardEligibilityReason.MaterialArtKey,
    materialArtClassification.EligibilityReason,
    "Material art keys should report a reasoned rejection."
);
AssertFalse(
    CollectionCardClassifier
        .Classify(
            ECardType.Item,
            ESpawnEligibility.Always,
            "Assets/Cards/Template.png",
            "[SMALL ITEM TEMPLATE]"
        )
        .IsCatalogCard,
    "Bracketed item template names do not enter the catalog."
);
AssertEqual(
    CollectionCardEligibilityReason.InvalidArtKey,
    CollectionCardClassifier
        .Classify(ECardType.Item, ESpawnEligibility.Always, "Invalid", "Real Item")
        .EligibilityReason,
    "The literal Invalid art key should report a reasoned rejection."
);
AssertEqual(
    CollectionCardEligibilityReason.MissingArtKey,
    CollectionCardClassifier
        .Classify(ECardType.Item, ESpawnEligibility.Always, "", "Real Item")
        .EligibilityReason,
    "A missing art key should report a reasoned rejection."
);
AssertEqual(
    CollectionCardEligibilityReason.UnsupportedType,
    CollectionCardClassifier
        .Classify((ECardType)999, ESpawnEligibility.Always, "Assets/Cards/Event.png", "Event")
        .EligibilityReason,
    "Unsupported card types should report a reasoned rejection."
);
var catalogEligibilityCases = new[]
{
    (
        Name: "Item Always",
        Template: CatalogTemplate(ECardType.Item, ESpawnEligibility.Always),
        ExpectedCatalogCard: true,
        ExpectedReason: CollectionCardEligibilityReason.Accepted
    ),
    (
        Name: "Skill Always",
        Template: CatalogTemplate(ECardType.Skill, ESpawnEligibility.Always),
        ExpectedCatalogCard: true,
        ExpectedReason: CollectionCardEligibilityReason.Accepted
    ),
    (
        Name: "Item GuidOnly",
        Template: CatalogTemplate(ECardType.Item, ESpawnEligibility.GuidOnly),
        ExpectedCatalogCard: true,
        ExpectedReason: CollectionCardEligibilityReason.Accepted
    ),
    (
        Name: "Skill GuidOnly",
        Template: CatalogTemplate(ECardType.Skill, ESpawnEligibility.GuidOnly),
        ExpectedCatalogCard: true,
        ExpectedReason: CollectionCardEligibilityReason.Accepted
    ),
    (
        Name: "Item Never",
        Template: CatalogTemplate(ECardType.Item, ESpawnEligibility.Never),
        ExpectedCatalogCard: false,
        ExpectedReason: CollectionCardEligibilityReason.NeverSpawnEligibility
    ),
    (
        Name: "Skill Never",
        Template: CatalogTemplate(ECardType.Skill, ESpawnEligibility.Never),
        ExpectedCatalogCard: false,
        ExpectedReason: CollectionCardEligibilityReason.NeverSpawnEligibility
    ),
};
foreach (var testCase in catalogEligibilityCases)
{
    var classification = CollectionCardClassifier.Classify(testCase.Template);
    AssertEqual(
        testCase.ExpectedCatalogCard,
        classification.IsCatalogCard,
        $"{testCase.Name} should have the expected catalog eligibility."
    );
    AssertEqual(
        testCase.ExpectedReason,
        classification.EligibilityReason,
        $"{testCase.Name} should report the expected catalog eligibility reason."
    );
}
var hiddenTagPackageTemplate = new TCardItem
{
    Id = Guid.NewGuid(),
    Type = ECardType.Item,
    SpawningEligibility = ESpawnEligibility.GuidOnly,
    ArtKey = "Assets/Cards/Bundle.png",
    InternalName = "Vanessa Starter Bundle",
    HiddenTags = new HashSet<EHiddenTag> { EHiddenTag.Package },
};
var hiddenTagPackageClassification = CollectionCardClassifier.Classify(hiddenTagPackageTemplate);
AssertTrue(
    hiddenTagPackageClassification.IsCatalogCard,
    "GuidOnly package templates remain eligible for the catalog classifier."
);
AssertTrue(
    hiddenTagPackageClassification.IsPackage,
    "HiddenTag.Package marks package templates as hidden from CollectionPanel."
);
AssertSequence(
    CollectionFilterEngine.Apply(
        new[] { Project(hiddenTagPackageTemplate, hiddenTagPackageClassification) },
        new CollectionFilterState()
    ),
    Array.Empty<Guid>(),
    "GuidOnly package templates remain hidden by the existing package visibility rule."
);
var packageNameWithoutTagTemplate = new TCardItem
{
    Type = ECardType.Item,
    ArtKey = "Assets/Cards/Package.png",
    InternalName = "Vanessa Starter Package",
};
AssertFalse(
    CollectionCardClassifier.Classify(packageNameWithoutTagTemplate).IsPackage,
    "Package-like names no longer mark packages without HiddenTag.Package."
);

var tooltipOnlySearchVm = Project(
    new TCardItem
    {
        Id = Guid.NewGuid(),
        Type = ECardType.Item,
        StartingTier = ETier.Bronze,
        Size = ECardSize.Small,
        InternalName = "Tooltip Search Probe",
        ArtKey = "tooltip-search-probe",
        Heroes = new HashSet<EHero> { EHero.Common },
        Localization = new TCardLocalization
        {
            Title = new TLocalizableText { Text = "Tooltip Search Probe" },
            Tooltips = new List<TTooltip>
            {
                new()
                {
                    Content = new TLocalizableText
                    {
                        Text = "Deal <color=#ff0000>Burn</color> to enemies {ability.0}.",
                    },
                },
            },
        },
    }
);
var localizationHashSearchVm = Project(
    new TCardItem
    {
        Id = Guid.NewGuid(),
        Type = ECardType.Skill,
        StartingTier = ETier.Bronze,
        Size = ECardSize.Medium,
        InternalName = "Hash Search Probe",
        ArtKey = "hash-search-probe",
        Heroes = new HashSet<EHero> { EHero.Common },
        Localization = new TCardLocalization
        {
            Title = new TLocalizableText
            {
                Key = "69d1f37d94ddc3150f8dd44d12d53ced",
                Text = "Gumball Machine",
            },
        },
    }
);
AssertSequence(
    CollectionFilterEngine.Apply(
        new[] { tooltipOnlySearchVm },
        new CollectionFilterState { SearchQuery = "burn enemies" }
    ),
    new[] { tooltipOnlySearchVm.Id },
    "Collection search should match normalized tooltip text when a card has no description."
);
AssertSequence(
    CollectionFilterEngine.Apply(
        new[] { localizationHashSearchVm },
        new CollectionFilterState { ActiveType = CardType.Skill, SearchQuery = "ddddddddd" }
    ),
    Array.Empty<Guid>(),
    "Collection search should not expose opaque localization hashes as searchable content."
);
AssertSequence(
    CollectionFilterEngine.Apply(
        new[] { localizationHashSearchVm },
        new CollectionFilterState { ActiveType = CardType.Skill, SearchQuery = "gumball" }
    ),
    new[] { localizationHashSearchVm.Id },
    "Removing localization hashes should preserve authored fallback text search."
);
AssertEqual(
    "Your Burn items have +5 Burn.",
    CollectionAbilityPlaceholders.DropAuraPlaceholders("Your Burn items have +5 Burn. [{aura.0}]"),
    "An aura placeholder should drop with the brackets it leaves."
);
AssertEqual(
    "Shield equal to 10% of your Max Health",
    CollectionAbilityPlaceholders.DropAuraPlaceholders(
        "Shield equal to 10% of your Max Health ({aura.e1})"
    ),
    "A parenthesized aura placeholder should drop with its parentheses."
);
AssertEqual(
    "Gain {ability.0} Gold",
    CollectionAbilityPlaceholders.DropAuraPlaceholders("Gain {ability.0} Gold"),
    "Text without an aura placeholder should be left alone, ability tokens included."
);

Console.WriteLine("CollectionCardProjection checks passed.");

static CollectionCardVm Project(
    TCardBase template,
    CollectionCardClassification? classification = null
) =>
    (
        classification == null
            ? CollectionCardVmProjection.From(template)
            : CollectionCardVmProjection.From(template, classification)
    )
    ?? throw new InvalidOperationException(
        $"{template.InternalName} has no card facet projection."
    );

static TCardBase CatalogTemplate(ECardType type, ESpawnEligibility spawningEligibility) =>
    type switch
    {
        ECardType.Item => new TCardItem
        {
            Type = ECardType.Item,
            SpawningEligibility = spawningEligibility,
            ArtKey = "Assets/Cards/CatalogItem.png",
            InternalName = "Catalog Item",
        },
        ECardType.Skill => new TCardSkill
        {
            Type = ECardType.Skill,
            SpawningEligibility = spawningEligibility,
            ArtKey = "Assets/Cards/CatalogSkill.png",
            InternalName = "Catalog Skill",
        },
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

static TCardItem MechanicItemTemplate(
    Dictionary<ETier, TCardTier> tiers,
    Dictionary<string, TCardAbility>? abilities = null,
    Dictionary<string, TCardAura>? auras = null,
    Dictionary<EEnchantmentType, TEnchantment>? enchantments = null,
    HashSet<EHiddenTag>? hiddenTags = null
) =>
    new()
    {
        Id = Guid.NewGuid(),
        Type = ECardType.Item,
        StartingTier = ETier.Bronze,
        Size = ECardSize.Medium,
        InternalName = "Multicast Projection Item",
        ArtKey = "Assets/Cards/MulticastProjectionItem.png",
        HiddenTags = hiddenTags ?? new HashSet<EHiddenTag>(),
        Tiers = tiers,
        Abilities = abilities ?? new Dictionary<string, TCardAbility>(),
        Auras = auras ?? new Dictionary<string, TCardAura>(),
        Enchantments = enchantments,
    };

static TCardTier Tier(
    Dictionary<ECardAttributeType, int>? attributes = null,
    IReadOnlyCollection<string>? abilityIds = null,
    IReadOnlyCollection<string>? auraIds = null
) =>
    new()
    {
        Attributes = attributes ?? new Dictionary<ECardAttributeType, int>(),
        AbilityIds = abilityIds == null ? new HashSet<string>() : new HashSet<string>(abilityIds),
        AuraIds = auraIds == null ? new HashSet<string>() : new HashSet<string>(auraIds),
    };

static TCardAbility Ability(ITAction action, TTriggerBase? trigger = null) =>
    new()
    {
        Id = Guid.NewGuid().ToString(),
        Action = action,
        Trigger = trigger ?? new TTriggerOnCardFired(),
    };

static TCardAura Aura(ITAuraAction action) =>
    new() { Id = Guid.NewGuid().ToString(), Action = action };

static TActionCardModifyAttribute MulticastModifier() =>
    new() { AttributeType = ECardAttributeType.Multicast };

static TAuraActionCardModifyAttribute MulticastAuraModifier() =>
    new() { AttributeType = ECardAttributeType.Multicast };

static void AssertSequence(
    IReadOnlyList<CollectionCardVm> actual,
    IReadOnlyList<Guid> expected,
    string message
)
{
    var actualIds = new Guid[actual.Count];
    for (var i = 0; i < actual.Count; i++)
        actualIds[i] = actual[i].Id;
    AssertValues(actualIds, expected, message);
}

static void AssertValues<T>(IReadOnlyList<T> actual, IReadOnlyList<T> expected, string message)
{
    if (actual.Count != expected.Count)
        throw new InvalidOperationException(
            $"{message} Expected {expected.Count} values, got {actual.Count}."
        );
    for (var i = 0; i < actual.Count; i++)
    {
        if (!EqualityComparer<T>.Default.Equals(actual[i], expected[i]))
            throw new InvalidOperationException(
                $"{message} At {i}: expected {expected[i]}, got {actual[i]}."
            );
    }
}

static void AssertEqual<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{message} Expected {expected}, got {actual}.");
}

static void AssertTrue(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static void AssertFalse(bool condition, string message) => AssertTrue(!condition, message);

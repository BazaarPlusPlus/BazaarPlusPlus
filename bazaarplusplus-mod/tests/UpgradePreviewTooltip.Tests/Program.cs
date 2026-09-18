using BazaarGameClient.Domain.Cards;
using BazaarGameClient.Domain.Models;
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameClient.Domain.Tooltips;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Enchantments;
using BazaarGameShared.Domain.Cards.Item;
using BazaarGameShared.Domain.Core;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Effect;
using BazaarGameShared.Domain.Effect.Actions;
using BazaarGameShared.Domain.Effect.AuraActions;
using BazaarGameShared.Domain.Targeting;
using BazaarGameShared.Domain.Values;
using BazaarGameShared.Domain.Values.ReferenceValues;
using BazaarPlusPlus.Game.Tooltips;

var bongosValueReference = new TReferenceValueCardAttribute
{
    AttributeType = ECardAttributeType.Custom_1,
    Target = new TTargetCardSelf(),
    Modifier = new TValueModifier
    {
        ModifyMode = EValueModifierMode.Multiply,
        Value = new TFixedValue { Value = 0.2f },
    },
};
var fieryAbility = new TCardAbility
{
    Id = "fiery",
    Action = new TActionCardModifyAttribute
    {
        AttributeType = ECardAttributeType.BurnApplyAmount,
        Target = new TTargetCardSelf(),
        Value = bongosValueReference,
    },
};
var template = new TCardItem
{
    Type = ECardType.Item,
    StartingTier = ETier.Bronze,
    Enchantments = new Dictionary<EEnchantmentType, TEnchantment>
    {
        [EEnchantmentType.Fiery] = new TEnchantment
        {
            Abilities = new Dictionary<string, TCardAbility> { [fieryAbility.Id] = fieryAbility },
        },
    },
    Tiers = new Dictionary<ETier, TCardTier>
    {
        [ETier.Bronze] = new TCardTier
        {
            Attributes = new Dictionary<ECardAttributeType, int>
            {
                [ECardAttributeType.Custom_1] = 5,
            },
        },
        [ETier.Silver] = new TCardTier
        {
            Attributes = new Dictionary<ECardAttributeType, int>
            {
                [ECardAttributeType.Custom_1] = 10,
            },
        },
        [ETier.Gold] = new TCardTier
        {
            Attributes = new Dictionary<ECardAttributeType, int>
            {
                [ECardAttributeType.Custom_1] = 15,
            },
        },
    },
};
var card = new ItemCard
{
    Template = template,
    Type = ECardType.Item,
    Tier = ETier.Silver,
    Enchantment = EEnchantmentType.Fiery,
    Attributes = new Dictionary<ECardAttributeType, int> { [ECardAttributeType.Custom_1] = 10 },
};
var currentContext = new TooltipContext(card, template, new ValueContext(null!, card));
var currentToken = TooltipComponentAbility.Create(currentContext, fieryAbility.Id, null, 0);

AssertNotNull(currentToken, "Current Fiery token should be created.");
AssertFloatEqual(2f, currentToken!.Resolve()!.Value, "Current derived Burn value should be 2.");
AssertTrue(
    UpgradePreviewValueProjection.TryCreate(
        card,
        template,
        currentContext.ValueContext,
        out var projection
    ),
    "Upgrade projection should be created."
);
AssertEqual(
    15,
    projection.Card.Attributes[ECardAttributeType.Custom_1],
    "Projected Bongos value should use the Gold base."
);
AssertTrue(
    projection.TryResolve(currentToken, out var upgradedBurn),
    "Projected Fiery token should resolve."
);
AssertFloatEqual(3f, upgradedBurn, "Derived Burn should be recomputed as 20% of 15, not 2 + 5.");

var guzhengAura = new TCardAura
{
    Id = "guzheng-damage-per-tempo",
    Action = new TAuraActionCardModifyAttribute
    {
        AttributeType = ECardAttributeType.DamageAmount,
        Operation = EAttributeModifierOperation.Add,
        Target = new TTargetCardSelf(),
        Value = new TReferenceValuePlayerAttribute
        {
            AttributeType = EPlayerAttributeType.Tempo,
            Modifier = new TValueModifier
            {
                ModifyMode = EValueModifierMode.Multiply,
                Value = new TReferenceValueCardAttribute
                {
                    AttributeType = ECardAttributeType.Custom_0,
                    Target = new TTargetCardSelf(),
                    Modifier = new TValueModifier
                    {
                        ModifyMode = EValueModifierMode.Multiply,
                        Value = new TFixedValue { Value = 1f },
                    },
                },
            },
        },
    },
};
var guzhengTemplate = new TCardItem
{
    Type = ECardType.Item,
    StartingTier = ETier.Gold,
    Auras = new Dictionary<string, TCardAura> { [guzhengAura.Id] = guzhengAura },
    Tiers = new Dictionary<ETier, TCardTier>
    {
        [ETier.Gold] = new TCardTier
        {
            Attributes = new Dictionary<ECardAttributeType, int>
            {
                [ECardAttributeType.Custom_0] = 15,
            },
        },
        [ETier.Diamond] = new TCardTier
        {
            Attributes = new Dictionary<ECardAttributeType, int>
            {
                [ECardAttributeType.Custom_0] = 30,
            },
        },
    },
};
var guzhengCard = new ItemCard
{
    Template = guzhengTemplate,
    Type = ECardType.Item,
    Tier = ETier.Gold,
    Attributes = new Dictionary<ECardAttributeType, int> { [ECardAttributeType.Custom_0] = 15 },
};
var guzhengContext = new TooltipContext(
    guzhengCard,
    guzhengTemplate,
    new ValueContext(null!, guzhengCard)
);
var guzhengToken = TooltipComponentAura.Create(
    guzhengContext,
    guzhengAura.Id,
    ETooltipAccessorType.Mod,
    0
);

AssertNotNull(guzhengToken, "Guzheng aura token should be created.");
AssertTrue(
    !guzhengToken!.ReferencedAttribute.HasValue,
    "Guzheng's player-attribute aura intentionally has no native referenced card attribute."
);
AssertFloatEqual(15f, guzhengToken.Resolve()!.Value, "Current Guzheng multiplier should be 15.");
AssertTrue(
    UpgradePreviewValueProjection.TryCreate(
        guzhengCard,
        guzhengTemplate,
        guzhengContext.ValueContext,
        out var guzhengProjection
    ),
    "Guzheng upgrade projection should be created."
);
AssertTrue(
    guzhengProjection.TryResolve(guzhengToken, out var upgradedGuzhengMultiplier),
    "Projected Guzheng aura token should resolve."
);
AssertFloatEqual(
    30f,
    upgradedGuzhengMultiplier,
    "Guzheng Damage and Shield per Tempo should upgrade from 15 to 30."
);

card.Attributes[ECardAttributeType.Custom_1] = 12;
AssertTrue(
    UpgradePreviewValueProjection.TryCreate(
        card,
        template,
        currentContext.ValueContext,
        out var buffedProjection
    ),
    "Buffed upgrade projection should be created."
);
AssertEqual(
    17,
    buffedProjection.Card.Attributes[ECardAttributeType.Custom_1],
    "Runtime buffs should be preserved over the next-tier base."
);

var doubleDamageAura = new TCardAura
{
    Id = "double-damage",
    ActiveIn = EEffectActiveIn.HandAndStash,
    WorksIn = EEffectWorksIn.Anywhere,
    Action = new TAuraActionCardModifyAttribute
    {
        AttributeType = ECardAttributeType.DamageAmount,
        Operation = EAttributeModifierOperation.Multiply,
        Target = new TTargetCardSelf(),
        Value = new TFixedValue { Value = 2f },
    },
};
var doubleAmmoAura = doubleDamageAura with
{
    Id = "double-ammo",
    Action = new TAuraActionCardModifyAttribute
    {
        AttributeType = ECardAttributeType.AmmoMax,
        Operation = EAttributeModifierOperation.Multiply,
        Target = new TTargetCardSelf(),
        Value = new TFixedValue { Value = 2f },
    },
};
var multipliedTemplate = new TCardItem
{
    Type = ECardType.Item,
    StartingTier = ETier.Silver,
    Enchantments = new Dictionary<EEnchantmentType, TEnchantment>
    {
        [EEnchantmentType.Obsidian] = new TEnchantment
        {
            Auras = new Dictionary<string, TCardAura>
            {
                [doubleDamageAura.Id] = doubleDamageAura,
                [doubleAmmoAura.Id] = doubleAmmoAura,
            },
        },
    },
    Tiers = new Dictionary<ETier, TCardTier>
    {
        [ETier.Silver] = new TCardTier
        {
            Attributes = new Dictionary<ECardAttributeType, int>
            {
                [ECardAttributeType.DamageAmount] = 10,
                [ECardAttributeType.AmmoMax] = 4,
            },
        },
        [ETier.Gold] = new TCardTier
        {
            Attributes = new Dictionary<ECardAttributeType, int>
            {
                [ECardAttributeType.DamageAmount] = 15,
                [ECardAttributeType.AmmoMax] = 5,
            },
        },
    },
};
var multipliedCard = new ItemCard
{
    Template = multipliedTemplate,
    Type = ECardType.Item,
    Tier = ETier.Silver,
    Section = EInventorySection.Hand,
    Enchantment = EEnchantmentType.Obsidian,
    // (10 base + 2 additive runtime buff) * 2 Obsidian multiplier.
    Attributes = new Dictionary<ECardAttributeType, int>
    {
        [ECardAttributeType.DamageAmount] = 24,
        [ECardAttributeType.AmmoMax] = 8,
    },
};
AssertTrue(
    UpgradePreviewValueProjection.TryCreate(
        multipliedCard,
        multipliedTemplate,
        new ValueContext(null!, multipliedCard),
        out var multipliedProjection
    ),
    "Multiplied upgrade projection should be created."
);
AssertEqual(
    34,
    multipliedProjection.Card.Attributes[ECardAttributeType.DamageAmount],
    "Multipliers should scale the next-tier base after preserving additive runtime buffs."
);
AssertEqual(
    10,
    multipliedProjection.Card.Attributes[ECardAttributeType.AmmoMax],
    "Ammo multipliers should scale the next-tier base instead of being treated as additive."
);

var externalDoubleAura = doubleDamageAura with
{
    Id = "external-double-damage",
    Action = new TAuraActionCardModifyAttribute
    {
        AttributeType = ECardAttributeType.DamageAmount,
        Operation = EAttributeModifierOperation.Multiply,
        Target = new TTargetCardSection
        {
            TargetSection = ETargetCardSectionTargetSection.SelfHand,
        },
        Value = new TFixedValue { Value = 2f },
    },
};
var externalAuraTemplate = new TCardItem
{
    Type = ECardType.Item,
    StartingTier = ETier.Silver,
    Auras = new Dictionary<string, TCardAura> { [externalDoubleAura.Id] = externalDoubleAura },
    Tiers = new Dictionary<ETier, TCardTier>
    {
        [ETier.Silver] = new TCardTier { AuraIds = [externalDoubleAura.Id] },
    },
};
var externalTargetTemplate = multipliedTemplate with { Enchantments = null };
var player = new Player { CombatantId = ECombatantId.Player };
var run = new Run { Player = player };
var externalTarget = new ItemCard
{
    InstanceId = new InstanceId("external-target"),
    Template = externalTargetTemplate,
    Type = ECardType.Item,
    Size = ECardSize.Small,
    Tier = ETier.Silver,
    Section = EInventorySection.Hand,
    LeftSocketId = EContainerSocketId.Socket_0,
    Owner = player,
    Attributes = new Dictionary<ECardAttributeType, int> { [ECardAttributeType.DamageAmount] = 24 },
};
var externalAuraSource = new ItemCard
{
    InstanceId = new InstanceId("external-aura-source"),
    Template = externalAuraTemplate,
    Type = ECardType.Item,
    Size = ECardSize.Small,
    Tier = ETier.Silver,
    Section = EInventorySection.Hand,
    LeftSocketId = EContainerSocketId.Socket_1,
    Owner = player,
};
((CardContainer)player.Hand).Container.Sockets[0] = externalTarget;
((CardContainer)player.Hand).Container.Sockets[1] = externalAuraSource;
AssertTrue(
    UpgradePreviewValueProjection.TryCreate(
        externalTarget,
        externalTargetTemplate,
        new ValueContext(run, externalTarget),
        out var externalMultiplierProjection
    ),
    "External multiplier projection should be created."
);
AssertEqual(
    34,
    externalMultiplierProjection.Card.Attributes[ECardAttributeType.DamageAmount],
    "Multipliers from other active cards should be preserved during projection."
);

var cooldownTemplate = new TCardItem
{
    Type = ECardType.Item,
    StartingTier = ETier.Silver,
    Tiers = new Dictionary<ETier, TCardTier>
    {
        [ETier.Silver] = new TCardTier
        {
            Attributes = new Dictionary<ECardAttributeType, int>
            {
                [ECardAttributeType.CooldownMax] = 7000,
            },
        },
        [ETier.Gold] = new TCardTier
        {
            Attributes = new Dictionary<ECardAttributeType, int>
            {
                [ECardAttributeType.CooldownMax] = 6000,
            },
        },
    },
};
var cooldownCard = new ItemCard
{
    Template = cooldownTemplate,
    Type = ECardType.Item,
    Tier = ETier.Silver,
    Attributes = new Dictionary<ECardAttributeType, int>
    {
        // Runtime materializes the active 50% reduction into CooldownMax while retaining
        // PercentCooldownReduction as the source modifier.
        [ECardAttributeType.CooldownMax] = 3500,
        [ECardAttributeType.PercentCooldownReduction] = 50,
    },
};
AssertTrue(
    UpgradePreviewValueProjection.TryCreate(
        cooldownCard,
        cooldownTemplate,
        new ValueContext(null!, cooldownCard),
        out var cooldownProjection
    ),
    "Cooldown upgrade projection should be created."
);
AssertEqual(
    3000,
    cooldownProjection.Card.Attributes[ECardAttributeType.CooldownMax],
    "Projected cooldown should apply the active percentage once to the next-tier base."
);
AssertTrue(
    cooldownProjection.TryResolveEffectiveCooldowns(
        out var currentCooldown,
        out var upgradedCooldown
    ),
    "Effective cooldowns should resolve."
);
AssertFloatEqual(3.5f, currentCooldown, "50% reduction should halve the current cooldown.");
AssertFloatEqual(3f, upgradedCooldown, "50% reduction should halve the upgraded cooldown.");

cooldownCard.Attributes[ECardAttributeType.CooldownMax] = 4000;
AssertTrue(
    UpgradePreviewValueProjection.TryCreate(
        cooldownCard,
        cooldownTemplate,
        new ValueContext(null!, cooldownCard),
        out var buffedCooldownProjection
    ),
    "Buffed cooldown projection should be created."
);
AssertEqual(
    3500,
    buffedCooldownProjection.Card.Attributes[ECardAttributeType.CooldownMax],
    "Additive cooldown changes should be preserved before applying the active percentage."
);

cooldownCard.Attributes[ECardAttributeType.CooldownMax] = 8000;
cooldownCard.Attributes[ECardAttributeType.PercentCooldownReduction] = 0;
AssertTrue(
    UpgradePreviewValueProjection.TryCreate(
        cooldownCard,
        cooldownTemplate,
        new ValueContext(null!, cooldownCard),
        out var additiveCooldownProjection
    ),
    "Additive-only cooldown projection should be created."
);
AssertEqual(
    7000,
    additiveCooldownProjection.Card.Attributes[ECardAttributeType.CooldownMax],
    "Additive cooldown changes should remain additive when no percentage is active."
);

AssertFloatEqual(
    2f,
    UpgradePreviewValueRegistry.ConvertProjectedValueToRenderedUnits(
        ECardAttributeType.HasteAmount,
        null,
        2000f
    ),
    "Projected duration values should convert from milliseconds exactly once."
);
AssertFloatEqual(
    2f,
    UpgradePreviewValueRegistry.ConvertProjectedValueToRenderedUnits(
        ECardAttributeType.Custom_1,
        ECardAttributeType.HasteAmount,
        2000f
    ),
    "A duration style override should control projected-value conversion."
);
AssertFloatEqual(
    15f,
    UpgradePreviewValueRegistry.ConvertProjectedValueToRenderedUnits(
        ECardAttributeType.Custom_1,
        null,
        15f
    ),
    "Non-duration projected values should keep their raw units."
);
AssertTrue(
    UpgradePreviewValueRegistry.HaveSameFormattedValue(3.01f, 3.02f),
    "Values that render identically should not show a redundant upgrade arrow."
);

Console.WriteLine("Upgrade preview tooltip projection tests passed.");

static void AssertNotNull(object? value, string message)
{
    if (value == null)
        throw new InvalidOperationException(message);
}

static void AssertTrue(bool value, string message)
{
    if (!value)
        throw new InvalidOperationException(message);
}

static void AssertEqual<T>(T expected, T actual, string message)
    where T : IEquatable<T>
{
    if (!expected.Equals(actual))
        throw new InvalidOperationException($"{message} Expected={expected}, Actual={actual}");
}

static void AssertFloatEqual(float expected, float actual, string message)
{
    const float tolerance = 0.0001f;
    if (MathF.Abs(expected - actual) > tolerance)
        throw new InvalidOperationException($"{message} Expected={expected}, Actual={actual}");
}

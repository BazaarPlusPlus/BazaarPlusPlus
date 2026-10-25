#nullable enable
using System.Text;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Interfaces;
using BazaarGameShared.Domain.Cards.Item;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Effect;
using BazaarGameShared.Domain.Effect.Actions;
using BazaarGameShared.Infra.Messages.CombatSimEvents;
using BazaarGameShared.Infra.Messages.GameSimEvents;
using BazaarGameShared.Infra.Serialization;
using BazaarPlusPlus.Core.Combat;
using BazaarPlusPlus.Game.PostCombatImpact.Data;
using BazaarPlusPlus.GameInterop.Cards;
using BazaarPlusPlus.GameInterop.CombatSimulation;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;

internal sealed class CombatImpactCorpusCatalog
{
    private readonly IReadOnlyDictionary<Guid, TCardBase> _cards;

    private CombatImpactCorpusCatalog(IReadOnlyDictionary<Guid, TCardBase> cards)
    {
        _cards = cards;
    }

    internal static CombatImpactCorpusCatalog Load(string databasePath)
    {
        if (!File.Exists(databasePath))
            throw new FileNotFoundException("GameData.db was not found.", databasePath);

        var cards = new Dictionary<Guid, TCardBase>();
        var settings = new BazaarJsonSerializerSettings(validate: false);
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString();
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Data FROM cards ORDER BY Id";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (!Guid.TryParse(reader.GetString(0), out var id))
                continue;
            var payload = reader.GetValue(1) switch
            {
                string text => text,
                byte[] bytes => Encoding.UTF8.GetString(bytes),
                var value => Convert.ToString(
                    value,
                    System.Globalization.CultureInfo.InvariantCulture
                ),
            };
            if (string.IsNullOrWhiteSpace(payload))
                continue;
            var card = JsonConvert.DeserializeObject<TCardBase>(payload!, settings);
            if (card != null)
                cards[id] = card;
        }

        if (cards.Count == 0)
            throw new InvalidOperationException("GameData.db yielded no card templates.");
        return new CombatImpactCorpusCatalog(cards);
    }

    internal CombatImpactCorpusEntityBuild BuildEntities(
        GameSim spawn,
        CombatSim combat,
        CombatTimeline timeline
    )
    {
        var seeds = new Dictionary<string, CorpusCardSeed>(StringComparer.Ordinal);
        foreach (var spawned in spawn.Events.OfType<GameSimEventCardSpawned>())
        {
            if (!Guid.TryParse(spawned.TemplateId, out var templateId))
                continue;
            spawn.Cards.TryGetValue(spawned.InstanceId, out var update);
            seeds[spawned.InstanceId] = new CorpusCardSeed(
                spawned.InstanceId,
                templateId,
                spawned.Type,
                spawned.CombatantId,
                update?.Tier,
                update?.Enchantment,
                ReadAttributes(update)
            );
        }

        foreach (var snapshot in CombatImpactTransformedCardSnapshotReader.Read(combat))
        {
            if (!Guid.TryParse(snapshot.Card.TemplateId, out var templateId))
                continue;
            seeds.TryAdd(
                snapshot.Card.InstanceId,
                new CorpusCardSeed(
                    snapshot.Card.InstanceId,
                    templateId,
                    snapshot.Card.Type,
                    snapshot.Card.CombatantId,
                    snapshot.Update?.Tier,
                    snapshot.Update?.Enchantment,
                    ReadAttributes(snapshot.Update)
                )
            );
        }

        var entities = new Dictionary<string, CombatImpactEntity>(StringComparer.Ordinal);
        var order = 0;
        foreach (var seed in seeds.Values.OrderBy(seed => seed.InstanceId, StringComparer.Ordinal))
        {
            if (!_cards.TryGetValue(seed.TemplateId, out var template))
                continue;
            var tier = seed.Tier ?? template.StartingTier;
            var activeAbilities = ActiveAbilities(template, tier, seed.Enchantment);
            var activeAuras = template is IHasTierData auraTiered
                ? auraTiered.GetAuraTemplatesByTier(tier).ToList()
                : template.Auras.Values.ToList();
            if (
                seed.Enchantment is { } enchantment
                && template is TCardItem auraItem
                && auraItem.Enchantments?.TryGetValue(enchantment, out var enchantmentTemplate)
                    == true
            )
                activeAuras.AddRange(enchantmentTemplate.Auras.Values);
            var attributeTypes = ReadAbilityAttributeTypes(template, seed.Enchantment);
            entities[seed.InstanceId] = new CombatImpactEntity(
                seed.InstanceId,
                template.InternalName,
                seed.Type.ToString(),
                null,
                order++,
                seed.TemplateId,
                DesktopCardFacets.CapturedTier(tier),
                EnchantmentType: DesktopCardFacets.CapturedEnchantment(seed.Enchantment),
                Attributes: seed.Attributes == null
                    ? null
                    : DesktopCombatFacets.CardAttributes(seed.Attributes),
                CombatantId: DesktopCombatFacets.ToCombatant(seed.Combatant),
                AbilityAttributeTypesByEffectId: DesktopCombatFacets.AttributesByEffectId(
                    attributeTypes
                ),
                PrerequisiteSkillSourceRulesByEffectId: CombatImpactAttributionRuleReader.ReadSourceRules(
                    activeAbilities,
                    []
                ),
                UseAttributionRules: seed.Type == ECardType.SocketEffect
                    ? CombatImpactAttributionRuleReader.ReadUseRules(activeAbilities)
                    : null,
                AbilityAttributeModifiersByEffectId: CombatImpactAbilityAttributeModifierReader.Read(
                    activeAbilities
                ),
                CriticalTriggerAbilitiesByEffectId: CombatImpactCriticalTriggerReader.Read(
                    activeAbilities
                ),
                AuraAttributeModifiersByEffectId: CombatImpactAbilityAttributeModifierReader.ReadAuras(
                    activeAuras
                ),
                ReferenceValuedAuraEffectIds: activeAuras
                    .Where(aura =>
                        aura.Action
                            is BazaarGameShared.Domain.Effect.AuraActions.TAuraActionCardModifyAttribute modifier
                        && modifier.Value
                            is BazaarGameShared.Domain.Values.ReferenceValues.ITReferenceValue
                    )
                    .Select(aura => aura.Id)
                    .ToArray()
            );
        }

        foreach (var combatant in new[] { Combatant.Player, Combatant.Opponent })
        {
            var id = CombatImpactProjector.PlayerId(combatant);
            entities[id] = new CombatImpactEntity(
                id,
                combatant.ToString(),
                "Player",
                null,
                order++,
                CombatantId: combatant
            );
        }
        var implicitPlayerEffects = CombatImpactImplicitPlayerEffectReader.AddMissing(
            timeline,
            entities,
            _cards.Values
        );
        return new CombatImpactCorpusEntityBuild(entities, implicitPlayerEffects);
    }

    private static IReadOnlyDictionary<ECardAttributeType, int>? ReadAttributes(
        SimUpdateCard? update
    ) =>
        update == null
            ? null
            : update.Attributes.ToDictionary(item => item.Key, item => item.Value.Value);

    private static IReadOnlyDictionary<ECardAttributeType, int>? ReadAttributes(
        CombatSimCardUpdate? update
    ) =>
        update == null
            ? null
            : update.Attributes.ToDictionary(item => item.Key, item => item.Value.CurrentValue);

    private static IReadOnlyList<TCardAbility> ActiveAbilities(
        TCardBase template,
        ETier tier,
        EEnchantmentType? enchantment
    )
    {
        var abilities = template is IHasTierData tiered
            ? tiered.GetAbilityTemplatesByTier(tier).ToList()
            : template.Abilities.Values.ToList();
        if (
            enchantment.HasValue
            && template is TCardItem item
            && item.Enchantments?.TryGetValue(enchantment.Value, out var enchantmentTemplate)
                == true
        )
        {
            abilities.AddRange(enchantmentTemplate.Abilities.Values);
        }
        return abilities;
    }

    private static IReadOnlyDictionary<string, ECardAttributeType>? ReadAbilityAttributeTypes(
        TCardBase template,
        EEnchantmentType? enchantment
    )
    {
        var mappings = new Dictionary<string, ECardAttributeType>(StringComparer.Ordinal);
        var ambiguous = new HashSet<string>(StringComparer.Ordinal);
        AddAbilityAttributeTypes(template.Abilities.Values, mappings, ambiguous);
        if (
            enchantment.HasValue
            && template is TCardItem item
            && item.Enchantments?.TryGetValue(enchantment.Value, out var enchantmentTemplate)
                == true
        )
        {
            AddAbilityAttributeTypes(enchantmentTemplate.Abilities.Values, mappings, ambiguous);
        }
        return mappings.Count == 0 ? null : mappings;
    }

    private static void AddAbilityAttributeTypes(
        IEnumerable<TCardAbility> abilities,
        IDictionary<string, ECardAttributeType> mappings,
        ISet<string> ambiguous
    )
    {
        foreach (var ability in abilities)
        {
            if (
                ability.Action is not TActionCardModifyAttribute modifier
                || string.IsNullOrWhiteSpace(ability.Id)
                || ambiguous.Contains(ability.Id)
            )
                continue;
            if (!mappings.TryGetValue(ability.Id, out var existing))
            {
                mappings[ability.Id] = modifier.AttributeType;
                continue;
            }
            if (existing == modifier.AttributeType)
                continue;
            mappings.Remove(ability.Id);
            ambiguous.Add(ability.Id);
        }
    }

    private sealed record CorpusCardSeed(
        string InstanceId,
        Guid TemplateId,
        ECardType Type,
        ECombatantId Combatant,
        ETier? Tier,
        EEnchantmentType? Enchantment,
        IReadOnlyDictionary<ECardAttributeType, int>? Attributes
    );
}

internal sealed record CombatImpactCorpusEntityBuild(
    Dictionary<string, CombatImpactEntity> Entities,
    int ImplicitPlayerEffects
);

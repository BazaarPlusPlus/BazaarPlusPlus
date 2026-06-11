#nullable enable
using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Core.GameState;

namespace BazaarPlusPlus.GameInterop.Encounter;

/// <summary>Maps a pedestal encounter's stable <c>TemplateId</c> to its effect.
///
/// The client cannot read the real pedestal <c>Behavior</c> — it is
/// <c>[BazaarObfuscate]</c> and therefore excluded from deserialization, so every
/// pedestal's <c>Behavior</c> is the default <c>TPedestalBehaviorUpgrade</c> on the
/// client. This datamined catalog supplies what the client lacks. Base and
/// "(Level Up)" variants of the same encounter are distinct template ids that map to
/// the same effect (many-to-one). Source: bazaarplusplus-extractor card-asset export.</summary>
internal static class PedestalEnchantCatalog
{
    private static readonly Dictionary<Guid, EEnchantmentType> EnchantByTemplateId = new()
    {
        // Icy — Yetarian Tomb
        [G("04f879e5-94fe-42ae-aa7a-273edbd6d410")] = EEnchantmentType.Icy,
        [G("a2ec8028-6526-49eb-9dd7-41f8538165e9")] = EEnchantmentType.Icy,
        // Fiery — Burning Caldera
        [G("e36bfb52-5c63-4f59-815d-912af7917620")] = EEnchantmentType.Fiery,
        [G("c1e69f94-fa5a-4d96-bb3e-1a4a0c8b4b04")] = EEnchantmentType.Fiery,
        // Deadly — Bladeborn Badlands
        [G("48462c74-567d-4e1e-a441-f0fa62617e47")] = EEnchantmentType.Deadly,
        [G("81110f4a-ab1e-4a9e-af1f-a0a4ce44b629")] = EEnchantmentType.Deadly,
        // Heavy — Gravity's Altar / Languid Dunes
        [G("181c3701-c473-480a-b113-9befff77474b")] = EEnchantmentType.Heavy,
        [G("c3925fd2-d174-40ff-8d15-3d9c41b3615b")] = EEnchantmentType.Heavy,
        // Restorative — Plains of Edin / Tranquil Spring
        [G("22b1eb02-1133-4120-afe3-85f6e9a5aba2")] = EEnchantmentType.Restorative,
        [G("5f65bbe1-bf39-4fe7-8eeb-57b669df1ee6")] = EEnchantmentType.Restorative,
        // Shielded — Guardian's Gorge
        [G("cce12722-7c66-4030-8a2d-b7b8260a847d")] = EEnchantmentType.Shielded,
        [G("4df2e5f8-1a7f-4046-b374-8f1642b24751")] = EEnchantmentType.Shielded,
        // Toxic — Murkwood Bayou
        [G("099c544c-6833-407d-8d38-d4d1bb488f72")] = EEnchantmentType.Toxic,
        [G("47c256d3-6f3e-495b-b119-12739adda129")] = EEnchantmentType.Toxic,
        // Mossy — Mossy Ridge
        [G("ce8b4112-39eb-419a-b65e-56f5b7c6aebd")] = EEnchantmentType.Mossy,
        [G("320e7f80-334b-440c-8cc1-0b54e3141335")] = EEnchantmentType.Mossy,
        // Turbo — Sirocco Steppe
        [G("8e1fe6b3-d96d-4587-a09d-1bf656d9b364")] = EEnchantmentType.Turbo,
        [G("41e6d3fc-2795-4107-ba0a-29ecad00f37b")] = EEnchantmentType.Turbo,
        // Radiant — Celestial Conduit
        [G("3b9b8d2d-37bf-4742-8d9f-4c05c6d2a000")] = EEnchantmentType.Radiant,
        [G("9c1f2654-3077-4c4e-b38e-946aed17cbfb")] = EEnchantmentType.Radiant,
        // Shiny — Arcane Abyss
        [G("4874450b-1595-4bc2-b9e1-5b03e72a8cf6")] = EEnchantmentType.Shiny,
        [G("f1878b7e-cad6-4301-a5c1-bccf5d35da7b")] = EEnchantmentType.Shiny,
        // Obsidian — Sanguine Valley
        [G("943d17eb-e3d9-4349-97cd-b29d50be5197")] = EEnchantmentType.Obsidian,
        [G("8dead654-6cd3-4819-a0f1-b9d5a70b59f7")] = EEnchantmentType.Obsidian,
    };

    // "The Artist" — a low-tier encounter that applies a RANDOM enchant (the game picks
    // one; the player does not choose). The specific type is unknown ahead of time, so
    // kind is Enchant but no specific type is offered and callers show the item's full
    // enchant list rather than a single type.
    private static readonly HashSet<Guid> RandomEnchantTemplateIds = new()
    {
        G("49717d0f-71de-4291-8af6-d9c41ad3f438"),
        G("9b18ad79-901b-46ac-bec1-f447d41ad5ad"),
    };

    // Upgrade pedestals (B1&B2, Sterling, Forja, base + Level Up). Kept for completeness;
    // upgrade preview is hold-Shift only, so the policy does not auto-fire on these.
    private static readonly HashSet<Guid> UpgradeTemplateIds = new()
    {
        G("2e1e5a86-3ace-4f78-8b73-6641ca59e834"),
        G("0c410bf1-d560-4bc3-80db-ee06e882a447"),
        G("8b811b0f-15ba-4282-9732-6ddccb26da9f"),
        G("aae21c92-ae49-439a-812b-2c45c84e6b64"),
        G("693af45b-6d85-497f-ba32-b2f2d01b8aa3"),
        G("586ebd2d-a1c2-4274-875b-c3aeb29ab1ef"),
    };

    /// <summary>Classifies a pedestal by its template id. <paramref name="enchant"/> is
    /// the specific offered enchant for fixed enchant pedestals, or null for upgrade,
    /// random-enchant (The Artist), and unknown pedestals.</summary>
    public static ChoiceScreenPedestalKind Classify(Guid templateId, out EEnchantmentType? enchant)
    {
        if (EnchantByTemplateId.TryGetValue(templateId, out var specific))
        {
            enchant = specific;
            return ChoiceScreenPedestalKind.Enchant;
        }

        enchant = null;
        if (RandomEnchantTemplateIds.Contains(templateId))
            return ChoiceScreenPedestalKind.Enchant;
        if (UpgradeTemplateIds.Contains(templateId))
            return ChoiceScreenPedestalKind.Upgrade;
        return ChoiceScreenPedestalKind.None;
    }

    private static Guid G(string value) => new(value);
}

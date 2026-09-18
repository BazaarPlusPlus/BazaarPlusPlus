using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.GameInterop.Encounter;

// Real datamined template ids from PedestalEnchantCatalog.
var fiery = Guid.Parse("e36bfb52-5c63-4f59-815d-912af7917620");
var icy = Guid.Parse("04f879e5-94fe-42ae-aa7a-273edbd6d410");
var upgrade = Guid.Parse("2e1e5a86-3ace-4f78-8b73-6641ca59e834");
var randomArtist = Guid.Parse("49717d0f-71de-4291-8af6-d9c41ad3f438"); // The Artist (random enchant)
var unknown = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

// --- Catalog: stable templateId -> kind + specific enchant ---
var fieryKind = PedestalEnchantCatalog.Classify(fiery, out var fieryEnchant);
AssertEqual(ChoiceScreenPedestalKind.Enchant, fieryKind, "Fiery pedestal classifies as Enchant.");
AssertTrue(fieryEnchant == EEnchantmentType.Fiery, "Fiery pedestal yields the Fiery enchant.");

var upgradeKind = PedestalEnchantCatalog.Classify(upgrade, out var upgradeEnchant);
AssertEqual(
    ChoiceScreenPedestalKind.Upgrade,
    upgradeKind,
    "Upgrade pedestal classifies as Upgrade."
);
AssertTrue(!upgradeEnchant.HasValue, "Upgrade pedestal has no specific enchant.");

var artistKind = PedestalEnchantCatalog.Classify(randomArtist, out var artistEnchant);
AssertEqual(ChoiceScreenPedestalKind.Enchant, artistKind, "The Artist classifies as Enchant.");
AssertTrue(
    !artistEnchant.HasValue,
    "The Artist applies a random enchant, so no specific type is known ahead of time."
);

var unknownKind = PedestalEnchantCatalog.Classify(unknown, out _);
AssertEqual(ChoiceScreenPedestalKind.None, unknownKind, "Unknown template id classifies as None.");

// --- Resolver: instance id -> template id -> classify + aggregate ---
Func<string, Guid?> lookup = id =>
    id switch
    {
        "inst_fiery" => fiery,
        "inst_icy" => icy,
        "inst_upgrade" => upgrade,
        "inst_artist" => randomArtist,
        "inst_event" => Guid.Parse("11111111-1111-1111-1111-111111111111"), // not a pedestal
        _ => (Guid?)null,
    };

AssertEqual(
    ChoiceScreenPedestalKind.None,
    ChoiceScreenPedestalResolver.Resolve(null, lookup),
    "null SelectionSet resolves to None."
);

AssertEqual(
    ChoiceScreenPedestalKind.None,
    ChoiceScreenPedestalResolver.Resolve(Array.Empty<string>(), lookup),
    "Empty SelectionSet resolves to None."
);

AssertEqual(
    ChoiceScreenPedestalKind.None,
    ChoiceScreenPedestalResolver.Resolve(new[] { "inst_event" }, lookup),
    "A non-pedestal encounter resolves to None."
);

AssertEqual(
    ChoiceScreenPedestalKind.None,
    ChoiceScreenPedestalResolver.Resolve(new[] { "unmapped", "" }, lookup),
    "Unresolvable instance ids are skipped."
);

AssertEqual(
    ChoiceScreenPedestalKind.Upgrade,
    ChoiceScreenPedestalResolver.Resolve(new[] { "inst_upgrade" }, lookup),
    "Upgrade pedestal resolves to Upgrade."
);

var fieryResult = ChoiceScreenPedestalResolver.ResolveDetailed(new[] { "inst_fiery" }, lookup);
AssertEqual(
    ChoiceScreenPedestalKind.Enchant,
    fieryResult.Kind,
    "Single Fiery pedestal resolves to Enchant."
);
AssertTrue(
    fieryResult.EnchantmentTypeNames.Count == 1
        && fieryResult.EnchantmentTypeNames.Contains("Fiery"),
    "Fiery pedestal exposes exactly the Fiery enchant name."
);

var artistResult = ChoiceScreenPedestalResolver.ResolveDetailed(new[] { "inst_artist" }, lookup);
AssertEqual(ChoiceScreenPedestalKind.Enchant, artistResult.Kind, "The Artist resolves to Enchant.");
AssertTrue(
    artistResult.EnchantmentTypeNames.Count == 0,
    "The Artist's enchant is random, so it exposes no specific type and the preview shows the full list."
);

var multiResult = ChoiceScreenPedestalResolver.ResolveDetailed(
    new[] { "inst_fiery", "inst_icy", "inst_event" },
    lookup
);
AssertEqual(
    ChoiceScreenPedestalKind.Enchant,
    multiResult.Kind,
    "Multiple enchant pedestals resolve to Enchant."
);
AssertTrue(
    multiResult.EnchantmentTypeNames.Count == 2
        && multiResult.EnchantmentTypeNames.Contains("Fiery")
        && multiResult.EnchantmentTypeNames.Contains("Icy"),
    "Multiple offered enchant pedestals aggregate every offered enchant type."
);

var directTemplateResult = ChoiceScreenPedestalResolver.ResolveDetailedFromTemplateIds(
    new[] { fiery, icy, unknown, Guid.Empty }
);
AssertEqual(
    ChoiceScreenPedestalKind.Enchant,
    directTemplateResult.Kind,
    "Resolved template ids classify offered enchant pedestals without a selection lookup."
);
AssertTrue(
    directTemplateResult.EnchantmentTypeNames.Count == 2
        && directTemplateResult.EnchantmentTypeNames.Contains("Fiery")
        && directTemplateResult.EnchantmentTypeNames.Contains("Icy"),
    "Resolved template ids aggregate enchant types and skip unknown or empty template ids."
);

AssertEqual(
    ChoiceScreenPedestalKind.None,
    ChoiceScreenPedestalResolver.ResolveFromTemplateIds(Array.Empty<Guid>()),
    "Empty resolved template ids classify as None."
);

Console.WriteLine("ChoiceScreenPedestalResolver checks passed.");

static void AssertEqual<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{message} Expected: {expected}, Actual: {actual}");
}

static void AssertTrue(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

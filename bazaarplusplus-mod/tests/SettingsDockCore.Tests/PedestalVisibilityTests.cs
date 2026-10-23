#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Game.ItemEnchantPreview;
using Xunit;

namespace SettingsDockCore.Tests;

/// <summary>
/// The pedestal rule both hosts apply under <c>[EnchantPreview] Mode</c>: which pedestal a
/// template id is, what a screen of them offers, and when the preview shows for which enchantments.
/// </summary>
public sealed class PedestalVisibilityTests
{
    private static readonly Guid Deadly = new("48462c74-567d-4e1e-a441-f0fa62617e47");
    private static readonly Guid DeadlyLevelUp = new("81110f4a-ab1e-4a9e-af1f-a0a4ce44b629");
    private static readonly Guid Radiant = new("3b9b8d2d-37bf-4742-8d9f-4c05c6d2a000");
    private static readonly Guid TheArtist = new("49717d0f-71de-4291-8af6-d9c41ad3f438");
    private static readonly Guid Sterling = new("2e1e5a86-3ace-4f78-8b73-6641ca59e834");
    private static readonly Guid Unknown = new("00000000-0000-0000-0000-000000000401");

    [Fact]
    public void The_catalog_classifies_fixed_random_and_upgrade_pedestals()
    {
        Assert.Equal(
            ChoiceScreenPedestalKind.Enchant,
            PedestalEnchantCatalog.Classify(Deadly, out var name)
        );
        Assert.Equal("Deadly", name);
        Assert.Equal(
            ChoiceScreenPedestalKind.Enchant,
            PedestalEnchantCatalog.Classify(TheArtist, out var random)
        );
        Assert.Null(random);
        Assert.Equal(
            ChoiceScreenPedestalKind.Upgrade,
            PedestalEnchantCatalog.Classify(Sterling, out var upgrade)
        );
        Assert.Null(upgrade);
        Assert.Equal(
            ChoiceScreenPedestalKind.None,
            PedestalEnchantCatalog.Classify(Unknown, out var none)
        );
        Assert.Null(none);
    }

    [Fact]
    public void The_catalog_names_twelve_enchantments()
    {
        Assert.Equal(
            new[]
            {
                "Deadly",
                "Fiery",
                "Heavy",
                "Icy",
                "Mossy",
                "Obsidian",
                "Radiant",
                "Restorative",
                "Shielded",
                "Shiny",
                "Toxic",
                "Turbo",
            },
            PedestalEnchantCatalog.EnchantmentTypeNames.OrderBy(
                name => name,
                StringComparer.Ordinal
            )
        );
    }

    [Fact]
    public void A_screen_without_a_known_pedestal_offers_nothing()
    {
        Assert.Equal(
            ChoiceScreenPedestalKind.None,
            ChoiceScreenPedestalResolver.Resolve(null).Kind
        );
        Assert.Equal(
            ChoiceScreenPedestalKind.None,
            ChoiceScreenPedestalResolver.Resolve(Array.Empty<Guid>()).Kind
        );
        Assert.Equal(
            ChoiceScreenPedestalKind.None,
            ChoiceScreenPedestalResolver.Resolve(new[] { Unknown, Guid.Empty }).Kind
        );
    }

    [Fact]
    public void A_choice_screen_offers_every_enchantment_of_its_pedestals_once()
    {
        var snapshot = ChoiceScreenPedestalResolver.Resolve(
            new[] { Guid.Empty, Unknown, Radiant, Deadly, DeadlyLevelUp }
        );

        Assert.True(snapshot.IsEnchantChoice);
        Assert.Equal(new[] { "Radiant", "Deadly" }, snapshot.EnchantmentTypeNames);
    }

    [Fact]
    public void An_upgrade_or_random_pedestal_names_no_enchantment()
    {
        var upgrade = ChoiceScreenPedestalResolver.Resolve(new[] { Sterling });
        Assert.Equal(ChoiceScreenPedestalKind.Upgrade, upgrade.Kind);
        Assert.Empty(upgrade.EnchantmentTypeNames);

        var random = ChoiceScreenPedestalResolver.Resolve(new[] { TheArtist });
        Assert.True(random.IsEnchantChoice);
        Assert.Empty(random.EnchantmentTypeNames);
    }

    [Fact]
    public void Each_mode_shows_the_preview_where_it_says()
    {
        var enchant = ChoiceScreenPedestalResolver.Resolve(new[] { Deadly });
        var upgrade = ChoiceScreenPedestalResolver.Resolve(new[] { Sterling });
        const PreviewVisibilityMode auto = PreviewVisibilityMode.AutoOnPedestalChoice;

        Assert.True(EnchantPreviewVisibility.Shows(PreviewVisibilityMode.Always, null));
        Assert.True(EnchantPreviewVisibility.Shows(PreviewVisibilityMode.Always, upgrade));
        Assert.False(EnchantPreviewVisibility.Shows(PreviewVisibilityMode.Off, enchant));
        Assert.True(EnchantPreviewVisibility.Shows(auto, enchant));
        Assert.False(EnchantPreviewVisibility.Shows(auto, upgrade));
        Assert.False(EnchantPreviewVisibility.Shows(auto, null));
        Assert.False(EnchantPreviewVisibility.Shows(auto, ChoicePedestalSnapshot.Empty));
    }

    [Fact]
    public void Only_the_pedestal_mode_reads_the_pedestal_and_restricts_the_preview()
    {
        var enchant = ChoiceScreenPedestalResolver.Resolve(new[] { Deadly });
        var random = ChoiceScreenPedestalResolver.Resolve(new[] { TheArtist });
        const PreviewVisibilityMode auto = PreviewVisibilityMode.AutoOnPedestalChoice;

        Assert.True(EnchantPreviewVisibility.ReadsPedestal(auto));
        Assert.False(EnchantPreviewVisibility.ReadsPedestal(PreviewVisibilityMode.Always));
        Assert.False(EnchantPreviewVisibility.ReadsPedestal(PreviewVisibilityMode.Off));

        Assert.Equal(new[] { "Deadly" }, EnchantPreviewVisibility.Restriction(auto, enchant));
        Assert.Empty(EnchantPreviewVisibility.Restriction(auto, random)!);
        Assert.Null(EnchantPreviewVisibility.Restriction(PreviewVisibilityMode.Always, enchant));
        Assert.Null(EnchantPreviewVisibility.Restriction(auto, ChoicePedestalSnapshot.Empty));
    }
}

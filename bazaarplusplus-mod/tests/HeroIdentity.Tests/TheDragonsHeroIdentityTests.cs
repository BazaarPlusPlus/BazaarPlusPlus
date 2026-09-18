using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.GameInterop.Heroes;
using Xunit;

namespace HeroIdentity.Tests;

public sealed class TheDragonsHeroIdentityTests
{
    [Theory]
    [InlineData("Hero8")]
    [InlineData(" hero8 ")]
    [InlineData("TheDragons")]
    [InlineData(" THEDRAGONS ")]
    public void Both_aliases_resolve_to_the_same_current_enum(string alias)
    {
        Assert.True(TheDragonsHeroIdentity.TryResolve(alias, out var resolved));
        Assert.True(TheDragonsHeroIdentity.IsTheDragons(resolved));

        Assert.True(TheDragonsHeroIdentity.TryResolve("Hero8", out var legacy));
        Assert.True(TheDragonsHeroIdentity.TryResolve("TheDragons", out var canonical));
        Assert.Equal(legacy, canonical);
    }

    [Theory]
    [InlineData("Hero8")]
    [InlineData(" hero8 ")]
    [InlineData("TheDragons")]
    [InlineData(" THEDRAGONS ")]
    public void Both_aliases_canonicalize_to_TheDragons(string alias)
    {
        Assert.True(TheDragonsHeroIdentity.TryCanonicalize(alias, out var canonical));
        Assert.Equal("TheDragons", canonical);
    }

    [Fact]
    public void Alias_equivalence_is_symmetric()
    {
        Assert.True(TheDragonsHeroIdentity.AreEquivalent("Hero8", "TheDragons"));
        Assert.True(TheDragonsHeroIdentity.AreEquivalent("TheDragons", "Hero8"));
        Assert.True(TheDragonsHeroIdentity.AreEquivalent(" hero8 ", " THEDRAGONS "));
    }

    [Fact]
    public void Persistence_identity_writes_canonical_hero_ids()
    {
        Assert.True(TheDragonsHeroIdentity.TryResolve("Hero8", out var dragons));

        Assert.Equal("TheDragons", TheDragonsHeroIdentity.ToCanonicalId(dragons));
        Assert.Equal("Vanessa", TheDragonsHeroIdentity.ToCanonicalId(EHero.Vanessa));
    }

    [Fact]
    public void Write_path_stores_the_canonical_id_under_either_runtime_enum_name()
    {
        Assert.True(TheDragonsHeroIdentity.TryResolve("Hero8", out var dragons));

        // A build that only exposes the legacy member name must still write "TheDragons".
        Assert.Equal("TheDragons", TheDragonsHeroIdentity.ToCanonicalId(dragons, _ => "Hero8"));
        Assert.Equal(
            "TheDragons",
            TheDragonsHeroIdentity.ToCanonicalId(dragons, _ => "TheDragons")
        );
        Assert.Equal(
            "Vanessa",
            TheDragonsHeroIdentity.ToCanonicalId(EHero.Vanessa, _ => "Vanessa")
        );
        Assert.Throws<ArgumentNullException>(() =>
            TheDragonsHeroIdentity.ToCanonicalId(dragons, null!)
        );
    }

    [Fact]
    public void Legacy_only_runtime_shape_resolves_both_aliases()
    {
        Assert.True(TheDragonsHeroIdentity.TryResolve("Hero8", out var currentDragons));
        var requestedNames = new List<string>();
        Func<string, EHero?> legacyOnly = name =>
        {
            requestedNames.Add(name);
            return string.Equals(name, "Hero8", StringComparison.Ordinal) ? currentDragons : null;
        };

        Assert.True(
            TheDragonsHeroIdentity.TryResolve("Hero8", legacyOnly, out var fromLegacyAlias)
        );
        Assert.True(
            TheDragonsHeroIdentity.TryResolve("TheDragons", legacyOnly, out var fromCanonicalAlias)
        );
        Assert.Equal(currentDragons, fromLegacyAlias);
        Assert.Equal(currentDragons, fromCanonicalAlias);
        Assert.Equal(["TheDragons", "Hero8", "TheDragons", "Hero8"], requestedNames);
    }

    [Fact]
    public void Canonical_only_runtime_shape_resolves_both_aliases()
    {
        Assert.True(TheDragonsHeroIdentity.TryResolve("Hero8", out var currentDragons));
        var requestedNames = new List<string>();
        Func<string, EHero?> canonicalOnly = name =>
        {
            requestedNames.Add(name);
            return string.Equals(name, "TheDragons", StringComparison.Ordinal)
                ? currentDragons
                : null;
        };

        Assert.True(
            TheDragonsHeroIdentity.TryResolve("Hero8", canonicalOnly, out var fromLegacyAlias)
        );
        Assert.True(
            TheDragonsHeroIdentity.TryResolve(
                "TheDragons",
                canonicalOnly,
                out var fromCanonicalAlias
            )
        );
        Assert.Equal(currentDragons, fromLegacyAlias);
        Assert.Equal(currentDragons, fromCanonicalAlias);
        Assert.Equal(["TheDragons", "TheDragons"], requestedNames);
    }

    [Fact]
    public void Neither_runtime_shape_is_explicitly_unavailable()
    {
        Func<string, EHero?> unavailable = _ => null;

        Assert.False(TheDragonsHeroIdentity.TryResolve("Hero8", unavailable, out _));
        Assert.False(TheDragonsHeroIdentity.TryResolve("TheDragons", unavailable, out _));
    }

    [Fact]
    public void Non_dragons_and_invalid_inputs_keep_normal_enum_identity()
    {
        Assert.True(TheDragonsHeroIdentity.TryResolve("Vanessa", out var vanessa));
        Assert.Equal(EHero.Vanessa, vanessa);
        Assert.True(TheDragonsHeroIdentity.TryCanonicalize(" Vanessa ", out var canonical));
        Assert.Equal("Vanessa", canonical);
        Assert.True(TheDragonsHeroIdentity.AreEquivalent("Vanessa", " Vanessa "));

        Assert.False(TheDragonsHeroIdentity.TryResolve("vanessa", out _));
        Assert.False(TheDragonsHeroIdentity.TryResolve(null, out _));
        Assert.False(TheDragonsHeroIdentity.TryResolve(" ", out _));
        Assert.False(TheDragonsHeroIdentity.TryResolve("UnknownHero", out _));
        Assert.False(TheDragonsHeroIdentity.TryResolve("The Dragons", out _));
        Assert.False(TheDragonsHeroIdentity.TryCanonicalize("UnknownHero", out _));
        Assert.False(TheDragonsHeroIdentity.AreEquivalent("UnknownHero", "UnknownHero"));
    }

    [Fact]
    public void Display_name_prefers_the_native_localized_value()
    {
        Assert.True(TheDragonsHeroIdentity.TryResolve("TheDragons", out var dragons));

        var displayName = TheDragonsHeroIdentity.ResolveDisplayName(
            dragons,
            _ => "Localized Dragons"
        );

        Assert.Equal("Localized Dragons", displayName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Hero8")]
    [InlineData(" hero8 ")]
    [InlineData("TheDragons")]
    public void Blank_or_alias_placeholder_native_name_falls_back_safely(string? nativeName)
    {
        Assert.True(TheDragonsHeroIdentity.TryResolve("TheDragons", out var dragons));

        Assert.Equal(
            "The Dragons",
            TheDragonsHeroIdentity.ResolveDisplayName(dragons, _ => nativeName)
        );
    }

    [Fact]
    public void Native_localization_exception_falls_back_without_escaping()
    {
        Assert.True(TheDragonsHeroIdentity.TryResolve("TheDragons", out var dragons));

        var exception = Record.Exception(() =>
            TheDragonsHeroIdentity.ResolveDisplayName(
                dragons,
                _ => throw new InvalidOperationException("localization unavailable")
            )
        );

        Assert.Null(exception);
        Assert.Equal(
            "The Dragons",
            TheDragonsHeroIdentity.ResolveDisplayName(
                dragons,
                _ => throw new InvalidOperationException("localization unavailable")
            )
        );
    }

    [Fact]
    public void Stable_alias_placeholder_is_read_once_then_served_from_fallback_cache()
    {
        Assert.True(TheDragonsHeroIdentity.TryResolve("TheDragons", out var dragons));
        var nativeCalls = 0;
        var resolver = new TheDragonsHeroIdentity.DisplayNameResolver(_ =>
        {
            nativeCalls++;
            return "Hero8";
        });

        Assert.Equal("The Dragons", resolver.Resolve(dragons));
        Assert.Equal("The Dragons", resolver.Resolve(dragons));
        Assert.Equal(1, nativeCalls);
    }

    [Fact]
    public void Blank_native_name_is_not_cached_and_can_recover()
    {
        Assert.True(TheDragonsHeroIdentity.TryResolve("TheDragons", out var dragons));
        var nativeCalls = 0;
        var resolver = new TheDragonsHeroIdentity.DisplayNameResolver(_ =>
            ++nativeCalls == 1 ? null : "Recovered Dragons"
        );

        Assert.Equal("The Dragons", resolver.Resolve(dragons));
        Assert.Equal("Recovered Dragons", resolver.Resolve(dragons));
        Assert.Equal(2, nativeCalls);
    }

    [Fact]
    public void Native_exception_is_not_cached_and_can_recover()
    {
        Assert.True(TheDragonsHeroIdentity.TryResolve("TheDragons", out var dragons));
        var nativeCalls = 0;
        var resolver = new TheDragonsHeroIdentity.DisplayNameResolver(_ =>
        {
            nativeCalls++;
            return nativeCalls == 1
                ? throw new InvalidOperationException("localization unavailable")
                : "Recovered Dragons";
        });

        Assert.Equal("The Dragons", resolver.Resolve(dragons));
        Assert.Equal("Recovered Dragons", resolver.Resolve(dragons));
        Assert.Equal(2, nativeCalls);
    }

    [Fact]
    public void Non_dragons_native_display_remains_unchanged()
    {
        Assert.Equal(
            "Vanessa localized",
            TheDragonsHeroIdentity.ResolveDisplayName(EHero.Vanessa, _ => "Vanessa localized")
        );
    }
}

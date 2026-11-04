using BazaarPlusPlus.Android.GameInterop;
using Il2CppBazaarGameShared.Domain.Core.Types;
using Xunit;

namespace AndroidHost.Tests;

/// <summary>
/// Saved Card Values as <see cref="AndroidNativeCardSetUp.Values"/> parses them over the interop
/// attribute enum. The IL2CPP writes that follow (the instance, the client card after the set-up,
/// the text card) build <c>Il2CppSystem</c> dictionaries, which ADR-0016 keeps out of this suite,
/// so a device checks those.
/// </summary>
public sealed class AndroidNativeCardSetUpTests
{
    [Fact]
    public void A_saved_value_maps_to_the_attribute_of_its_member_name()
    {
        var values = AndroidNativeCardSetUp.Values(
            new Dictionary<string, int> { ["DamageAmount"] = 55, ["Heated"] = 1 }
        );
        Assert.Equal(2, values.Count);
        Assert.Equal(55, values[ECardAttributeType.DamageAmount]);
        Assert.Equal(1, values[ECardAttributeType.Heated]);
    }

    [Fact]
    public void An_unknown_or_differently_cased_name_is_dropped()
    {
        var values = AndroidNativeCardSetUp.Values(
            new Dictionary<string, int> { ["NotAGameAttribute"] = 9, ["damageamount"] = 7 }
        );
        Assert.Empty(values);
    }

    [Fact]
    public void No_saved_values_is_an_empty_set()
    {
        Assert.Empty(AndroidNativeCardSetUp.Values(null));
    }
}

using BazaarPlusPlus.Android.GameInterop;
using Il2CppBazaarGameShared.Domain.Core.Types;
using Xunit;

namespace AndroidHost.Tests;

// Leave writes to device checks because they construct IL2CPP dictionaries.
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

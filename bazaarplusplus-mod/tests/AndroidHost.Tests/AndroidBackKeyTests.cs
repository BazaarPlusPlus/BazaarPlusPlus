#nullable enable
using BazaarPlusPlus.Android.Ui;
using Xunit;

namespace AndroidHost.Tests;

/// <summary>
/// The touch UI registers its back-key handlers each time it mounts, and a rebuilt canvas mounts
/// it again: a second registration of a name replaces the first in place, so one press never
/// asks the same panel twice and the top-most order stays the order of first registration.
/// The handler list is process-wide, so each test uses its own names.
/// </summary>
public sealed class AndroidBackKeyTests
{
    [Fact]
    public void Registering_a_name_again_keeps_one_entry_in_its_place()
    {
        var first = "test.back_key." + Guid.NewGuid().ToString("N");
        var second = "test.back_key." + Guid.NewGuid().ToString("N");
        AndroidBackKey.Register(first, () => false);
        AndroidBackKey.Register(second, () => false);

        AndroidBackKey.Register(first, () => true);

        var mine = AndroidBackKey.Registered.Where(name => name == first || name == second);
        Assert.Equal(new[] { first, second }, mine);
    }

    [Fact]
    public void A_null_handler_is_refused()
    {
        Assert.Throws<ArgumentNullException>(() =>
            AndroidBackKey.Register("test.back_key.null", null!)
        );
        Assert.DoesNotContain("test.back_key.null", AndroidBackKey.Registered);
    }
}

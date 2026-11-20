using System.Runtime.CompilerServices;
using BazaarPlusPlus.Android.GameInterop;
using Xunit;

namespace AndroidHost.Tests;

// An IL2CPP bool proxy can supply any nonzero byte for true.
public sealed class AndroidGameIdsTests
{
    [Theory]
    [InlineData((byte)0, (byte)0)]
    [InlineData((byte)1, (byte)1)]
    [InlineData((byte)2, (byte)1)]
    [InlineData((byte)0xFF, (byte)1)]
    public void Canonical_stores_a_bool_as_zero_or_one(byte stored, byte expected)
    {
        var value = Unsafe.As<byte, bool>(ref stored);

        var canonical = AndroidGameIds.Canonical(value);

        Assert.Equal(expected, Unsafe.As<bool, byte>(ref canonical));
    }
}

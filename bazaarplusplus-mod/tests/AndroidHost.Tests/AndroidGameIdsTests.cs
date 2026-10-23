using System.Runtime.CompilerServices;
using BazaarPlusPlus.Android.GameInterop;
using Xunit;

namespace AndroidHost.Tests;

/// <summary>
/// The compiled host's bool canonicalization: a bool an IL2CPP struct proxy reads can hold any
/// nonzero byte, and the upgrade preview passes it back into the game only as 0 or 1.
/// </summary>
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

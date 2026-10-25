using BazaarPlusPlus.Android.GameInterop;
using Il2CppBazaarGameShared.Domain.Core.Types;
using Xunit;

namespace AndroidHost.Tests;

/// <summary>
/// A captured PvP card's facets, as <see cref="AndroidCardFacets"/> maps the interop values the
/// battle reads take: each member keeps its name, a type the facets lack fails the read, and an
/// unnamed size, section or socket is none, as on the desktop.
/// </summary>
public sealed class AndroidCardFacetsCapturedTests
{
    [Fact]
    public void Every_interop_member_is_captured_by_its_name()
    {
        foreach (var type in Enum.GetValues<ECardType>())
            Assert.Equal(type.ToString(), AndroidCardFacets.CapturedType(type).ToString());
        foreach (var size in Enum.GetValues<ECardSize>())
            Assert.Equal(size.ToString(), AndroidCardFacets.CapturedSize(size).ToString());
        foreach (var section in Enum.GetValues<EInventorySection>())
            Assert.Equal(section.ToString(), AndroidCardFacets.CapturedSection(section).ToString());
        foreach (var socket in Enum.GetValues<EContainerSocketId>())
            Assert.Equal(socket.ToString(), AndroidCardFacets.CapturedSocket(socket).ToString());
    }

    [Fact]
    public void A_type_with_no_facet_fails_the_read()
    {
        Assert.Throws<InvalidDataException>(() => AndroidCardFacets.CapturedType((ECardType)99));
    }

    [Fact]
    public void An_unnamed_or_absent_size_section_or_socket_is_none()
    {
        Assert.Null(AndroidCardFacets.CapturedSize((ECardSize)0));
        Assert.Null(AndroidCardFacets.CapturedSize(null));
        Assert.Null(AndroidCardFacets.CapturedSection((EInventorySection)99));
        Assert.Null(AndroidCardFacets.CapturedSection(null));
        Assert.Null(AndroidCardFacets.CapturedSocket((EContainerSocketId)99));
        Assert.Null(AndroidCardFacets.CapturedSocket(null));
    }
}

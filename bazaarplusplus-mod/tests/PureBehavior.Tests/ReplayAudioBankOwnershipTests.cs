using BazaarPlusPlus.Game.CombatReplay.Warmup;
using Xunit;

namespace PureBehavior.Tests;

public sealed class ReplayAudioBankOwnershipTests
{
    [Fact]
    public void OpponentBankTheWarmupLoaded_IsReleasedOnceAtExit()
    {
        var ownership = new ReplayAudioBankOwnership();

        Assert.True(ownership.RecordWarmed("Opp", "Opp.assets", false, false, false));

        Assert.Equal([new ReplayAudioBank("Opp", "Opp.assets")], ownership.Drain());
        Assert.Empty(ownership.Drain());
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void BankLoadedBeforeTheWarmup_StaysWithItsOwner(bool metadataLoaded, bool assetLoaded)
    {
        var ownership = new ReplayAudioBankOwnership();

        Assert.False(
            ownership.RecordWarmed("Opp", "Opp.assets", metadataLoaded, assetLoaded, false)
        );

        Assert.Empty(ownership.Drain());
    }

    [Fact]
    public void PlayerBoardBank_IsLeftForTheGamesRunInterruptedUnload()
    {
        var ownership = new ReplayAudioBankOwnership();

        Assert.False(ownership.RecordWarmed("Mine", "Mine.assets", false, false, true));

        Assert.Empty(ownership.Drain());
    }

    [Theory]
    [InlineData("Opp", "Opp.assets")]
    [InlineData("Opp", "Other.assets")]
    [InlineData("Other", "Opp.assets")]
    public void BankSharingEitherHalfWithAnOwnedBank_IsReleasedOnce(
        string metadataBank,
        string assetBank
    )
    {
        var ownership = new ReplayAudioBankOwnership();

        Assert.True(ownership.RecordWarmed("Opp", "Opp.assets", false, false, false));
        Assert.False(ownership.RecordWarmed(metadataBank, assetBank, false, false, false));

        Assert.Equal([new ReplayAudioBank("Opp", "Opp.assets")], ownership.Drain());
    }

    [Fact]
    public void DistinctBanks_AreAllReleased()
    {
        var ownership = new ReplayAudioBankOwnership();

        ownership.RecordWarmed("Opp", "Opp.assets", false, false, false);
        ownership.RecordWarmed("Other", "Other.assets", false, false, false);

        Assert.Equal(
            [
                new ReplayAudioBank("Opp", "Opp.assets"),
                new ReplayAudioBank("Other", "Other.assets"),
            ],
            ownership.Drain()
        );
    }
}

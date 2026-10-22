#nullable enable
using BazaarPlusPlus.Infrastructure.SelfUpdate;
using Xunit;

namespace SelfUpdate.Tests;

public sealed class SelfUpdatePolicyTests
{
    private static readonly string Digest = new('d', 64);

    private static ManagedPayloadRelease Release(
        string version = "3.3.0",
        int historyVersion = 1,
        string? digest = null
    ) =>
        new(
            version,
            new Uri($"https://cdn.example/{version}.zip"),
            10,
            new string('a', 64),
            "sig",
            historyVersion,
            digest ?? Digest
        );

    private static RunningManagedPayload Running(
        string version = "3.2.9.prod",
        int historyVersion = 1,
        string? digest = "d"
    ) => new(version, historyVersion, digest == "d" ? Digest : digest);

    [Theory]
    [InlineData("3.3.0.preview")]
    [InlineData("3.3.0.prod")]
    [InlineData("3.3.0.t20260604.120000.dev")]
    [InlineData("4.0.0.preview")]
    [InlineData("not-a-version")]
    public void A_copy_at_or_past_the_release_or_unreadable_is_up_to_date(string current)
    {
        Assert.Equal(
            SelfUpdateDecision.UpToDate,
            SelfUpdatePolicy.Decide(Running(current), "3.3.0", Release(), staged: false)
        );
    }

    [Fact]
    public void An_unreadable_published_version_is_up_to_date()
    {
        Assert.Equal(
            SelfUpdateDecision.UpToDate,
            SelfUpdatePolicy.Decide(Running(), "not-a-version", Release(), staged: false)
        );
    }

    [Fact]
    public void A_matching_release_is_staged_or_reported_staged()
    {
        Assert.Equal(
            SelfUpdateDecision.Stage,
            SelfUpdatePolicy.Decide(Running(), "3.3.0", Release(), staged: false)
        );
        Assert.Equal(
            SelfUpdateDecision.Staged,
            SelfUpdatePolicy.Decide(Running(), "3.3.0", Release(), staged: true)
        );
    }

    [Fact]
    public void Anything_the_copy_cannot_take_by_itself_falls_back_to_a_manual_update()
    {
        // No Managed Payload published for this platform.
        Assert.Equal(
            SelfUpdateDecision.ManualUpdate,
            SelfUpdatePolicy.Decide(Running(), "3.3.0", null, staged: false)
        );
        // A history database schema change needs the installer, which must read the new schema.
        Assert.Equal(
            SelfUpdateDecision.ManualUpdate,
            SelfUpdatePolicy.Decide(Running(), "3.3.0", Release(historyVersion: 2), false)
        );
        // A native, bootstrap, SQLite or Updater change.
        Assert.Equal(
            SelfUpdateDecision.ManualUpdate,
            SelfUpdatePolicy.Decide(Running(), "3.3.0", Release(digest: new string('f', 64)), false)
        );
        // A development build has no installed manifest, so no digest of its own.
        Assert.Equal(
            SelfUpdateDecision.ManualUpdate,
            SelfUpdatePolicy.Decide(Running(digest: null), "3.3.0", Release(), false)
        );
        // A record of another version than the manifest names.
        Assert.Equal(
            SelfUpdateDecision.ManualUpdate,
            SelfUpdatePolicy.Decide(Running(), "3.3.0", Release(version: "3.2.9"), false)
        );
    }

    [Fact]
    public void The_comparison_is_against_the_running_copy_not_the_previous_release()
    {
        // A copy on 3.1.0 skipping to 3.3.0 compares its own schema, whatever 3.2.0 changed.
        var running = Running("3.1.0.prod", historyVersion: 1);
        Assert.Equal(
            SelfUpdateDecision.ManualUpdate,
            SelfUpdatePolicy.Decide(running, "3.3.0", Release(historyVersion: 2), false)
        );
        Assert.Equal(
            SelfUpdateDecision.Stage,
            SelfUpdatePolicy.Decide(running, "3.3.0", Release(historyVersion: 1), false)
        );
    }
}

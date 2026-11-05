using System.Text;
using BazaarPlusPlus.SteamSnapshot;
using SteamKit2;
using Xunit;

namespace SteamSnapshot.Tests;

public sealed class SteamSnapshotTests
{
    private const string MacData = "TheBazaar.app/Contents/Resources/Data";

    [Fact]
    public void Select_keeps_the_version_file_and_the_managed_directory_beside_it()
    {
        var files = SnapshotFiles.Select([
            $"{MacData}/globalgamemanagers",
            $"{MacData}/globalgamemanagers.assets",
            $"{MacData}/Managed/Assembly-CSharp.dll",
            $"{MacData}/Managed/Sub/Other.dll",
            $"{MacData}/StreamingAssets/Managed/Decoy.dll",
            "TheBazaar.app/Contents/MacOS/TheBazaar",
        ]);

        Assert.Equal(
            [
                new SnapshotFile($"{MacData}/globalgamemanagers", "Data/globalgamemanagers"),
                new SnapshotFile(
                    $"{MacData}/Managed/Assembly-CSharp.dll",
                    "Data/Managed/Assembly-CSharp.dll"
                ),
                new SnapshotFile($"{MacData}/Managed/Sub/Other.dll", "Data/Managed/Sub/Other.dll"),
            ],
            files
        );
    }

    [Fact]
    public void Select_reads_a_backslash_listing_and_keeps_the_depot_name()
    {
        var files = SnapshotFiles.Select([
            @"TheBazaar_Data\globalgamemanagers",
            @"TheBazaar_Data\Managed\Assembly-CSharp.dll",
        ]);

        Assert.Contains(
            new SnapshotFile(
                @"TheBazaar_Data\Managed\Assembly-CSharp.dll",
                "Data/Managed/Assembly-CSharp.dll"
            ),
            files
        );
    }

    [Theory]
    [InlineData("a/globalgamemanagers", "b/globalgamemanagers", "a/Managed/Assembly-CSharp.dll")]
    [InlineData("a/Managed/Assembly-CSharp.dll", "a/other", "a/more")]
    [InlineData("a/globalgamemanagers", "a/Managed/Other.dll", "a/more")]
    [InlineData("a/globalgamemanagers", "a/../Managed/Assembly-CSharp.dll", "a/more")]
    [InlineData("a/globalgamemanagers", "C:/Managed/Assembly-CSharp.dll", "a/more")]
    public void Select_refuses_a_listing_it_cannot_trust(string first, string second, string third)
    {
        Assert.Throws<InvalidDataException>(() => SnapshotFiles.Select([first, second, third]));
    }

    [Fact]
    public void Read_returns_the_branch_build_and_depot_manifests()
    {
        var state = BranchState.Read(Product()["depots"], "public", [1617401, 1617402]);

        Assert.Equal("26443971", state.Buildid);
        Assert.Equal(8272434544826088728UL, state.Manifests[1617401]);
        Assert.Equal(3065482850319556123UL, state.Manifests[1617402]);
    }

    [Fact]
    public void Read_refuses_a_branch_or_depot_that_Steam_does_not_show()
    {
        Assert.Throws<InvalidDataException>(() =>
            BranchState.Read(Product()["depots"], "staging", [1617401])
        );
        Assert.Throws<InvalidDataException>(() =>
            BranchState.Read(Product()["depots"], "public", [1617403])
        );
    }

    [Fact]
    public void A_saved_login_round_trips()
    {
        var login = new SteamLogin("account", "refresh");

        Assert.Equal(login, SteamLogin.Parse(login.ToJson()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("secret-value-not-json")]
    [InlineData("{\"account\":\"a\"}")]
    public void A_bad_login_fails_without_repeating_its_value(string? value)
    {
        var error = Assert.Throws<InvalidOperationException>(() => SteamLogin.Parse(value));

        Assert.Contains("just mod::steam-login", error.Message);
        Assert.DoesNotContain("secret-value", error.Message);
    }

    [Fact]
    public void An_access_code_is_found_by_branch()
    {
        var passwords = BetaPasswords.Parse("{\"staging\":\"code\"}");

        Assert.Equal("code", passwords.For("staging"));
        var missing = Assert.Throws<InvalidOperationException>(() =>
            passwords.For("public_test_realm")
        );
        Assert.Contains("public_test_realm", missing.Message);
        Assert.DoesNotContain("code\"", missing.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("secret-value-not-json")]
    public void Bad_access_codes_fail_without_repeating_their_value(string? value)
    {
        var error = Assert.Throws<InvalidOperationException>(() => BetaPasswords.Parse(value));

        Assert.Contains(BetaPasswords.EnvironmentVariable, error.Message);
        Assert.DoesNotContain("secret-value", error.Message);
    }

    private static KeyValue Product()
    {
        const string text = """
            "appinfo"
            {
                "depots"
                {
                    "branches"
                    {
                        "public" { "buildid" "26443971" }
                    }
                    "1617401" { "manifests" { "public" { "gid" "8272434544826088728" } } }
                    "1617402" { "manifests" { "public" { "gid" "3065482850319556123" } } }
                }
            }
            """;
        var product = new KeyValue();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        Assert.True(product.ReadAsText(stream));
        return product;
    }
}

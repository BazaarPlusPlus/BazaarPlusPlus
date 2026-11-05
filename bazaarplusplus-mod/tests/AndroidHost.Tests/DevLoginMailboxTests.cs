using BazaarPlusPlus.Android.Host;
using BazaarPlusPlus.TestSupport;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AndroidHost.Tests;

public sealed class DevLoginMailboxTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "bpp-login-" + Guid.NewGuid().ToString("N")
    );
    private const string Id = "1234567890abcdef1234567890abcdef";

    public DevLoginMailboxTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, true);

    private void Write(string json) =>
        File.WriteAllText(Path.Combine(_directory, "request.json"), json);

    [Fact]
    public void Request_is_consumed_once_and_receipt_never_contains_credentials()
    {
        Write(
            new JObject
            {
                ["id"] = Id,
                ["mode"] = "login",
                ["expiresAt"] = 123L,
                ["email"] = "test@example.invalid",
                ["password"] = "密码 $() %s",
            }.ToString()
        );
        var mailbox = new DevLoginMailbox(_directory);
        var request = Assert.IsType<DevLoginMailbox.Request>(mailbox.Take());
        Assert.Null(mailbox.Take());
        Assert.Equal("密码 $() %s", request.Password);
        mailbox.Report(request, 42, "13084", "signed_in", "PlayNowValidated", true);
        Assert.Null(request.Email);
        Assert.Null(request.Password);
        var text = TestInputs.Scratch(Path.Combine(_directory, "result.json"));
        Assert.DoesNotContain("example.invalid", text);
        Assert.DoesNotContain("密码", text);
        var result = JObject.Parse(text);
        Assert.Equal(Id, (string?)result["id"]);
        Assert.Equal(42, (int?)result["pid"]);
        Assert.Equal("signed_in", (string?)result["status"]);
        Assert.False(File.Exists(Path.Combine(_directory, "result.json.tmp")));
    }

    [Theory]
    [InlineData("{\"password\":\"secret\"")]
    [InlineData("{\"id\":\"secret\",\"mode\":\"login\"}")]
    public void Malformed_input_is_deleted_without_quoting_it_in_an_exception(string json)
    {
        Write(json);
        var error = Assert.Throws<InvalidDataException>(() =>
            new DevLoginMailbox(_directory).Take()
        );
        Assert.DoesNotContain("secret", error.ToString());
        Assert.False(File.Exists(Path.Combine(_directory, "request.json")));
    }

    [Fact]
    public void Oversized_input_is_deleted_before_parsing()
    {
        Write(new string('x', 16385));
        Assert.Throws<InvalidDataException>(() => new DevLoginMailbox(_directory).Take());
        Assert.False(File.Exists(Path.Combine(_directory, "request.json")));
    }

    [Theory]
    [InlineData("resume")]
    [InlineData("manual")]
    public void Observing_modes_discard_any_supplied_credentials(string mode)
    {
        Write(
            new JObject
            {
                ["id"] = Id,
                ["mode"] = mode,
                ["email"] = "private",
                ["password"] = "secret",
            }.ToString()
        );
        var request = Assert.IsType<DevLoginMailbox.Request>(
            new DevLoginMailbox(_directory).Take()
        );
        Assert.Null(request.Email);
        Assert.Null(request.Password);
    }

    [Fact]
    public void Native_async_login_trace_waits_for_authentication_and_loaded_profile()
    {
        Assert.Null(DevLoginOutcome.Read("LogIn", false, true, true, true));
        Assert.Null(DevLoginOutcome.Read("LinkAccount", false, true, false, true));
        Assert.Null(DevLoginOutcome.Read("PlayNowValidated", false, true, false, true));
        Assert.Equal(
            "signed_in",
            DevLoginOutcome.Read("PlayNowValidated", true, true, false, true)
        );
        Assert.Equal(
            "signed_in",
            DevLoginOutcome.Read("PlayNowValidated", true, false, false, false)
        );
        Assert.Equal("login_failed", DevLoginOutcome.Read("LogIn", false, true, false, true));
        Assert.Null(DevLoginOutcome.Read("LogIn", false, true, false, false));
        Assert.Equal(
            "action_required",
            DevLoginOutcome.Read("VerifyAccount", false, true, false, false)
        );
    }
}

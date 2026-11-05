using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BazaarPlusPlus.Android.Host;

// Private, one-use development input. No game or Unity types: file lifecycle and redaction
// are verified without loading IL2CPP. The debug App owns the directory, not the Data Root.
internal sealed class DevLoginMailbox(string directory)
{
    internal sealed class Request
    {
        public required string Id { get; init; }
        public required string Mode { get; init; }
        public required long ExpiresAt { get; init; }
        public string? Email { get; set; }
        public string? Password { get; set; }

        public void ForgetCredentials() => Email = Password = null;
    }

    public Request? Take()
    {
        var path = Path.Combine(directory, "request.json");
        if (!File.Exists(path))
            return null;
        string json;
        try
        {
            if (new FileInfo(path).Length > 16384)
                throw new InvalidDataException("Development login request is too large.");
            json = File.ReadAllText(path);
        }
        finally
        {
            // Deletion must succeed before parsing or handing credentials to the game.
            File.Delete(path);
        }
        try
        {
            var value = JObject.Parse(json);
            var id = (string?)value["id"];
            var mode = (string?)value["mode"];
            if (
                !Guid.TryParseExact(id, "N", out _) || mode is not ("login" or "resume" or "manual")
            )
                throw new InvalidDataException();
            var request = new Request
            {
                Id = id!,
                Mode = mode!,
                ExpiresAt = (long?)value["expiresAt"] ?? 0,
                Email = (string?)value["email"],
                Password = (string?)value["password"],
            };
            if (
                mode == "login"
                && (
                    string.IsNullOrWhiteSpace(request.Email)
                    || string.IsNullOrEmpty(request.Password)
                )
            )
                throw new InvalidDataException();
            if (mode != "login")
                request.ForgetCredentials();
            return request;
        }
        catch
        {
            // A JSON parser's exception can quote input; never propagate it or its inner exception.
            throw new InvalidDataException("Invalid development login request.");
        }
    }

    public void Report(
        Request request,
        int pid,
        string version,
        string status,
        string phase,
        bool submitted
    )
    {
        request.ForgetCredentials();
        var result = new JObject
        {
            ["id"] = request.Id,
            ["pid"] = pid,
            ["gameVersion"] = version,
            ["status"] = status,
            ["phase"] = phase,
            ["submitted"] = submitted,
        };
        var output = Path.Combine(directory, "result.json");
        try
        {
            File.WriteAllText(output + ".tmp", result.ToString(Formatting.None));
            File.Move(output + ".tmp", output, true);
        }
        finally
        {
            File.Delete(output + ".tmp");
        }
    }
}

// This order reflects the native Android flow: a notice or a LogIn frame while the async
// submission is pending is not a terminal result. Authentication also waits for profile data.
internal static class DevLoginOutcome
{
    public static string? Read(
        string phase,
        bool profileReady,
        bool submitted,
        bool pending,
        bool errorNotice
    )
    {
        if (phase == "PlayNowValidated")
            return profileReady ? "signed_in" : null;
        if (phase is "VerifyAccount" or "AccessDenied")
            return "action_required";
        return submitted && phase == "LogIn" && !pending && errorNotice ? "login_failed" : null;
    }
}

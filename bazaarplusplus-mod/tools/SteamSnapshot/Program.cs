using System.Text.Json;
using BazaarPlusPlus.SteamSnapshot;

// stdout carries one JSON line for release/game-libs.ts; everything else goes to stderr.
const string Usage = """
    BazaarPlusPlus.SteamSnapshot, driven by bazaarplusplus-mod/scripts/game.sh:
      login --out <file>     Sign in at the terminal and save the login with owner-only access
      state --app <id> --branch <name> --depots <id,id>
                             Print the branch's buildid, update time and each depot's manifest
      download --app <id> --branch <name> --depot <id> --out <dir>
                             Write <dir>/Data/globalgamemanagers and <dir>/Data/Managed/
    state and download read the login from STEAM_SNAPSHOT_LOGIN, and the access code of a
    password-protected branch from STEAM_BETA_PASSWORDS.
    """;

try
{
    return await Run(args);
}
catch (Exception error)
{
    await Console.Error.WriteLineAsync($"{error.GetType().Name}: {error.Message}");
    return 1;
}

static async Task<int> Run(string[] args)
{
    if (args.Length == 0 || args.Length % 2 == 0)
    {
        await Console.Error.WriteLineAsync(Usage);
        return 2;
    }
    var options = new Dictionary<string, string>();
    for (var index = 1; index < args.Length; index += 2)
    {
        if (!args[index].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException($"Expected an option, got {args[index]}\n{Usage}");
        options[args[index][2..]] = args[index + 1];
    }
    string Required(string name) =>
        options.TryGetValue(name, out var value)
            ? value
            : throw new ArgumentException($"{args[0]} requires --{name}\n{Usage}");

    switch (args[0])
    {
        case "login":
            await Login(Required("out"));
            return 0;
        case "state":
        {
            var depots = Required("depots").Split(',').Select(uint.Parse).ToList();
            using var session = await SteamSession.SignIn(SavedLogin());
            var state = await SteamDepot.ReadBranch(
                session,
                uint.Parse(Required("app")),
                Required("branch"),
                depots
            );
            Print(
                new
                {
                    buildid = state.Buildid,
                    updatedAt = state.UpdatedAt,
                    manifests = ManifestStrings(state),
                }
            );
            return 0;
        }
        case "download":
        {
            var depot = uint.Parse(Required("depot"));
            using var session = await SteamSession.SignIn(SavedLogin());
            var state = await SteamDepot.Download(
                session,
                uint.Parse(Required("app")),
                Required("branch"),
                depot,
                Required("out")
            );
            Print(new { buildid = state.Buildid, manifest = state.Manifests[depot].ToString() });
            return 0;
        }
        default:
            await Console.Error.WriteLineAsync(Usage);
            return 2;
    }
}

static SteamLogin SavedLogin() =>
    SteamLogin.Parse(Environment.GetEnvironmentVariable(SteamLogin.EnvironmentVariable));

static Dictionary<string, string> ManifestStrings(BranchState state) =>
    state.Manifests.ToDictionary(pair => pair.Key.ToString(), pair => pair.Value.ToString());

static void Print(object value) => Console.Out.WriteLine(JsonSerializer.Serialize(value));

static async Task Login(string output)
{
    if (Console.IsInputRedirected)
        throw new InvalidOperationException("login needs a terminal");
    await Console.Error.WriteAsync("Steam account: ");
    var account = Console.ReadLine()?.Trim();
    await Console.Error.WriteAsync("Steam password: ");
    var password = ReadHidden();
    if (string.IsNullOrEmpty(account) || string.IsNullOrEmpty(password))
        throw new InvalidOperationException("An account and a password are required");
    var login = await SteamSession.CreateLogin(account, password);
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
    var mode = OperatingSystem.IsWindows()
        ? new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write }
        : new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
        };
    await using var file = new StreamWriter(output, mode);
    await file.WriteAsync(login.ToJson());
    await Console.Error.WriteLineAsync(
        $"Saved the login of {login.Account} to {output}. Run 'just secrets-sync' to copy it to GitHub."
    );
}

static string ReadHidden()
{
    var text = new System.Text.StringBuilder();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter)
        {
            Console.Error.WriteLine();
            return text.ToString();
        }
        if (key.Key == ConsoleKey.Backspace)
        {
            if (text.Length > 0)
                text.Length--;
        }
        else if (!char.IsControl(key.KeyChar))
            text.Append(key.KeyChar);
    }
}

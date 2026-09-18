using System.Diagnostics;
using System.Text;
using BazaarPlusPlus.TestSupport;

var projectRoot = Environment.GetEnvironmentVariable("ProjectRoot");
if (string.IsNullOrWhiteSpace(projectRoot))
{
    projectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
}

if (!OperatingSystem.IsMacOS())
{
    Console.WriteLine("Skipping macOS trampoline repair tests on non-macOS host.");
    return;
}

var tempRoot = Path.Combine(Path.GetTempPath(), $"bpp-trampoline-repair-{Guid.NewGuid():N}");
Directory.CreateDirectory(tempRoot);
try
{
    var gameRoot = Path.Combine(tempRoot, "The Bazaar");
    var macosDir = Path.Combine(gameRoot, "TheBazaar.app", "Contents", "MacOS");
    Directory.CreateDirectory(macosDir);
    Directory.CreateDirectory(Path.Combine(gameRoot, "TheBazaar.app", "Contents"));
    Directory.CreateDirectory(Path.Combine(gameRoot, "BepInEx", "core"));

    var exe = Path.Combine(macosDir, "The Bazaar");
    var orig = exe + ".orig";
    var script = Path.Combine(gameRoot, "run_bepinex.sh");
    var marker = Path.Combine(gameRoot, ".bpp-launch-mode");
    var stub = Path.Combine(tempRoot, "bpp_launcher");
    var fakeBin = Path.Combine(tempRoot, "bin");
    var dotnetRecord = Path.Combine(tempRoot, "dotnet.args");

    File.WriteAllText(
        Path.Combine(gameRoot, "TheBazaar.app", "Contents", "Info.plist"),
        """
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        <plist version="1.0">
        <dict>
            <key>CFBundleExecutable</key>
            <string>The Bazaar</string>
            <key>CFBundleIdentifier</key>
            <string>com.TempoStorm.TheBazaar</string>
            <key>CFBundleShortVersionString</key>
            <string>test</string>
        </dict>
        </plist>
        """
    );
    var nestedApp = Path.Combine(gameRoot, "TheBazaar.app", "TheBazaar_ARM64.app");
    Directory.CreateDirectory(Path.Combine(nestedApp, "Contents", "MacOS"));
    File.Copy(
        Path.Combine(gameRoot, "TheBazaar.app", "Contents", "Info.plist"),
        Path.Combine(nestedApp, "Contents", "Info.plist")
    );
    foreach (var bundle in new[] { Path.Combine(gameRoot, "TheBazaar.app"), nestedApp })
    {
        foreach (
            var relative in new[]
            {
                "Contents/Frameworks/UnityPlayer.dylib",
                "Contents/Frameworks/libmonobdwgc-2.0.dylib",
                "Contents/Resources/Data/boot.config",
            }
        )
        {
            var path = Path.Combine(bundle, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "matching game contents");
        }
    }
    File.WriteAllText(Path.Combine(nestedApp, "Contents", "MacOS", "The Bazaar"), "UNITY arm64");

    File.WriteAllText(exe, "UNITY current executable");
    File.WriteAllText(orig, "UNITY stale backup");
    File.WriteAllText(script, "#!/bin/sh\n");
    // Current installers remove the obsolete launch-mode marker.
    File.WriteAllText(Path.Combine(gameRoot, "libdoorstop.dylib"), "DOORSTOP");
    File.WriteAllText(stub, "TRAMPOLINE STUB");
    Directory.CreateDirectory(fakeBin);

    RunShell($"chmod +x {Quote(exe)} {Quote(orig)} {Quote(script)} {Quote(stub)}");
    WriteTool(
        fakeBin,
        "dotnet",
        $"#!/bin/sh\nprintf '%s\\n' \"$@\" > {Quote(dotnetRecord)}\nexit 0\n"
    );
    WriteTool(fakeBin, "defaults", "#!/bin/sh\nprintf '%s\\n' 'The Bazaar'\n");
    WriteTool(
        fakeBin,
        "otool",
        """
        #!/bin/sh
        file="$1"
        while [ "$#" -gt 0 ]; do
          file="$1"
          shift
        done
        if grep -q 'UNITY' "$file"; then
          printf '%s\n\t@executable_path/../Frameworks/UnityPlayer.dylib\n' "$file"
        else
          printf '%s\n\t/usr/lib/libSystem.B.dylib\n' "$file"
        fi
        """
    );
    // Real codesign refuses to sign or verify a bundle whose root holds anything
    // besides Contents/ ("unsealed contents present in the bundle root"). The
    // Bazaar ships TheBazaar_ARM64.app exactly there, so model that rejection.
    WriteTool(
        fakeBin,
        "codesign",
        """
        #!/bin/sh
        for arg in "$@"; do
          case "$arg" in
            *.app)
              for entry in "$arg"/* "$arg"/.[!.]*; do
                [ -e "$entry" ] || continue
                case "${entry##*/}" in
                  Contents) ;;
                  *)
                    echo "$arg: unsealed contents present in the bundle root" >&2
                    exit 1
                    ;;
                esac
              done
              ;;
          esac
        done
        exit 0
        """
    );
    WriteTool(fakeBin, "xattr", "#!/bin/sh\nexit 0\n");

    var result = RunProcess(
        "/bin/bash",
        new[] { Path.Combine(projectRoot, "run.sh"), "build" },
        new Dictionary<string, string?>
        {
            ["BPP_GAME_ROOT"] = gameRoot,
            ["BPP_TRAMPOLINE_STUB"] = stub,
            ["PATH"] = fakeBin + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"),
        }
    );

    AssertEqual(0, result.ExitCode, result.Output);
    AssertFalse(File.Exists(marker), "Repair must not recreate the obsolete launch-mode marker.");
    AssertEqual(
        "TRAMPOLINE STUB",
        TestInputs.Scratch(exe),
        "run.sh build should restore the trampoline stub as the launched executable.\n"
            + result.Output
    );
    AssertEqual(
        "UNITY current executable",
        TestInputs.Scratch(orig),
        "Repair must preserve the fresh Steam-updated executable, not the stale .orig."
    );
    AssertFalse(
        IsExecutable(script),
        "Repair should disable the prefix launcher while trampoline mode is desired."
    );
    AssertTrue(
        File.Exists(dotnetRecord),
        "run.sh build should still invoke dotnet build after repair."
    );
    AssertFalse(
        Directory.Exists(nestedApp),
        "Repair must leave the duplicate outside the signed application."
    );
    var backups = Path.Combine(gameRoot, ".bpp-bundle-root-stash");
    var backup = Directory.GetDirectories(backups, "*.app").Single();
    AssertEqual(
        "UNITY arm64",
        TestInputs.Scratch(Path.Combine(backup, "Contents", "MacOS", "The Bazaar")),
        "Repair must preserve the duplicate contents in its external backup."
    );
    result = RunProcess(
        "/bin/bash",
        new[] { Path.Combine(projectRoot, "run.sh"), "build" },
        new Dictionary<string, string?>
        {
            ["BPP_GAME_ROOT"] = gameRoot,
            ["BPP_TRAMPOLINE_STUB"] = stub,
            ["PATH"] = fakeBin + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"),
        }
    );
    AssertEqual(0, result.ExitCode, result.Output);
    AssertEqual(
        backup,
        Directory.GetDirectories(backups, "*.app").Single(),
        "Repeated builds must reuse the backup and preserve the signed root layout."
    );

    File.Delete(Path.Combine(gameRoot, "libdoorstop.dylib"));
    File.Delete(dotnetRecord);
    result = RunProcess(
        "/bin/bash",
        new[] { Path.Combine(projectRoot, "run.sh"), "build" },
        new Dictionary<string, string?>
        {
            ["BPP_GAME_ROOT"] = gameRoot,
            ["BPP_TRAMPOLINE_STUB"] = stub,
            ["PATH"] = fakeBin + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"),
        }
    );
    AssertTrue(result.ExitCode != 0, "Missing bootstrap must fail the build.");
    AssertTrue(
        result.Output.Contains("libdoorstop.dylib is missing"),
        "Missing bootstrap must report an actionable error."
    );
    AssertFalse(File.Exists(dotnetRecord), "Build must stop when bootstrap repair cannot run.");
}
finally
{
    try
    {
        Directory.Delete(tempRoot, recursive: true);
    }
    catch
    {
        // Best effort temp cleanup.
    }
}

static void WriteTool(string directory, string name, string contents)
{
    var path = Path.Combine(directory, name);
    File.WriteAllText(path, contents.Replace("\r\n", "\n"));
    RunShell($"chmod +x {Quote(path)}");
}

static bool IsExecutable(string path)
{
    var result = RunShell($"test -x {Quote(path)}");
    return result.ExitCode == 0;
}

static (int ExitCode, string Output) RunShell(
    string command,
    IReadOnlyDictionary<string, string?>? environment = null
) => RunProcess("/bin/sh", new[] { "-c", command }, environment);

static (int ExitCode, string Output) RunProcess(
    string fileName,
    IReadOnlyList<string> arguments,
    IReadOnlyDictionary<string, string?>? environment = null
)
{
    var start = new ProcessStartInfo(fileName)
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };
    foreach (var argument in arguments)
        start.ArgumentList.Add(argument);

    if (environment != null)
    {
        foreach (var pair in environment)
            start.Environment[pair.Key] = pair.Value;
    }

    using var process =
        Process.Start(start) ?? throw new InvalidOperationException($"Failed to start {fileName}");
    var output = new StringBuilder();
    output.Append(process.StandardOutput.ReadToEnd());
    output.Append(process.StandardError.ReadToEnd());
    process.WaitForExit();
    return (process.ExitCode, output.ToString());
}

static string Quote(string value) => "'" + value.Replace("'", "'\"'\"'") + "'";

static void AssertEqual<T>(T expected, T actual, string? message = null)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(message ?? $"Expected {expected}, got {actual}");
}

static void AssertTrue(bool value, string message)
{
    if (!value)
        throw new InvalidOperationException(message);
}

static void AssertFalse(bool value, string message)
{
    if (value)
        throw new InvalidOperationException(message);
}

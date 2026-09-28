using System.Diagnostics;
using System.Text.Json;
using BazaarPlusPlus.GameAssemblyCompatibility;
using BazaarPlusPlus.TestSupport;
using Xunit;

namespace GameAssemblyCompatibility.Tests;

public sealed class CompatibilityEntryTests
{
    [Theory]
    [InlineData("valid", 0)]
    [InlineData("optional-incompatible", 1)]
    [InlineData("required-missing", 1)]
    [InlineData("runtime-missing", 1)]
    public async Task Real_compat_entry_checks_the_release_output_and_propagates_failure(
        string scenario,
        int expectedExit
    )
    {
        using var files = new MetadataFixtures();
        var mod = files.FilePath("mod");
        var scripts = Path.Combine(mod, "scripts");
        var tests = Path.Combine(mod, "tests");
        var managed = files.FilePath("Managed");
        var release = files.FilePath("fresh-release");
        var inventoryDirectory = files.FilePath("release");
        var tools = files.FilePath("tools");
        foreach (
            var directory in new[] { scripts, tests, managed, release, inventoryDirectory, tools }
        )
            Directory.CreateDirectory(directory);
        File.Copy(
            Path.Combine(TestInputs.RepoRoot, "scripts", "test.sh"),
            Path.Combine(scripts, "test.sh")
        );
        File.Copy(
            Path.Combine(TestInputs.RepoRoot, "scripts", "lib.sh"),
            Path.Combine(scripts, "lib.sh")
        );
        File.WriteAllText(Path.Combine(tests, "CompatibilityTests.manifest"), "");
        File.WriteAllText(Path.Combine(managed, "Assembly-CSharp.dll"), "preflight fixture");
        if (scenario != "runtime-missing")
            File.Copy(files.Library("library.dll"), Path.Combine(managed, "Newtonsoft.Json.dll"));
        if (scenario != "required-missing")
            File.Copy(files.Consumer("base.dll"), Path.Combine(release, "base.dll"));
        if (scenario == "optional-incompatible")
            File.Copy(
                files.Consumer("dependency.dll", "single"),
                Path.Combine(release, "dependency.dll")
            );
        File.WriteAllText(
            Path.Combine(inventoryDirectory, "payload.json"),
            JsonSerializer.Serialize(
                new
                {
                    files = new[]
                    {
                        new
                        {
                            path = "BepInEx/plugins/base.dll",
                            producer = "managed",
                            optional = false,
                        },
                        new
                        {
                            path = "BepInEx/plugins/dependency.dll",
                            producer = "managed",
                            optional = true,
                        },
                    },
                }
            )
        );

        // Run the real shell entry and real checker. Only compilation is replaced by a local
        // producer of PE fixtures; the output path comes from the MSBuild query, not a bin guess.
        var shim = Path.Combine(tools, "dotnet");
        File.WriteAllText(
            shim,
            """
            #!/usr/bin/env bash
            set -euo pipefail
            case "$1" in
              build) printf '%s\n' "$@" > "$BPP_TEST_BUILD_ARGS" ;;
              msbuild) printf '%s\n' "$BPP_TEST_OUTPUT" ;;
              run)
                while [[ "$1" != "--" ]]; do shift; done
                shift
                exec "$BPP_TEST_DOTNET" exec --runtimeconfig "$BPP_TEST_RUNTIME_CONFIG" "$BPP_TEST_CHECKER" "$@"
                ;;
              *) exit 88 ;;
            esac
            """.Replace("\r\n", "\n", StringComparison.Ordinal)
        );
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(
                shim,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            );
        var start = new ProcessStartInfo("bash")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (
            var argument in new[]
            {
                Path.Combine(scripts, "test.sh"),
                "test-compat",
                "-p:ManagedPath=" + managed,
                "-p:Configuration=Debug",
                "-p:BppDeployToGame=true",
                "-p:BuildProductionPackage=true",
            }
        )
            start.ArgumentList.Add(argument);
        start.Environment["PATH"] =
            tools + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
        start.Environment["BPP_TEST_OUTPUT"] = release;
        start.Environment["BPP_TEST_BUILD_ARGS"] = files.FilePath("build-args.txt");
        start.Environment["BPP_TEST_CHECKER"] = typeof(MemberCompatibility).Assembly.Location;
        start.Environment["BPP_TEST_RUNTIME_CONFIG"] = Path.ChangeExtension(
            typeof(CompatibilityEntryTests).Assembly.Location,
            ".runtimeconfig.json"
        );
        start.Environment["BPP_TEST_DOTNET"] = Path.GetFullPath(
            Path.Combine(
                Path.GetDirectoryName(typeof(object).Assembly.Location)!,
                "..",
                "..",
                "..",
                OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"
            )
        );
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw;
        }
        var output = await stdout + await stderr;
        Assert.True(process.ExitCode == expectedExit, output);
        if (scenario == "runtime-missing")
        {
            Assert.Contains("requires Assembly-CSharp.dll and Newtonsoft.Json.dll", output);
            Assert.False(File.Exists(files.FilePath("build-args.txt")));
            return;
        }
        using var argumentStream = File.OpenRead(files.FilePath("build-args.txt"));
        using var argumentReader = new StreamReader(argumentStream);
        var arguments = argumentReader.ReadToEnd().Split('\n');
        Assert.Equal(
            "-p:Configuration=Release",
            arguments.Last(a => a.StartsWith("-p:Configuration=", StringComparison.Ordinal))
        );
        Assert.Equal(
            "-p:BppDeployToGame=false",
            arguments.Last(a => a.StartsWith("-p:BppDeployToGame=", StringComparison.Ordinal))
        );
        Assert.Equal(
            "-p:BuildProductionPackage=false",
            arguments.Last(a =>
                a.StartsWith("-p:BuildProductionPackage=", StringComparison.Ordinal)
            )
        );
        Assert.Contains(
            scenario switch
            {
                "optional-incompatible" => "dependency.dll: missing",
                "required-missing" => "Required Payload assembly is missing",
                _ => "Compatibility checks passed: 1/1",
            },
            output
        );
    }

    [Fact]
    public void Empty_inventory_and_invalid_PE_fail_instead_of_passing_vacuously()
    {
        using var files = new MetadataFixtures();
        var inventory = files.FilePath("empty.json");
        File.WriteAllText(inventory, "{\"files\": []}");
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(
            1,
            CompatibilityCommand.Run(
                ["--payload", files.Library("runtime.dll"), inventory, files.Root],
                output,
                error
            )
        );
        Assert.Contains("no managed assemblies", error.ToString());
        var bad = files.FilePath("bad.dll");
        File.WriteAllText(bad, "not a PE");
        Assert.Equal(
            1,
            CompatibilityCommand.Run(
                ["--assemblies", files.FilePath("runtime.dll"), bad],
                output,
                error
            )
        );
    }
}

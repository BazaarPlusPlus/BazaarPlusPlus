#nullable enable
using System.Reflection;
using System.Text.Json;
using BazaarPlusPlus.TestSupport;
using Xunit;

namespace Architecture.Tests;

public sealed class SourceTextBanTests
{
    private static readonly string[] PermanentHeaders =
    {
        "src/**.cs",
        "build/**.cs",
        "Shared/TestFiles/**.cs",
        "CombatImpact.Corpus/**.cs",
    };

    private static readonly string[] FrozenDebtFiles =
    {
        "Architecture.Tests/BundleSealConvergenceArchitectureTests.cs",
        "Architecture.Tests/CollectionMechanicArchitectureTests.cs",
        "Architecture.Tests/CoreLayeringTests.cs",
        "Architecture.Tests/CurrentReplayRecordingArchitectureTests.cs",
        "Architecture.Tests/EndOfRunCaptureArchitectureTests.cs",
        "Architecture.Tests/HistoryPagingArchitectureTests.cs",
        "Architecture.Tests/LoggingGovernanceTests.cs",
        "Architecture.Tests/MacNativeReplayManagedArchitectureTests.cs",
        "Architecture.Tests/MacNativeReplayNativeArchitectureTests.cs",
        "Architecture.Tests/MacNativeReplayPackagingArchitectureTests.cs",
        "Architecture.Tests/NativeAssetLoadingArchitectureTests.cs",
        "Architecture.Tests/NativeCardPreviewArchitectureTests.cs",
        "Architecture.Tests/NativeMonsterBoardArchitectureTests.cs",
        "Architecture.Tests/NativePairedTooltipArchitectureTests.cs",
        "Architecture.Tests/OutboundNetworkMainMenuArchitectureTests.cs",
        "Architecture.Tests/OutboundNetworkSeedArchitectureTests.cs",
        "Architecture.Tests/RemoteEmbeddedCatalogArchitectureTests.cs",
        "Architecture.Tests/V5DataPipelineArchitectureTests.cs",
        "HistoryPanelFactory.Tests/Program.cs",
        "LiveBuildRecommendations.Tests/Program.cs",
        "NativeAssetCompatibility.Tests/Program.cs",
        "PtrCompatibility.Tests/Program.cs",
        "VoiceSubtitles.Tests/VoiceObserverLoggingTests.cs",
    };

    [Fact]
    public void Banned_symbols_cover_every_file_to_text_overload()
    {
        var listed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in TestInputs.DataLines("tests/BannedSymbols.txt"))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed[0] == '#')
                continue;
            listed.Add(trimmed.Split(';')[0].Trim());
        }

        var missing = DiscoverFileToTextDocumentationIds()
            .Where(id => !listed.Contains(id))
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        Assert.True(
            missing.Length == 0,
            "BannedSymbols.txt is missing file-to-text overloads:\n  "
                + string.Join("\n  ", missing)
        );
    }

    [Fact]
    public void Editorconfig_rs0030_exemptions_are_the_permanent_set_plus_frozen_debt()
    {
        var sections = TestInputs
            .EditorConfig(".editorconfig")
            .Concat(TestInputs.EditorConfig("tests/.editorconfig"))
            .Where(section =>
                section.Properties.TryGetValue(
                    "dotnet_diagnostic.RS0030.severity",
                    out var severity
                ) && string.Equals(severity, "none", StringComparison.OrdinalIgnoreCase)
            )
            .ToArray();

        var headers = sections.Select(section => section.Header).ToHashSet(StringComparer.Ordinal);
        foreach (var permanent in PermanentHeaders)
            Assert.True(
                headers.Contains(permanent),
                $"Permanent RS0030 exemption '{permanent}' is missing."
            );

        var debt = headers
            .Where(header => !PermanentHeaders.Contains(header, StringComparer.Ordinal))
            .OrderBy(header => header, StringComparer.Ordinal)
            .ToArray();
        var unexpected = debt.Except(FrozenDebtFiles, StringComparer.Ordinal).ToArray();
        var missing = FrozenDebtFiles.Except(debt, StringComparer.Ordinal).ToArray();
        Assert.True(
            unexpected.Length == 0 && missing.Length == 0,
            "RS0030 debt must equal SourceTextBanTests.FrozenDebtFiles (shrink only).\n  extra: "
                + string.Join(", ", unexpected)
                + "\n  missing: "
                + string.Join(", ", missing)
        );
    }

    [Fact]
    public void Test_projects_do_not_silence_rs0030()
    {
        var silenced = new List<string>();
        foreach (
            var relative in Directory
                .EnumerateFiles(
                    Path.Combine(TestInputs.RepoRoot, "tests"),
                    "*.*",
                    SearchOption.AllDirectories
                )
                .Select(path => TestInputs.RepoRelative(path))
                .Where(relative =>
                    (
                        relative.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                        || relative.EndsWith(".props", StringComparison.OrdinalIgnoreCase)
                    )
                    && !relative.Contains("/bin/", StringComparison.Ordinal)
                    && !relative.Contains("/obj/", StringComparison.Ordinal)
                )
        )
        {
            var document = TestInputs.MsBuild(relative);
            foreach (var element in document.Descendants())
            {
                if (element.Name.LocalName is not ("NoWarn" or "WarningsNotAsErrors"))
                    continue;
                if (ContainsDiagnostic(element.Value, "RS0030"))
                    silenced.Add($"{relative}: {element.Name.LocalName}");
            }
        }

        Assert.True(
            silenced.Count == 0,
            "Test projects must not hide RS0030:\n  " + string.Join("\n  ", silenced)
        );
    }

    [Fact]
    public void Architecture_tests_sarif_has_no_in_source_rs0030_suppression()
    {
        var sarifPath = FindArchitectureSarif();
        using var stream = File.OpenRead(sarifPath);
        using var document = JsonDocument.Parse(stream);
        var suppressed = new List<string>();
        foreach (var run in document.RootElement.GetProperty("runs").EnumerateArray())
        {
            if (!run.TryGetProperty("results", out var results))
                continue;
            foreach (var result in results.EnumerateArray())
            {
                var rule = result.TryGetProperty("ruleId", out var ruleId)
                    ? ruleId.GetString()
                    : null;
                if (!string.Equals(rule, "RS0030", StringComparison.Ordinal))
                    continue;
                if (
                    result.TryGetProperty("suppressions", out var suppressions)
                    && suppressions.GetArrayLength() > 0
                )
                {
                    suppressed.Add(result.GetRawText());
                }
            }
        }

        Assert.True(
            suppressed.Count == 0,
            "In-source RS0030 suppressions are forbidden:\n  " + string.Join("\n  ", suppressed)
        );
    }

    [Fact]
    public void TestInputs_refuses_source_and_decompiled_paths()
    {
        Assert.Throws<InvalidOperationException>(() =>
            TestInputs.Markdown("src/BazaarPlusPlus/Plugin.cs")
        );
        Assert.Throws<InvalidOperationException>(() =>
            TestInputs.Fixture("src/BazaarPlusPlus/Plugin.cs")
        );
        Assert.Throws<InvalidOperationException>(() =>
            TestInputs.Scratch(Path.Combine(TestInputs.RepoRoot, "CLAUDE.md"))
        );
    }

    private static string FindArchitectureSarif()
    {
        var projectDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
        var matches = Directory
            .EnumerateFiles(projectDir, "Architecture.Tests.sarif", SearchOption.AllDirectories)
            .ToArray();
        Assert.True(
            matches.Length > 0,
            $"Architecture.Tests.sarif was not written under '{projectDir}'."
        );
        return matches.OrderByDescending(File.GetLastWriteTimeUtc).First();
    }

    private static bool ContainsDiagnostic(string value, string diagnostic)
    {
        foreach (
            var part in value.Split(
                new[] { ';', ',', ' ' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
        )
        {
            if (string.Equals(part, diagnostic, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static IReadOnlyList<string> DiscoverFileToTextDocumentationIds()
    {
        var ids = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var method in typeof(File).GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (!IsFileToTextName(method.Name))
                continue;
            ids.Add(DocumentationId(method));
        }

        var openText = typeof(FileInfo).GetMethod(nameof(FileInfo.OpenText), Type.EmptyTypes);
        Assert.NotNull(openText);
        ids.Add(DocumentationId(openText));

        foreach (
            var ctor in typeof(StreamReader).GetConstructors(
                BindingFlags.Public | BindingFlags.Instance
            )
        )
        {
            var parameters = ctor.GetParameters();
            if (parameters.Length == 0 || parameters[0].ParameterType != typeof(string))
                continue;
            ids.Add(DocumentationId(ctor));
        }

        return ids.ToArray();
    }

    private static bool IsFileToTextName(string name) =>
        name
            is "ReadAllText"
                or "ReadAllTextAsync"
                or "ReadAllLines"
                or "ReadAllLinesAsync"
                or "ReadLines"
                or "ReadLinesAsync"
                or "ReadAllBytes"
                or "ReadAllBytesAsync"
                or "OpenText";

    private static string DocumentationId(MethodBase method)
    {
        var typeName = method.DeclaringType!.FullName!.Replace('+', '.');
        var name = method is ConstructorInfo ? "#ctor" : method.Name;
        var parameters = method.GetParameters();
        if (parameters.Length == 0)
            return "M:" + typeName + "." + name;
        return "M:"
            + typeName
            + "."
            + name
            + "("
            + string.Join(",", parameters.Select(ParameterId))
            + ")";
    }

    private static string ParameterId(ParameterInfo parameter)
    {
        var type = parameter.ParameterType;
        if (type.IsByRef)
            return TypeId(type.GetElementType()!) + "@";
        return TypeId(type);
    }

    private static string TypeId(Type type)
    {
        if (type.IsArray)
            return TypeId(type.GetElementType()!) + "[]";
        if (!type.IsGenericType)
            return type.FullName!.Replace('+', '.');

        var definition = type.GetGenericTypeDefinition().FullName!.Replace('+', '.');
        var tick = definition.IndexOf('`');
        if (tick >= 0)
            definition = definition[..tick];
        return definition + "{" + string.Join(",", type.GetGenericArguments().Select(TypeId)) + "}";
    }
}

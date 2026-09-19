#nullable enable
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace Architecture.Tests;

public sealed class LoggingGovernanceTests
{
    private static readonly Regex LegacyBppLogCall = new(
        @"\bBppLog\.(?<member>Debug|Info|Warn|Error)\s*\(",
        RegexOptions.CultureInvariant
    );

    private static readonly Regex LegacyVoiceSubtitlesMember = new(
        @"\bVoiceSubtitlesLog\.(?<member>Info|Debug|Warn|Error|Field|ObjectId|Verbose)\b",
        RegexOptions.CultureInvariant
    );

    private static readonly Regex LegacyStorageLoggerCall = new(
        @"\??\.(?<member>Warn|Error)\s*\(",
        RegexOptions.CultureInvariant
    );

    private static readonly Regex LegacyBppLogFacadeMember = new(
        @"\bstatic\s+(?:string|void)\s+(?:Format|FormatError|Debug|Info|Warn|Error)\s*\(",
        RegexOptions.CultureInvariant
    );

    private static readonly Regex BppLogShimClass = new(
        @"\bstatic\s+class\s+BppLog\b",
        RegexOptions.CultureInvariant
    );

    private static readonly Regex LogShapedCall = new(
        @"\.(?<member>Log|LogDebug|LogInfo|LogWarning|LogError|LogFatal|LogMessage)\s*\(",
        RegexOptions.CultureInvariant
    );

    private static readonly Regex RegisteredEventDefinition = new(
        @"\b(?:internal|private|public)\s+static\s+readonly\s+"
            + @"BppLogEventDefinition\s+[A-Za-z_][A-Za-z0-9_]*\s*=\s*new\s*\(",
        RegexOptions.Singleline | RegexOptions.CultureInvariant
    );

    private static readonly Regex EventDefinitionConstruction = new(
        RegisteredEventDefinition
            + @"|\bnew\s+BppLogEventDefinition\s*\("
            + @"|\bBppLogEventDefinition\s+[A-Za-z_][A-Za-z0-9_]*\s*(?:=|=>)\s*new\s*\("
            + @"|\bBppLog\.(?:DebugEvent|InfoEvent|WarnEvent|ErrorEvent)\s*\(\s*new\s*\("
            + @"|\bBppLogEventDefinition\s+[A-Za-z_][A-Za-z0-9_]*\s*\([^;{}]*\)\s*"
            + @"=>[\s\S]{0,512}?\bnew\s*\("
            + @"|\bBppLogEventDefinition\s+[A-Za-z_][A-Za-z0-9_]*\s*\([^;{}]*\)\s*"
            + @"\{[\s\S]*?\breturn[\s\S]{0,512}?\bnew\s*\("
            + @"|\bFunc\s*<\s*BppLogEventDefinition\s*>\s+[A-Za-z_][A-Za-z0-9_]*\s*=\s*"
            + @"(?:\([^)]*\)|[A-Za-z_][A-Za-z0-9_]*)\s*=>[\s\S]{0,512}?\bnew\s*\("
            + @"|\bBppLogEventDefinition\s+[A-Za-z_][A-Za-z0-9_]*\s*"
            + @"=>[\s\S]{0,512}?\bnew\s*\("
            + @"|\bBppLogEventDefinition\s+[A-Za-z_][A-Za-z0-9_]*\s*"
            + @"\{[\s\S]*?\breturn[\s\S]{0,512}?\bnew\s*\(",
        RegexOptions.Singleline | RegexOptions.CultureInvariant
    );

    private static readonly HashSet<string> ApprovedBepInExAdapters = new(StringComparer.Ordinal)
    {
        "BazaarPlusPlus/Infrastructure/BppLog.cs",
    };

    [Fact]
    public void Legacy_free_text_BppLog_calls_are_absent()
    {
        var root = Path.Combine(RepoRoot(), "src", "BazaarPlusPlus");
        AssertNoFingerprints(
            FingerprintMatches(root, LegacyBppLogCall, relativeTo: root),
            "Legacy BppLog free-text calls are prohibited."
        );
    }

    [Fact]
    public void Legacy_facade_suppressor_and_exe_runner_BppLog_shims_are_absent()
    {
        var repoRoot = RepoRoot();
        var infrastructureRoot = Path.Combine(repoRoot, "src", "BazaarPlusPlus", "Infrastructure");
        var facade = File.ReadAllText(Path.Combine(infrastructureRoot, "BppLog.cs"));

        Assert.False(File.Exists(Path.Combine(infrastructureRoot, "LogRepeatSuppressor.cs")));
        Assert.DoesNotContain("LogRepeatSuppressor", facade, StringComparison.Ordinal);
        Assert.DoesNotContain("Suppressor", facade, StringComparison.Ordinal);
        Assert.DoesNotMatch(LegacyBppLogFacadeMember, facade);
        Assert.DoesNotContain("private static void Emit(", facade, StringComparison.Ordinal);

        var violations = Directory
            .EnumerateFiles(
                Path.Combine(repoRoot, "tests"),
                "*.csproj",
                SearchOption.AllDirectories
            )
            .Where(project =>
                !File.ReadAllText(project)
                    .Contains("Microsoft.NET.Test.Sdk", StringComparison.Ordinal)
            )
            .SelectMany(project => ProductionFiles(Path.GetDirectoryName(project)!))
            .Where(file => BppLogShimClass.IsMatch(File.ReadAllText(file)))
            .Select(file => Path.GetRelativePath(repoRoot, file).Replace('\\', '/'))
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Exe-runner projects must consume production structured logging seams instead of "
                + "declaring BppLog compatibility shims:\n"
                + string.Join("\n", violations)
        );
    }

    [Theory]
    [InlineData("public static void Info(string component, string message)")]
    [InlineData("internal static void Warn(string component, string message)")]
    [InlineData("private static string Format(string component, string message)")]
    public void Legacy_facade_ratchet_is_independent_of_access_modifier(string source)
    {
        Assert.Matches(LegacyBppLogFacadeMember, source);
    }

    [Theory]
    [InlineData("public static class BppLog")]
    [InlineData("internal static class BppLog")]
    [InlineData("static class BppLog")]
    public void Exe_runner_shim_ratchet_is_independent_of_access_modifier(string source)
    {
        Assert.Matches(BppLogShimClass, source);
    }

    [Fact]
    public void VoiceSubtitles_wrapper_surface_is_absent()
    {
        var root = Path.Combine(RepoRoot(), "src", "BazaarPlusPlus");
        AssertNoFingerprints(
            FingerprintMatches(root, LegacyVoiceSubtitlesMember, relativeTo: root),
            "Legacy VoiceSubtitles wrappers and helper members are prohibited."
        );
    }

    [Fact]
    public void VoiceSubtitles_free_text_wrappers_and_aliases_are_absent()
    {
        var root = Path.Combine(RepoRoot(), "src", "BazaarPlusPlus");
        Assert.False(
            File.Exists(Path.Combine(root, "Game", "VoiceSubtitles", "VoiceSubtitlesLog.cs"))
        );
        Assert.False(
            File.Exists(
                Path.Combine(root, "GameInterop", "VoiceSubtitles", "VoiceSubtitlesInteropLog.cs")
            )
        );

        foreach (var file in ProductionFiles(root))
        {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain("VoiceSubtitlesLog.", source, StringComparison.Ordinal);
            Assert.DoesNotContain("VoiceSubtitlesInteropLog", source, StringComparison.Ordinal);
            Assert.DoesNotContain("using VoiceSubtitlesLog", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void CombatReplay_and_Screenshots_free_text_logging_is_absent()
    {
        var gameRoot = Path.Combine(RepoRoot(), "src", "BazaarPlusPlus", "Game");
        var migratedRoots = new[]
        {
            Path.Combine(gameRoot, "CombatReplay"),
            Path.Combine(gameRoot, "Screenshots"),
        };
        var violations = migratedRoots
            .SelectMany(ProductionFiles)
            .SelectMany(file =>
                LegacyBppLogCall
                    .Matches(File.ReadAllText(file))
                    .Select(match => $"{Path.GetRelativePath(gameRoot, file)}:{match.Value}")
            )
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "CombatReplay/Screenshots migrations must not retain free-text BppLog calls:\n"
                + string.Join("\n", violations)
        );
    }

    [Fact]
    public void Panel_domains_free_text_logging_is_absent()
    {
        var gameRoot = Path.Combine(RepoRoot(), "src", "BazaarPlusPlus", "Game");
        var migratedRoots = new[]
        {
            Path.Combine(gameRoot, "CollectionPanel"),
            Path.Combine(gameRoot, "HistoryPanel"),
            Path.Combine(gameRoot, "LiveBuildPanel"),
            Path.Combine(gameRoot, "OverlayPanels"),
        };
        var violations = migratedRoots
            .SelectMany(ProductionFiles)
            .SelectMany(file =>
                LegacyBppLogCall
                    .Matches(File.ReadAllText(file))
                    .Select(match => $"{Path.GetRelativePath(gameRoot, file)}:{match.Value}")
            )
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Panel domain migrations must not retain free-text BppLog calls:\n"
                + string.Join("\n", violations)
        );
    }

    [Fact]
    public void Storage_free_text_port_is_absent()
    {
        var storageRoot = Path.Combine(RepoRoot(), "src", "BazaarPlusPlus.Storage");

        AssertNoFingerprints(
            FingerprintMatches(storageRoot, LegacyStorageLoggerCall, relativeTo: storageRoot),
            "The Storage free-text logger surface is prohibited."
        );
    }

    [Fact]
    public void Storage_logging_port_and_project_stay_free_of_BepInEx()
    {
        var storageRoot = Path.Combine(RepoRoot(), "src", "BazaarPlusPlus.Storage");
        var violations = Directory
            .EnumerateFiles(storageRoot, "*", SearchOption.AllDirectories)
            .Where(path =>
                path.EndsWith(".cs", StringComparison.Ordinal)
                || path.EndsWith(".csproj", StringComparison.Ordinal)
            )
            .Where(path => File.ReadAllText(path).Contains("BepInEx", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(storageRoot, path).Replace('\\', '/'))
            .ToArray();

        Assert.Empty(violations);

        var compiledReferences = typeof(BazaarPlusPlus.Storage.RunLog.IRunLogStore)
            .Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();
        Assert.DoesNotContain(
            compiledReferences,
            reference => reference.StartsWith("BepInEx", StringComparison.OrdinalIgnoreCase)
        );
    }

    [Fact]
    public void Log_shaped_calls_exist_only_in_approved_adapters()
    {
        var sourceRoot = Path.Combine(RepoRoot(), "src");
        var actual = FingerprintMatches(
            sourceRoot,
            LogShapedCall,
            relativeTo: sourceRoot,
            excluded: ApprovedBepInExAdapters
        );

        AssertNoFingerprints(
            actual,
            "Direct BepInEx writes are restricted to approved adapters; generic log-shaped calls "
                + "outside those adapters are prohibited."
        );
    }

    [Fact]
    public void Structured_events_can_only_use_declared_scope_tokens()
    {
        var loggingRoot = Path.Combine(
            RepoRoot(),
            "src",
            "BazaarPlusPlus",
            "Infrastructure",
            "Logging",
            "Core"
        );
        var schema = File.ReadAllText(Path.Combine(loggingRoot, "BppLogSchema.cs"));
        var renderer = File.ReadAllText(Path.Combine(loggingRoot, "BppLogEventRenderer.cs"));
        var facade = File.ReadAllText(
            Path.Combine(RepoRoot(), "src", "BazaarPlusPlus", "Infrastructure", "BppLog.cs")
        );

        Assert.Contains(
            "private BppLogFeatureScope(string prefixName, string eventIdPrefix)",
            schema
        );
        Assert.Contains("BppLogFeatureScope.IsDeclared(definition.Scope)", renderer);
        Assert.DoesNotMatch(
            new Regex(
                @"(?:DebugEvent|InfoEvent|WarnEvent|ErrorEvent)\s*\([^)]*\bstring\b",
                RegexOptions.Singleline | RegexOptions.CultureInvariant
            ),
            facade
        );

        foreach (var file in ProductionFiles(Path.Combine(RepoRoot(), "src", "BazaarPlusPlus")))
        {
            if (
                Path.GetFullPath(file)
                == Path.GetFullPath(Path.Combine(loggingRoot, "BppLogSchema.cs"))
            )
                continue;
            Assert.DoesNotContain("new BppLogFeatureScope", File.ReadAllText(file));
        }
    }

    [Fact]
    public void Production_helpers_do_not_accept_dynamic_log_scope_parameters()
    {
        var dynamicLogParameter = new Regex(
            @"\bstring\s+log(?:Tag|Component|Scope)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
        );
        var violations = ProductionFiles(Path.Combine(RepoRoot(), "src", "BazaarPlusPlus"))
            .Where(file => dynamicLogParameter.IsMatch(File.ReadAllText(file)))
            .Select(file => Path.GetRelativePath(RepoRoot(), file))
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Dynamic log scope parameters are prohibited:\n" + string.Join("\n", violations)
        );
    }

    [Fact]
    public void Plugin_teardown_resets_static_encounter_health_owners()
    {
        var plugin = File.ReadAllText(
            Path.Combine(RepoRoot(), "src", "BazaarPlusPlus", "Plugin.cs")
        );

        Assert.Contains("TooltipEncounterProbeReader.Reset();", plugin);
        Assert.Contains("BppTooltipSectionRenderPatch.ResetEncounterHealth();", plugin);
    }

    [Fact]
    public void Production_event_definitions_are_discoverable_registered_static_fields()
    {
        var violations = new List<string>();
        foreach (var file in ProductionFiles(Path.Combine(RepoRoot(), "src", "BazaarPlusPlus")))
        {
            var source = File.ReadAllText(file);
            var constructions = EventDefinitionConstruction.Matches(source);
            if (constructions.Count == 0)
                continue;

            var registered = RegisteredEventDefinition.Matches(source);
            if (
                registered.Count != constructions.Count
                || !source.Contains("[BppLogEventSource]", StringComparison.Ordinal)
            )
            {
                var relative = Path.GetRelativePath(RepoRoot(), file).Replace('\\', '/');
                violations.Add(
                    relative
                        + ": event definitions must be static readonly fields on a "
                        + "[BppLogEventSource] class"
                );
            }
        }

        Assert.True(
            violations.Count == 0,
            "Every production event definition must be visible to catalog discovery.\n"
                + string.Join("\n", violations)
        );
    }

    [Theory]
    [InlineData("private static BppLogEventDefinition Build() => new(scope, id, fields);")]
    [InlineData("private static BppLogEventDefinition Build() { return new(scope, id, fields); }")]
    [InlineData(
        "private static BppLogEventDefinition Build() => ok ? new(scope, id, fields) : fallback;"
    )]
    [InlineData("private static Func<BppLogEventDefinition> Build = () => new(scope, id, fields);")]
    [InlineData(
        "private static BppLogEventDefinition Build => ok ? new(scope, id, fields) : fallback;"
    )]
    public void Event_definition_construction_scan_recognizes_target_typed_factories(string source)
    {
        Assert.NotEmpty(EventDefinitionConstruction.Matches(source));
        Assert.Empty(RegisteredEventDefinition.Matches(source));
    }

    private static Dictionary<string, string> FingerprintMatches(
        string root,
        Regex pattern,
        string relativeTo,
        IReadOnlySet<string>? excluded = null
    )
    {
        var fingerprints = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in ProductionFiles(root))
        {
            var relative = Path.GetRelativePath(relativeTo, file).Replace('\\', '/');
            if (excluded?.Contains(relative) == true)
                continue;

            var source = File.ReadAllText(file);
            var matches = pattern.Matches(source);
            if (matches.Count == 0)
                continue;

            var entries = new string[matches.Count];
            for (var index = 0; index < matches.Count; index++)
            {
                var match = matches[index];
                entries[index] =
                    LineNumber(source, match.Index) + ":" + match.Groups["member"].Value;
            }
            fingerprints.Add(relative, string.Join(",", entries));
        }
        return fingerprints;
    }

    private static IEnumerable<string> ProductionFiles(string root) =>
        Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(file =>
                !file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                && !file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)
            );

    private static int LineNumber(string source, int characterIndex)
    {
        var line = 1;
        for (var index = 0; index < characterIndex; index++)
        {
            if (source[index] == '\n')
                line++;
        }
        return line;
    }

    private static void AssertNoFingerprints(
        IReadOnlyDictionary<string, string> actual,
        string message
    )
    {
        var violations = actual
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Key + ":" + pair.Value)
            .ToArray();

        Assert.True(violations.Length == 0, message + "\n" + string.Join("\n", violations));
    }

    private static string RepoRoot([CallerFilePath] string thisFile = "")
    {
        var testDir = Path.GetDirectoryName(thisFile)!;
        return Path.GetFullPath(Path.Combine(testDir, "..", ".."));
    }
}

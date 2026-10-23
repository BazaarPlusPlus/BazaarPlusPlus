#nullable enable
using System.Reflection;
using Architecture.Tests;
using BazaarPlusPlus.Infrastructure.Logging;
using Xunit;

namespace AndroidHost.Tests;

/// <summary>
/// Architecture.Tests holds the desktop and shared assemblies to <see cref="LogEventIdRule"/>; no
/// desktop build compiles the Android host, so its event ids are checked here.
/// </summary>
public sealed class AndroidHostLogEventIdTests
{
    [Fact]
    public void Every_compiled_android_log_event_id_has_three_segments_under_its_scope()
    {
        var core = CompiledAssembly.Load(typeof(BppLogFeatureScope).Assembly.Location);
        var prefixes = LogEventIdRule.Prefixes(
            core.Types.Single(type => type.FullName == LogEventIdRule.ScopeType)
        );
        var host = CompiledAssembly.Load(
            typeof(AndroidHostLogEventIdTests)
                .Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .Single(attribute => attribute.Key == "BppAndroidHostAssembly")
                .Value!
        );

        var violations = LogEventIdRule
            .Violations(host.Types.SelectMany(type => type.Methods), prefixes)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Android host BppLogEvent ids must render (BppLogSchemaRules.IsDottedSnakeIdentifier, "
                + $"{LogEventIdRule.MinimumSegments}+ segments under the scope's prefix):\n  "
                + string.Join("\n  ", violations)
        );
    }
}

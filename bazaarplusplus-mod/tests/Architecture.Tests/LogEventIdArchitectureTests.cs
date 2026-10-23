#nullable enable
using Xunit;
using static Architecture.Tests.ArchitectureRules;

namespace Architecture.Tests;

/// <summary>
/// Every event id the desktop and shared assemblies compile renders (<see cref="LogEventIdRule"/>);
/// AndroidHost.Tests holds the Android host to the same rule.
/// </summary>
public sealed class LogEventIdArchitectureTests
{
    [Fact]
    public void Every_compiled_log_event_id_has_three_segments_under_its_scope()
    {
        Holds(
            "BppLogEvent ids are literal, under their scope's prefix, and have at least "
                + $"{LogEventIdRule.MinimumSegments} dotted-snake segments "
                + "(BppLogSchemaRules.IsDottedSnakeIdentifier).",
            build =>
            {
                RequireReferencedType(build, LogEventIdRule.EventType);
                var prefixes = LogEventIdRule.Prefixes(build.Type(LogEventIdRule.ScopeType));
                return LogEventIdRule.Violations(
                    build.Types.SelectMany(type => type.Methods),
                    prefixes
                );
            }
        );
    }
}

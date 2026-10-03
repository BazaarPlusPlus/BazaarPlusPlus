#nullable enable
using Xunit;
using static Architecture.Tests.ArchitectureRules;

namespace Architecture.Tests;

public sealed class LogAdapterArchitectureTests
{
    /// <summary>The only types allowed to write to BepInEx; field redaction happens before them.</summary>
    private static readonly string[] ApprovedBepInExAdapters =
    {
        "BazaarPlusPlus.Infrastructure.BppLog",
    };

    [Fact]
    public void Log_shaped_calls_exist_only_in_approved_adapters()
    {
        const string logSource = "BepInEx.Logging.ManualLogSource";
        Holds(
            "Direct BepInEx log writes are restricted to approved adapters.",
            build =>
            {
                foreach (var adapter in ApprovedBepInExAdapters)
                    _ = build.Type(adapter);
                // BepInEx ships outside the build output; the adapter's own reference resolves it.
                RequireReferencedType(build, logSource);
                return build
                    .Types.Where(type => !ApprovedBepInExAdapters.Contains(type.FullName))
                    .SelectMany(type => type.Methods)
                    .SelectMany(method =>
                        method
                            .Instructions.Where(op =>
                                op.IsMemberAccess
                                && op.TargetType?.FullName == logSource
                                && op.TargetMember!.StartsWith("Log", StringComparison.Ordinal)
                            )
                            .Select(op => $"{method} {op}")
                    );
            }
        );
    }
}

#nullable enable
using System.Reflection;
using Architecture.Tests;
using Xunit;

namespace AndroidHost.Tests;

/// <summary>
/// Architecture.Tests holds the Desktop UI to <see cref="InGameColourRule"/>; no desktop build
/// compiles the Mobile UI, so the Android host's copy of it is checked here.
/// </summary>
public sealed class MobileUiColourTests
{
    private const string MobileNamespace = "BazaarPlusPlus.Mobile";

    [Fact]
    public void Mobile_ui_draws_only_in_game_tokens()
    {
        var host = CompiledAssembly.Load(
            typeof(MobileUiColourTests)
                .Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .Single(attribute => attribute.Key == "BppAndroidHostAssembly")
                .Value!
        );
        var mobile = host
            .Types.Where(type => CompiledBuild.IsInNamespace(type.Namespace, MobileNamespace))
            .ToArray();
        InGameColourRule.RequireThemeConstructsColours(
            mobile.Single(type => type.FullName == MobileNamespace + ".Ui.InGameTheme")
        );
        // The whole tree, so a move inside it cannot make the rule pass over a few types.
        Assert.True(mobile.Length >= 50, $"Only {mobile.Length} Mobile UI types were scanned.");

        var violations = InGameColourRule.Violations(mobile).ToArray();

        Assert.True(
            violations.Length == 0,
            "Mobile UI takes colours from InGameTheme (InGameTheme.WithAlpha for a fraction), "
                + "never a Color literal or a parsed colour string:\n  "
                + string.Join("\n  ", violations)
        );
    }
}

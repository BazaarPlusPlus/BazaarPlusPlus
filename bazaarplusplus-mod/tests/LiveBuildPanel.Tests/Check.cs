using Xunit;

namespace LiveBuildPanel.Tests;

// The capsule's checks kept their messages; xUnit reports them with the values compared.
internal static class Check
{
    public static void That(bool condition, string message) => Assert.True(condition, message);

    public static void Equal<T>(T expected, T actual, string message) =>
        Assert.True(
            EqualityComparer<T>.Default.Equals(expected, actual),
            $"{message} Expected {expected}, got {actual}."
        );
}

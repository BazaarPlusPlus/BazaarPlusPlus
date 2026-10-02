using System.Reflection;
using Xunit;

namespace RuntimeIntegration.Tests;

public sealed class NativeCardPreviewCompatibilityTests
{
    [Fact]
    public void Native_preview_setup_selects_the_instance_overload_in_the_installed_game()
    {
        // Load game metadata before initializing the mod's name-based type lookup.
        Assembly.Load("TheBazaarRuntime");
        var reflection = Assembly
            .Load("BazaarPlusPlus")
            .GetType("BazaarPlusPlus.GameInterop.CardPreview.NativeCardPreviewReflection", true)!;
        var field = reflection.GetField("SetUpMethod", BindingFlags.Static | BindingFlags.Public)!;

        var setup = Assert.IsAssignableFrom<MethodInfo>(field.GetValue(null));

        Assert.Equal(typeof(Task), setup.ReturnType);
        Assert.Equal(
            [
                "BazaarGameShared.Domain.Cards.TCardBase",
                "System.Boolean",
                "BazaarGameShared.Domain.Cards.TCardInstance",
                "System.Threading.CancellationToken",
            ],
            setup.GetParameters().Select(parameter => parameter.ParameterType.FullName)
        );
    }
}

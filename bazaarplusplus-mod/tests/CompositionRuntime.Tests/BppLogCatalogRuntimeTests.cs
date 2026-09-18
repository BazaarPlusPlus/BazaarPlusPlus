using System.Reflection;
using BazaarPlusPlus.Infrastructure.Logging;
using Xunit;

namespace BazaarPlusPlus.Tests.CompositionRuntime;

public sealed class BppLogCatalogRuntimeTests
{
    [Fact]
    public void Production_event_catalog_is_complete_and_valid()
    {
        var productionAssembly = typeof(BppLogEventCatalog).Assembly;
        var definitionFields = new List<(Type Type, FieldInfo Field)>();
        foreach (var type in BppLogEventCatalog.GetLoadableTypes(productionAssembly))
        {
            foreach (
                var field in type.GetFields(
                    BindingFlags.Static
                        | BindingFlags.Public
                        | BindingFlags.NonPublic
                        | BindingFlags.DeclaredOnly
                )
            )
            {
                try
                {
                    if (field.FieldType == typeof(BppLogEventDefinition))
                        definitionFields.Add((type, field));
                }
                catch (FileNotFoundException)
                {
                    // Some unrelated game-typed fields cannot load in this pure test process.
                }
            }
        }
        var unregistered = definitionFields
            .Where(entry =>
                entry.Type.GetCustomAttribute<BppLogEventSourceAttribute>(inherit: false) == null
            )
            .Select(entry => entry.Type.FullName + "." + entry.Field.Name)
            .ToArray();
        var invalidSources = BppLogEventCatalog
            .GetLoadableTypes(productionAssembly)
            .Where(type =>
                type.GetCustomAttribute<BppLogEventSourceAttribute>(inherit: false) != null
            )
            .Where(type =>
                !type.IsAbstract
                || !type.IsSealed
                || !definitionFields.Any(entry => entry.Type == type)
            )
            .Select(type => type.FullName)
            .ToArray();
        var catalog = BppLogEventCatalog.Discover(productionAssembly);

        Assert.Empty(unregistered);
        Assert.Empty(invalidSources);
        Assert.Equal(definitionFields.Count, catalog.Definitions.Count);
        Assert.NotEmpty(catalog.Definitions);
        Assert.True(
            catalog.Validate().IsValid,
            string.Join(Environment.NewLine, catalog.Validate().Violations)
        );
    }
}

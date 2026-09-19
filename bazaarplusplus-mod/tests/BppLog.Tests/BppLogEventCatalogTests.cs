using System.Reflection;
using BazaarPlusPlus.Infrastructure.Logging;
using Xunit;

namespace BazaarPlusPlus.Tests;

public sealed class BppLogEventCatalogTests
{
    [Fact]
    public void Discover_enumerates_registered_runtime_events()
    {
        var catalog = BppLogEventCatalog.Discover(typeof(BppLogEventCatalogTests).Assembly);

        Assert.Contains(BppLogRuntimeEvents.StormSuppressed, catalog.Definitions);
        Assert.True(catalog.Validate().IsValid);
    }

    [Fact]
    public void Validate_reports_duplicate_event_ids()
    {
        var first = Define("logging.catalog.duplicated");
        var second = Define("logging.catalog.duplicated");

        var result = BppLogEventCatalog.FromDefinitions(first, second).Validate();

        AssertViolation(result, BppLogCatalogViolationKind.DuplicateEventId);
    }

    [Fact]
    public void Validate_reports_an_undeclared_scope()
    {
        var constructor = typeof(BppLogFeatureScope).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(string), typeof(string)],
            modifiers: null
        );
        var undeclared = Assert.IsType<BppLogFeatureScope>(
            constructor?.Invoke(["Injected", "injected"])
        );

        var result = BppLogEventCatalog
            .FromDefinitions(Define("injected.catalog.rejected", scope: undeclared))
            .Validate();

        AssertViolation(result, BppLogCatalogViolationKind.UndeclaredScope);
    }

    [Fact]
    public void Validate_reports_an_event_prefix_owned_by_another_scope()
    {
        var result = BppLogEventCatalog
            .FromDefinitions(Define("screenshots.capture.failed"))
            .Validate();

        AssertViolation(result, BppLogCatalogViolationKind.EventPrefixMismatch);
    }

    [Fact]
    public void Validate_reports_fields_authored_out_of_order()
    {
        var result = BppLogEventCatalog
            .FromDefinitions(
                Define(
                    "logging.catalog.out_of_order",
                    fields: [Field(1, "second"), Field(0, "first")]
                )
            )
            .Validate();

        AssertViolation(result, BppLogCatalogViolationKind.FieldsOutOfOrder);
    }

    [Fact]
    public void Validate_reports_duplicate_field_names_and_orders()
    {
        var result = BppLogEventCatalog
            .FromDefinitions(
                Define(
                    "logging.catalog.duplicate_field",
                    fields: [Field(0, "same"), Field(0, "same")]
                )
            )
            .Validate();

        AssertViolation(result, BppLogCatalogViolationKind.DuplicateFieldName);
        AssertViolation(result, BppLogCatalogViolationKind.DuplicateFieldOrder);
    }

    [Theory]
    [InlineData(999, 0, (int)BppLogCatalogViolationKind.InvalidCorrelation)]
    [InlineData(0, 999, (int)BppLogCatalogViolationKind.InvalidCardinality)]
    public void Validate_reports_invalid_field_governance(
        int correlation,
        int cardinality,
        int expected
    )
    {
        var field = Field(
            0,
            "value",
            (BppLogCorrelationPolicy)correlation,
            (BppLogCardinality)cardinality
        );

        var result = BppLogEventCatalog
            .FromDefinitions(Define("logging.catalog.invalid_governance", fields: [field]))
            .Validate();

        AssertViolation(result, (BppLogCatalogViolationKind)expected);
    }

    [Fact]
    public void Validate_reports_storm_keys_that_are_not_declared_fields()
    {
        var declared = Field(0, "declared");
        var foreign = Field(0, "foreign");

        var result = BppLogEventCatalog
            .FromDefinitions(
                Define(
                    "logging.catalog.foreign_storm_key",
                    fields: [declared],
                    stormKeys: [foreign]
                )
            )
            .Validate();

        AssertViolation(result, BppLogCatalogViolationKind.StormKeyNotDeclared);
    }

    [Theory]
    [InlineData(0, 1, (int)BppLogCatalogViolationKind.StormKeyNotLowCardinality)]
    [InlineData(1, 0, (int)BppLogCatalogViolationKind.StormKeyCorrelated)]
    public void Validate_reports_unsafe_storm_key_governance(
        int correlation,
        int cardinality,
        int expected
    )
    {
        var key = Field(
            0,
            "key",
            (BppLogCorrelationPolicy)correlation,
            (BppLogCardinality)cardinality
        );

        var result = BppLogEventCatalog
            .FromDefinitions(
                Define("logging.catalog.unsafe_storm_key", fields: [key], stormKeys: [key])
            )
            .Validate();

        AssertViolation(result, (BppLogCatalogViolationKind)expected);
    }

    [Fact]
    public void Validate_reports_duplicate_storm_keys()
    {
        var key = Field(0, "key");

        var result = BppLogEventCatalog
            .FromDefinitions(
                Define("logging.catalog.duplicate_storm_key", fields: [key], stormKeys: [key, key])
            )
            .Validate();

        AssertViolation(result, BppLogCatalogViolationKind.DuplicateStormKey);
    }

    [Fact]
    public void Declared_scopes_have_unique_render_and_event_prefixes()
    {
        Assert.NotEmpty(BppLogFeatureScope.All);
        Assert.Equal(
            BppLogFeatureScope.All.Count,
            BppLogFeatureScope.All.Select(scope => scope.PrefixName).Distinct().Count()
        );
        Assert.Equal(
            BppLogFeatureScope.All.Count,
            BppLogFeatureScope.All.Select(scope => scope.EventIdPrefix).Distinct().Count()
        );
        Assert.All(
            BppLogFeatureScope.All,
            scope => Assert.True(BppLogFeatureScope.IsDeclared(scope))
        );
    }

    private static void AssertViolation(
        BppLogCatalogValidationResult result,
        BppLogCatalogViolationKind expected
    ) => Assert.Contains(result.Violations, violation => violation.Kind == expected);

    private static BppLogEventDefinition Define(
        string eventId,
        BppLogFeatureScope? scope = null,
        BppLogFieldDefinition[]? fields = null,
        BppLogFieldDefinition[]? stormKeys = null
    ) =>
        new(
            scope ?? BppLogFeatureScope.Logger,
            eventId,
            fields ?? [],
            stormKeys == null ? null : new BppLogStormPolicy(stormKeys)
        );

    private static BppLogFieldDefinition Field(
        int order,
        string name,
        BppLogCorrelationPolicy correlation = BppLogCorrelationPolicy.None,
        BppLogCardinality cardinality = BppLogCardinality.Low
    ) => new(order, name, correlation, cardinality);
}

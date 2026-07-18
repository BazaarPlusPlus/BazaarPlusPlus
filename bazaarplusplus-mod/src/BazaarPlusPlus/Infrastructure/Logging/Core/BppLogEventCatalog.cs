#nullable enable
using System.Reflection;

namespace BazaarPlusPlus.Infrastructure.Logging;

/// <summary>
/// Marks a feature-local static <c>*LogEvents</c> class for catalog discovery. Event definitions
/// remain beside their owner; the catalog discovers marked sources instead of maintaining a
/// central list of every event.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
internal sealed class BppLogEventSourceAttribute : Attribute { }

internal enum BppLogCatalogViolationKind
{
    InvalidDefinition,
    DuplicateEventId,
    UndeclaredScope,
    InvalidEventId,
    EventPrefixMismatch,
    TooManyFields,
    FieldsOutOfOrder,
    DuplicateFieldOrder,
    DuplicateFieldName,
    InvalidFieldName,
    InvalidPrivacy,
    InvalidCorrelation,
    InvalidCardinality,
    StormKeyNotDeclared,
    DuplicateStormKey,
    StormKeyNotLowCardinality,
    StormKeyCorrelated,
    StormKeySensitive,
}

internal sealed class BppLogCatalogViolation
{
    internal BppLogCatalogViolation(
        BppLogCatalogViolationKind kind,
        string? eventId = null,
        string? fieldName = null
    )
    {
        Kind = kind;
        EventId = eventId;
        FieldName = fieldName;
    }

    internal BppLogCatalogViolationKind Kind { get; }

    internal string? EventId { get; }

    internal string? FieldName { get; }

    public override string ToString()
    {
        var text = Kind.ToString();
        if (!string.IsNullOrEmpty(EventId))
            text += " event=" + EventId;
        if (!string.IsNullOrEmpty(FieldName))
            text += " field=" + FieldName;
        return text;
    }
}

internal sealed class BppLogCatalogValidationResult
{
    private readonly BppLogCatalogViolation[] _violations;

    internal BppLogCatalogValidationResult(IReadOnlyList<BppLogCatalogViolation> violations)
    {
        _violations = new BppLogCatalogViolation[violations.Count];
        for (var index = 0; index < violations.Count; index++)
            _violations[index] = violations[index];
    }

    internal bool IsValid => _violations.Length == 0;

    internal IReadOnlyList<BppLogCatalogViolation> Violations => _violations;
}

internal sealed class BppLogEventCatalog
{
    private readonly BppLogEventDefinition[] _definitions;

    private BppLogEventCatalog(IReadOnlyList<BppLogEventDefinition> definitions)
    {
        _definitions = new BppLogEventDefinition[definitions.Count];
        for (var index = 0; index < definitions.Count; index++)
            _definitions[index] = definitions[index];
    }

    internal IReadOnlyList<BppLogEventDefinition> Definitions => _definitions;

    internal static BppLogEventCatalog FromDefinitions(
        params BppLogEventDefinition[] definitions
    ) => new(definitions ?? Array.Empty<BppLogEventDefinition>());

    internal static BppLogEventCatalog Discover(Assembly assembly)
    {
        if (assembly == null)
            throw new ArgumentNullException(nameof(assembly));

        var definitions = new List<BppLogEventDefinition>();
        var sourceTypes = new List<Type>();
        foreach (var type in GetLoadableTypes(assembly))
        {
            if (type.GetCustomAttribute<BppLogEventSourceAttribute>(inherit: false) != null)
                sourceTypes.Add(type);
        }
        sourceTypes.Sort(
            (left, right) =>
                string.CompareOrdinal(left.FullName ?? left.Name, right.FullName ?? right.Name)
        );

        for (var typeIndex = 0; typeIndex < sourceTypes.Count; typeIndex++)
        {
            var fields = sourceTypes[typeIndex]
                .GetFields(
                    BindingFlags.Static
                        | BindingFlags.Public
                        | BindingFlags.NonPublic
                        | BindingFlags.DeclaredOnly
                );
            Array.Sort(fields, (left, right) => left.MetadataToken.CompareTo(right.MetadataToken));
            for (var fieldIndex = 0; fieldIndex < fields.Length; fieldIndex++)
            {
                var field = fields[fieldIndex];
                if (field.FieldType != typeof(BppLogEventDefinition))
                    continue;
                if (field.GetValue(null) is BppLogEventDefinition definition)
                    definitions.Add(definition);
            }
        }

        return new BppLogEventCatalog(definitions);
    }

    internal static IReadOnlyList<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            var types = new List<Type>();
            for (var index = 0; index < exception.Types.Length; index++)
            {
                if (exception.Types[index] is Type type)
                    types.Add(type);
            }
            return types;
        }
    }

    internal BppLogCatalogValidationResult Validate()
    {
        var violations = new List<BppLogCatalogViolation>();
        var eventIds = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < _definitions.Length; index++)
        {
            var definition = _definitions[index];
            if (definition == null)
            {
                violations.Add(
                    new BppLogCatalogViolation(BppLogCatalogViolationKind.InvalidDefinition)
                );
                continue;
            }

            ValidateDefinition(definition, eventIds, violations);
        }
        return new BppLogCatalogValidationResult(violations);
    }

    private static void ValidateDefinition(
        BppLogEventDefinition definition,
        HashSet<string> eventIds,
        List<BppLogCatalogViolation> violations
    )
    {
        var eventId = definition.EventId;
        if (!eventIds.Add(eventId ?? string.Empty))
            Add(violations, BppLogCatalogViolationKind.DuplicateEventId, eventId);

        if (!BppLogFeatureScope.IsDeclared(definition.Scope))
        {
            Add(violations, BppLogCatalogViolationKind.UndeclaredScope, eventId);
        }
        else if (
            string.IsNullOrEmpty(eventId)
            || !eventId.StartsWith(definition.Scope.EventIdPrefix + ".", StringComparison.Ordinal)
        )
        {
            Add(violations, BppLogCatalogViolationKind.EventPrefixMismatch, eventId);
        }

        if (!BppLogSchemaRules.IsDottedSnakeIdentifier(eventId, minimumSegments: 3))
            Add(violations, BppLogCatalogViolationKind.InvalidEventId, eventId);

        ValidateFields(definition, violations);
        ValidateStormPolicy(definition, violations);
    }

    private static void ValidateFields(
        BppLogEventDefinition definition,
        List<BppLogCatalogViolation> violations
    )
    {
        if (definition.Fields.Count > BppLogSchemaRules.MaximumFields)
            Add(violations, BppLogCatalogViolationKind.TooManyFields, definition.EventId);

        var names = new HashSet<string>(StringComparer.Ordinal);
        var orders = new HashSet<int>();
        var previousOrder = -1;
        for (var index = 0; index < definition.Fields.Count; index++)
        {
            var field = definition.Fields[index];
            if (field == null)
            {
                Add(violations, BppLogCatalogViolationKind.InvalidDefinition, definition.EventId);
                continue;
            }

            if (field.Order < 0 || (index > 0 && field.Order <= previousOrder))
                Add(
                    violations,
                    BppLogCatalogViolationKind.FieldsOutOfOrder,
                    definition.EventId,
                    field.Name
                );
            previousOrder = field.Order;

            if (!orders.Add(field.Order))
                Add(
                    violations,
                    BppLogCatalogViolationKind.DuplicateFieldOrder,
                    definition.EventId,
                    field.Name
                );
            if (!names.Add(field.Name ?? string.Empty))
                Add(
                    violations,
                    BppLogCatalogViolationKind.DuplicateFieldName,
                    definition.EventId,
                    field.Name
                );
            if (!BppLogSchemaRules.IsSnakeIdentifier(field.Name))
                Add(
                    violations,
                    BppLogCatalogViolationKind.InvalidFieldName,
                    definition.EventId,
                    field.Name
                );
            if (!BppLogSchemaRules.IsKnownPrivacy(field.Privacy))
                Add(
                    violations,
                    BppLogCatalogViolationKind.InvalidPrivacy,
                    definition.EventId,
                    field.Name
                );
            if (!BppLogSchemaRules.IsKnownCorrelation(field.Correlation))
                Add(
                    violations,
                    BppLogCatalogViolationKind.InvalidCorrelation,
                    definition.EventId,
                    field.Name
                );
            if (!BppLogSchemaRules.IsKnownCardinality(field.Cardinality))
                Add(
                    violations,
                    BppLogCatalogViolationKind.InvalidCardinality,
                    definition.EventId,
                    field.Name
                );
        }
    }

    private static void ValidateStormPolicy(
        BppLogEventDefinition definition,
        List<BppLogCatalogViolation> violations
    )
    {
        var keyFields = definition.StormPolicy?.KeyFields;
        if (keyFields == null)
            return;

        var seen = new HashSet<BppLogFieldDefinition>();
        for (var index = 0; index < keyFields.Count; index++)
        {
            var key = keyFields[index];
            if (key == null || !ContainsField(definition, key))
            {
                Add(
                    violations,
                    BppLogCatalogViolationKind.StormKeyNotDeclared,
                    definition.EventId,
                    key?.Name
                );
                continue;
            }
            if (!seen.Add(key))
                Add(
                    violations,
                    BppLogCatalogViolationKind.DuplicateStormKey,
                    definition.EventId,
                    key.Name
                );
            if (key.Cardinality != BppLogCardinality.Low)
                Add(
                    violations,
                    BppLogCatalogViolationKind.StormKeyNotLowCardinality,
                    definition.EventId,
                    key.Name
                );
            if (key.Correlation != BppLogCorrelationPolicy.None)
                Add(
                    violations,
                    BppLogCatalogViolationKind.StormKeyCorrelated,
                    definition.EventId,
                    key.Name
                );
            if (key.Privacy == BppLogFieldPrivacy.Sensitive)
                Add(
                    violations,
                    BppLogCatalogViolationKind.StormKeySensitive,
                    definition.EventId,
                    key.Name
                );
        }
    }

    private static bool ContainsField(
        BppLogEventDefinition definition,
        BppLogFieldDefinition candidate
    )
    {
        for (var index = 0; index < definition.Fields.Count; index++)
        {
            if (ReferenceEquals(definition.Fields[index], candidate))
                return true;
        }
        return false;
    }

    private static void Add(
        List<BppLogCatalogViolation> violations,
        BppLogCatalogViolationKind kind,
        string? eventId = null,
        string? fieldName = null
    ) => violations.Add(new BppLogCatalogViolation(kind, eventId, fieldName));
}

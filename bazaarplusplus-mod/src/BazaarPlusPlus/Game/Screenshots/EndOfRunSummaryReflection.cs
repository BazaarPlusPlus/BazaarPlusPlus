#nullable enable
using System.Reflection;

namespace BazaarPlusPlus.Game.Screenshots;

/// <summary>Reflection over the end-of-run summary's private members, shared by the per-frame
/// reveal detector and the visual snapshot sampler. Lookups are keyed by the runtime type, so
/// they bind no game type and cache misses as well as hits.</summary>
internal static class EndOfRunSummaryReflection
{
    private const string AnimatorMemberName = "Animator";
    private static readonly object AnimatorAccessorGate = new();
    private static readonly Dictionary<Type, AnimatorAccessor?> AnimatorAccessors = new();

    internal static AnimatorAccessor? GetAnimatorAccessor(Type type)
    {
        lock (AnimatorAccessorGate)
        {
            if (AnimatorAccessors.TryGetValue(type, out var cached))
                return cached;

            var property = type.GetProperty(
                AnimatorMemberName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );
            AnimatorAccessor? accessor = property == null ? null : new AnimatorAccessor(property);
            if (accessor == null)
            {
                var field = type.GetField(
                    AnimatorMemberName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                );
                if (field != null)
                    accessor = new AnimatorAccessor(field);
            }
            AnimatorAccessors[type] = accessor;
            return accessor;
        }
    }

    internal static FieldInfo? FindField(Type type, string fieldName)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            var field = current.GetField(
                fieldName,
                BindingFlags.Instance
                    | BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.DeclaredOnly
            );
            if (field != null)
                return field;
        }
        return null;
    }

    internal sealed class AnimatorAccessor
    {
        private readonly PropertyInfo? _property;
        private readonly FieldInfo? _field;

        internal AnimatorAccessor(PropertyInfo property) => _property = property;

        internal AnimatorAccessor(FieldInfo field) => _field = field;

        internal object? GetValue(object instance) =>
            _property != null ? _property.GetValue(instance) : _field?.GetValue(instance);
    }
}

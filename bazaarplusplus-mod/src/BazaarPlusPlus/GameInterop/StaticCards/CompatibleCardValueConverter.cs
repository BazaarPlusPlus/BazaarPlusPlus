#nullable enable
using System.Reflection;
using BazaarGameShared.Domain.Values;
using BazaarGameShared.Domain.Values.ReferenceValues;
using BazaarGameShared.Infra.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BazaarPlusPlus.GameInterop.StaticCards;

internal sealed class CompatibleCardValueConverter : JsonConverter
{
    internal const string UniqueCardCountType = "TReferenceValueUniqueCardCount";
    private const string NativeTypeMappingFieldName = "TypeMapping";

    // Read only after a native converter instance exists: its constructor proves the native
    // static constructor already scanned the loaded assemblies into the discriminator table.
    private static readonly Lazy<bool> NativeKnowsUniqueCardCount = new(
        ReadNativeKnowsUniqueCardCount
    );
    private readonly JsonConverter _native;

    internal CompatibleCardValueConverter(JsonConverter native) => _native = native;

    /// <summary>
    /// Inserts the adapter ahead of the native converter only when the client lacks
    /// <see cref="UniqueCardCountType"/>: native <c>ReadJson</c> resolves a discriminator solely
    /// through its private <c>TypeMapping</c>, so a client that maps it never reaches the
    /// fallback, and the adapter would only buffer every value into a <c>JObject</c>.
    /// </summary>
    internal static void Install(JsonSerializer serializer)
    {
        if (serializer.Converters.Any(converter => converter is CompatibleCardValueConverter))
            return;
        var native = serializer.Converters.OfType<BazaarJsonDerivedTypeConverter>().Single();
        if (NativeKnowsUniqueCardCount.Value)
            return;
        serializer.Converters.Insert(0, new CompatibleCardValueConverter(native));
    }

    // Any reflection miss keeps the adapter, which defers to native support at read time.
    private static bool ReadNativeKnowsUniqueCardCount()
    {
        try
        {
            return typeof(BazaarJsonDerivedTypeConverter)
                    .GetField(
                        NativeTypeMappingFieldName,
                        BindingFlags.NonPublic | BindingFlags.Static
                    )
                    ?.GetValue(null)
                    is IDictionary<string, Type> mapping
                && mapping.ContainsKey(UniqueCardCountType);
        }
        catch
        {
            return false;
        }
    }

    public override bool CanConvert(Type objectType) =>
        typeof(ITValue).IsAssignableFrom(objectType);

    public override object? ReadJson(
        JsonReader reader,
        Type objectType,
        object? existingValue,
        JsonSerializer serializer
    )
    {
        if (reader.TokenType == JsonToken.Null)
            return null;

        var data = JObject.Load(reader);
        try
        {
            // Prefer the native implementation when the installed client knows this type.
            using var nativeReader = data.CreateReader();
            return _native.ReadJson(nativeReader, objectType, existingValue, serializer);
        }
        catch (JsonSerializationException error)
            when ((string?)data["$type"] == UniqueCardCountType
                && error.Message
                    == "Unknown type or missing type information: " + UniqueCardCountType
                && objectType.IsAssignableFrom(CompatibleUniqueCardCount.RuntimeType)
            )
        {
            data.Remove("$type");
            var value = CompatibleUniqueCardCount.Create();
            using var compatibilityReader = data.CreateReader();
            serializer.Populate(compatibilityReader, value);
            return value;
        }
    }

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        if (
            value is not TReferenceValueWithTargetCard count
            || !CompatibleUniqueCardCount.IsInstance(count)
        )
        {
            _native.WriteJson(writer, value, serializer);
            return;
        }

        writer.WriteStartObject();
        writer.WritePropertyName("$type");
        writer.WriteValue(UniqueCardCountType);
        writer.WritePropertyName(nameof(count.Target));
        serializer.Serialize(writer, count.Target);
        writer.WritePropertyName(nameof(count.DefaultValue));
        writer.WriteValue(count.DefaultValue);
        writer.WritePropertyName(nameof(count.Modifier));
        serializer.Serialize(writer, count.Modifier);
        writer.WriteEndObject();
    }
}

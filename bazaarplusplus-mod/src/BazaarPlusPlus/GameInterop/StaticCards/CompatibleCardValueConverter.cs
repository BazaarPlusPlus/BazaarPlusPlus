#nullable enable
using BazaarGameShared.Domain.Values;
using BazaarGameShared.Domain.Values.ReferenceValues;
using BazaarGameShared.Infra.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BazaarPlusPlus.GameInterop.StaticCards;

internal sealed class CompatibleCardValueConverter : JsonConverter
{
    internal const string UniqueCardCountType = "TReferenceValueUniqueCardCount";
    private readonly JsonConverter _native;

    internal CompatibleCardValueConverter(JsonConverter native) => _native = native;

    internal static void Install(JsonSerializer serializer)
    {
        if (serializer.Converters.Any(converter => converter is CompatibleCardValueConverter))
            return;
        var native = serializer.Converters.OfType<BazaarJsonDerivedTypeConverter>().Single();
        serializer.Converters.Insert(0, new CompatibleCardValueConverter(native));
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

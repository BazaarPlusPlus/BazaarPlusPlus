using System.Reflection;
using System.Reflection.Metadata;
using BazaarPlusPlus.GameAssemblyCompatibility;
using Newtonsoft.Json;
using Xunit;

namespace GameAssemblyCompatibility.Tests;

public sealed class AttributeEnumTests
{
    [Fact]
    public void Compatible_attribute_enums_resolve_in_the_consumer_library_and_core_assemblies()
    {
        // Reading these attributes succeeds in the CLR; their boxed enum values require
        // decoding metadata from four different assemblies, not only Newtonsoft.
        Assert.Equal(
            LocalMode.Compact,
            typeof(Local).GetCustomAttribute<JsonConverterAttribute>()!.ConverterParameters![0]
        );
        Assert.Equal(
            DayOfWeek.Monday,
            typeof(Core).GetCustomAttribute<JsonConverterAttribute>()!.ConverterParameters![0]
        );
        Assert.Equal(
            Formatting.None,
            typeof(Library).GetCustomAttribute<JsonConverterAttribute>()!.ConverterParameters![0]
        );
        Assert.Equal(
            PrimitiveTypeCode.Int32,
            typeof(External).GetCustomAttribute<JsonConverterAttribute>()!.ConverterParameters![0]
        );
        var result = MemberCompatibility.Check(
            typeof(JsonConverterAttribute).Assembly.Location,
            [typeof(AttributeEnumTests).Assembly.Location]
        );
        Assert.Empty(result.Failures);
        Assert.True(result.Members > 0);
    }

    private enum LocalMode
    {
        Compact,
    }

    [JsonConverter(typeof(ModeConverter), LocalMode.Compact)]
    private sealed class Local;

    [JsonConverter(typeof(ModeConverter), DayOfWeek.Monday)]
    private sealed class Core;

    [JsonConverter(typeof(ModeConverter), Formatting.None)]
    private sealed class Library;

    [JsonConverter(typeof(ModeConverter), PrimitiveTypeCode.Int32)]
    private sealed class External;

    private sealed class ModeConverter(object mode) : JsonConverter
    {
        public override bool CanConvert(Type objectType) => false;

        public override object? ReadJson(
            JsonReader reader,
            Type objectType,
            object? existingValue,
            JsonSerializer serializer
        ) => mode;

        public override void WriteJson(
            JsonWriter writer,
            object? value,
            JsonSerializer serializer
        ) => writer.WriteValue(mode.ToString());
    }
}

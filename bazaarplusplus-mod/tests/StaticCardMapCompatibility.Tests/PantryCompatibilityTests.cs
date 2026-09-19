using System.Reflection;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Item;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Effect.AuraActions;
using BazaarGameShared.Domain.Players;
using BazaarGameShared.Domain.Prerequisites.Conditionals;
using BazaarGameShared.Domain.Values;
using BazaarGameShared.Domain.Values.ReferenceValues;
using BazaarGameShared.Infra.Serialization;
using BazaarPlusPlus.GameInterop.StaticCards;
using BazaarPlusPlus.TestSupport;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal static class PantryCompatibilityTests
{
    internal static void Run()
    {
        var json = TestInputs.Scratch(Path.Combine(AppContext.BaseDirectory, "Pantry.json"));
        var nativeAlreadySupportsPantry = NativeSupports(json);
        var map = CompatibleCardMapReader.ReadRows([json], out var omitted);
        Check(omitted == 0 && map.Count == 1, "Pantry must be retained in the catalog.");
        var pantry = (TCardItem)map.Values.Single();

        var serializer = JsonSerializer.Create(new BazaarJsonSerializerSettings());
        CompatibleCardValueConverter.Install(serializer);
        CompatibleCardValueConverter.Install(serializer);
        Check(
            serializer.Converters.OfType<CompatibleCardValueConverter>().Count() == 1,
            "Repeated native serializer getter calls must not stack converters."
        );

        var stash = new List<ICard> { Card(1, true), Card(1, true), Card(2, true), Card(3, false) };
        var inventory = NativeProxy.Create<IPlayerInventory>(
            (method, _) =>
                method == "GetItemsAsEnumerable" ? stash : throw new NotSupportedException(method)
        );
        var owner = NativeProxy.Create<IPlayer>(
            (method, _) =>
                method == "get_Stash" ? inventory : throw new NotSupportedException(method)
        );

        foreach (var tier in new[] { ETier.Bronze, ETier.Silver, ETier.Gold, ETier.Diamond })
        {
            var subject = NativeProxy.Create<ICard>(
                (method, args) =>
                    method switch
                    {
                        "get_Owner" => owner,
                        "get_State" => ECardState.Alive,
                        "GetAttributeValue" => pantry.GetAttributeBaseValueAtTier(
                            (ECardAttributeType)args![0]!,
                            tier
                        ),
                        _ => throw new NotSupportedException(method),
                    }
            );
            var context = new ValueContext(null!, subject);
            var values = pantry
                .Auras.Values.Concat(
                    pantry.Enchantments!.Values.SelectMany(enchantment => enchantment.Auras.Values)
                )
                .Select(aura => ((TAuraActionCardModifyAttribute)aura.Action).Value)
                .ToArray();
            Check(
                values.Length == 5,
                "All base and enchanted Pantry references must be exercised."
            );
            Check(
                values.All(value =>
                    (value.GetType().Assembly == typeof(ITValue).Assembly)
                    == nativeAlreadySupportsPantry
                ),
                "Use native counts when available and compatibility counts only when missing."
            );
            foreach (
                var value in values.SelectMany(value =>
                    new[]
                    {
                        value,
                        ReadFallback(
                            serializer,
                            JObject.FromObject(value, serializer).ToString(Formatting.None)
                        ),
                    }
                )
            )
            {
                Check(
                    value.GetRawValue(context) == 2,
                    "Count food template IDs, excluding duplicates and non-food."
                );
                var modifier = value.GetModifier(context)!;
                var expected = 2 * modifier.Value.GetValue(context);
                Check(value.GetValue(context) == expected, "Preserve native tier multipliers.");

                using var output = new StringWriter();
                serializer.Serialize(output, value);
                var encoded = output.ToString();
                Check(
                    (string?)JObject.Parse(encoded)["$type"]
                        == CompatibleCardValueConverter.UniqueCardCountType,
                    "Round trips must retain the native discriminator."
                );
                using var input = new JsonTextReader(new StringReader(encoded));
                Check(
                    serializer.Deserialize<ITValue>(input)!.GetValue(context) == expected,
                    "Round trips must preserve target, modifier, and distinct counting."
                );
            }
        }

        stash.Clear();
        var emptySubject = NativeProxy.Create<ICard>(
            (method, _) =>
                method switch
                {
                    "get_Owner" => owner,
                    "get_State" => ECardState.Alive,
                    "GetAttributeValue" => 4,
                    _ => throw new NotSupportedException(method),
                }
        );
        Check(
            ((TAuraActionCardModifyAttribute)pantry.Auras["2"].Action).Value.GetValue(
                new ValueContext(null!, emptySubject)
            ) == 0,
            "Empty stash must keep the native zero default."
        );

        var nativeValue = new TFixedValue { Value = 123 };
        var futureNative = new ReturningConverter(nativeValue);
        var adapter = new CompatibleCardValueConverter(futureNative);
        using var futureReader = new JsonTextReader(
            new StringReader("{\"$type\":\"TReferenceValueUniqueCardCount\"}")
        );
        Check(
            ReferenceEquals(
                nativeValue,
                adapter.ReadJson(futureReader, typeof(ITValue), null, serializer)
            ),
            "Future native support must take precedence over the compatibility implementation."
        );
        Check(
            NativeSupports(json) == nativeAlreadySupportsPantry,
            "A card serializer adapter must not mutate the global native type registry."
        );
        VerifyNativeTargetAndRecordBehavior(serializer);
        Console.WriteLine(
            "PASS: Pantry is retained; all five references preserve unique food counting, tiers, empty stash, and round trips."
        );
    }

    private static TReferenceValueWithTargetCard ReadFallback(
        JsonSerializer serializer,
        string json
    )
    {
        var converter = new CompatibleCardValueConverter(new MissingUniqueCountConverter());
        using var reader = new JsonTextReader(new StringReader(json));
        return (TReferenceValueWithTargetCard)
            converter.ReadJson(reader, typeof(TReferenceValueWithTargetCard), null, serializer)!;
    }

    private static void VerifyNativeTargetAndRecordBehavior(JsonSerializer serializer)
    {
        var value = ReadFallback(
            serializer,
            "{\"$type\":\"TReferenceValueUniqueCardCount\",\"DefaultValue\":7}"
        );
        var context = new ValueContext(null!);
        Check(
            value.GetRawValue(context) == 7 && value.GetValue(context) == 7,
            "A missing target must preserve the configured native default."
        );
        var clone = value with { DefaultValue = 9 };
        Check(
            clone.GetType() == value.GetType()
                && value.DefaultValue == 7
                && clone.DefaultValue == 9,
            "Native record cloning must preserve the compatibility type and isolate changes."
        );
        Check(
            value.Equals(value with { }) && !value.Equals(clone),
            "Native record equality must preserve inherited properties."
        );
        Check(
            !value.Equals(new TReferenceValueCardCount { DefaultValue = 7 }),
            "Compatibility records must remain distinct from native count records."
        );

        var card = NativeProxy.Create<ICard>(
            (method, _) =>
                method switch
                {
                    "get_TemplateId" => Guid.Empty,
                    "GetAttributeValue" => 1,
                    _ => throw new NotSupportedException(method),
                }
        );
        var conditional = new TCardConditionalAttribute
        {
            ComparisonOperator = EComparisonOperator.Equal,
            ComparisonValue = value,
        };
        Check(
            conditional.IsSatisfiedBy(card, new CardConditionalContext(null!, [card], null, null)),
            "Native null-target conditionals must count the explicit card instead of using DefaultValue."
        );
    }

    private sealed class MissingUniqueCountConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) => true;

        public override object? ReadJson(
            JsonReader reader,
            Type objectType,
            object? existingValue,
            JsonSerializer serializer
        ) =>
            throw new JsonSerializationException(
                "Unknown type or missing type information: "
                    + CompatibleCardValueConverter.UniqueCardCountType
            );

        public override void WriteJson(
            JsonWriter writer,
            object? value,
            JsonSerializer serializer
        ) => throw new NotSupportedException();
    }

    private static ICard Card(int identity, bool food) =>
        NativeProxy.Create<ICard>(
            (method, _) =>
                method switch
                {
                    "get_TemplateId" => Guid.Parse($"00000000-0000-0000-0000-{identity:D12}"),
                    "get_Tags" => food
                        ? new HashSet<ECardTag> { ECardTag.Food }
                        : new HashSet<ECardTag>(),
                    "get_LeftSocketId" => null,
                    "get_State" => ECardState.Alive,
                    _ => throw new NotSupportedException(method),
                }
        );

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }

    private static bool NativeSupports(string json)
    {
        try
        {
            _ = JsonConvert.DeserializeObject<ITCard>(json, new BazaarJsonSerializerSettings());
            return true;
        }
        catch (JsonSerializationException error)
            when (error.Message
                == "Unknown type or missing type information: "
                    + CompatibleCardValueConverter.UniqueCardCountType
            )
        {
            return false;
        }
    }

    private sealed class ReturningConverter(object value) : JsonConverter
    {
        public override bool CanConvert(Type objectType) => true;

        public override object ReadJson(
            JsonReader reader,
            Type objectType,
            object? existingValue,
            JsonSerializer serializer
        ) => value;

        public override void WriteJson(
            JsonWriter writer,
            object? item,
            JsonSerializer serializer
        ) => throw new NotSupportedException();
    }
}

public class NativeProxy : DispatchProxy
{
    private Func<string, object?[]?, object?> _invoke = null!;

    internal static T Create<T>(Func<string, object?[]?, object?> invoke)
        where T : class
    {
        var value = Create<T, NativeProxy>();
        ((NativeProxy)(object)value)._invoke = invoke;
        return value;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
        _invoke(targetMethod!.Name, args);
}

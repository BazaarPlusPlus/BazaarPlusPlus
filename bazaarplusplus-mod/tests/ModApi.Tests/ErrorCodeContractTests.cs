using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using BazaarPlusPlus.ModApi.Clients;
using BazaarPlusPlus.ModApi.Http;

internal static class ErrorCodeContractTests
{
    public static void Run()
    {
        using var stream =
            Assembly
                .GetExecutingAssembly()
                .GetManifestResourceStream("ModApi.Tests.mod-api-errors.json")
            ?? throw new InvalidOperationException("The server-owned error contract is missing.");
        using var contract = JsonDocument.Parse(stream);
        var declared = contract
            .RootElement.GetProperty("codes")
            .EnumerateArray()
            .Select(row => row.GetProperty("code").GetString()!)
            .ToHashSet(StringComparer.Ordinal);

        // Inspect the same compiled classifier used by uploads, rather than duplicating its
        // code strings in the test. BazaarDbLinkClient talks to the partner API, not this server.
        var classifier =
            typeof(BundleUploadClient).GetMethod(
                "Classify",
                BindingFlags.NonPublic | BindingFlags.Static
            ) ?? throw new InvalidOperationException("The upload classifier is missing.");
        var instructions = ReadInstructions(classifier)
            .Where(item => item.Code != OpCodes.Nop)
            .ToArray();
        var comparisons = 0;
        for (var index = 0; index < instructions.Length; index++)
        {
            if (
                instructions[index].Operand is not MethodInfo getter
                || getter.DeclaringType != typeof(ModApiErrorEnvelope)
                || getter.Name != "get_Code"
            )
                continue;

            // Fail closed if the compiler or classifier changes its comparison shape: a new
            // code read must never silently escape this contract check.
            if (
                index + 2 >= instructions.Length
                || instructions[index + 1].Code != OpCodes.Ldstr
                || instructions[index + 1].Operand is not string code
                || instructions[index + 2].Operand is not MethodInfo comparison
                || comparison.DeclaringType != typeof(string)
                || comparison.Name != "op_Equality"
            )
                throw new InvalidOperationException(
                    "Unrecognized server error-code comparison in Classify."
                );

            if (!declared.Contains(code))
                throw new InvalidOperationException(
                    $"Mod branches on undeclared server error code '{code}'."
                );
            comparisons++;
        }
        if (comparisons == 0)
            throw new InvalidOperationException(
                "No compiled server error-code comparisons were checked."
            );
        Console.WriteLine($"ErrorCodeContractTests passed ({comparisons} compiled comparisons).");
    }

    private static IEnumerable<(OpCode Code, object? Operand)> ReadInstructions(MethodInfo method)
    {
        var opcodes = typeof(OpCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(OpCode))
            .Select(field => (OpCode)field.GetValue(null)!)
            .ToDictionary(code => unchecked((ushort)code.Value));
        var bytes =
            method.GetMethodBody()?.GetILAsByteArray()
            ?? throw new InvalidOperationException("The upload classifier has no IL body.");
        for (var offset = 0; offset < bytes.Length; )
        {
            var value = (ushort)bytes[offset++];
            if (value == 0xfe)
                value = (ushort)(0xfe00 | bytes[offset++]);
            var opcode = opcodes[value];
            object? operand = opcode.OperandType switch
            {
                OperandType.InlineString => method.Module.ResolveString(
                    BitConverter.ToInt32(bytes, offset)
                ),
                OperandType.InlineMethod => method.Module.ResolveMethod(
                    BitConverter.ToInt32(bytes, offset)
                ),
                _ => null,
            };
            offset += opcode.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget
                or OperandType.ShortInlineI
                or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(bytes, offset),
                _ => 4,
            };
            yield return (opcode, operand);
        }
    }
}

#nullable enable
using System.Reflection;
using System.Reflection.Emit;
using BazaarPlusPlus.Game.CollectionPanel.Tooltips;
using HarmonyLib;
using TheBazaar.Tooltips;
using TheBazaar.UI.Tooltips;

namespace BazaarPlusPlus.Patches.CollectionPanel;

[HarmonyPatch]
internal static class CollectionLockVariantPanelPatch
{
    // Older clients do not ship the variant panel. Keep this optional without taking
    // a compile-time dependency on its state, options, or panel types.
    [HarmonyTargetMethods]
    private static IEnumerable<MethodBase> TargetMethods()
    {
        var method = AccessTools.Method(typeof(CardTooltipController), "SetupLockVariantPanel");
        if (method != null)
            yield return method;
    }

    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions
    )
    {
        var codes = instructions.ToList();
        var guards = codes
            .Select((code, index) => (code, index))
            .Where(entry =>
                entry.code.opcode == OpCodes.Isinst
                && entry.code.operand is Type { FullName: "TheBazaar.IUpgradeableCard" }
            )
            .Select(entry => entry.index)
            .ToArray();
        if (guards.Length != 1)
            throw new InvalidOperationException("Native lock variant eligibility guard changed.");

        // Broaden only eligibility. The native CardLockPreviewState accepts a null
        // CardController and owns preview copies, selection callbacks, and disposal.
        codes.InsertRange(
            guards[0] + 1,
            [
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(
                    OpCodes.Callvirt,
                    AccessTools.PropertyGetter(typeof(CardTooltipController), "CurrentTooltipData")
                ),
                new CodeInstruction(
                    OpCodes.Call,
                    AccessTools.Method(
                        typeof(CollectionLockVariantPanelPatch),
                        nameof(AllowCollection)
                    )
                ),
            ]
        );
        return codes;
    }

    internal static object? AllowCollection(object? upgradeableCard, ITooltipData? tooltipData) =>
        upgradeableCard
        ?? (
            tooltipData is CardTooltipData card
            && CollectionTierTooltipRegistry.Contains(card.CardInstance)
                ? card
                : null
        );
}

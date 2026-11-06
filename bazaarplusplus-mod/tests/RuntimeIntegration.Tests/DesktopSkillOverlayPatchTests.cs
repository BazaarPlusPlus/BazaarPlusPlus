using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;
using BazaarPlusPlus.Game.SkillDetails;
using BazaarPlusPlus.Patches.Input;
using HarmonyLib;
using Xunit;

namespace RuntimeIntegration.Tests;

public sealed class DesktopSkillOverlayPatchTests
{
    [Theory]
    [InlineData(
        "<size=145%><voffset=-4><sprite name=Crit><color=#ff0000>暴击率</color></size></voffset>",
        "<size=100%><voffset=-4><sprite name=Crit><color=#ff0000>暴击率</color></size></voffset>"
    )]
    [InlineData("<SIZE=72>Ammo</SIZE>", "<size=100%>Ammo</SIZE>")]
    [InlineData("附魔", "附魔")]
    public void Legend_heading_size_overrides_are_normalized_without_losing_native_art_or_color(
        string native,
        string expected
    )
    {
        var normalized = DesktopSkillOverlayLayout.NormalizeLegendTitle(native);
        Assert.Equal(expected, normalized);
        Assert.Equal(normalized, DesktopSkillOverlayLayout.NormalizeLegendTitle(normalized));
    }

    static DesktopSkillOverlayPatchTests()
    {
        var managed = typeof(DesktopSkillOverlayPatchTests)
            .Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "BppManagedPath")
            .Value!;
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            var path = Path.Combine(managed, name.Name + ".dll");
            return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
        };
    }

    [Fact]
    public void Native_skill_overlay_keeps_every_instruction_except_its_four_platform_guards()
    {
        var targets = DesktopSkillOverlayPlatformPatch.TargetMethods().ToArray();
        Assert.Equal(4, targets.Length);
        Assert.Contains(targets, method => method.Name == "Awake");
        Assert.Contains(targets, method => method.Name == "TryOpenOverlay");
        Assert.Contains(targets, method => method.Name == "MoveNext");
        Assert.Contains(targets, method => method.Name == "Show");

        foreach (var target in targets)
        {
            var generator = new DynamicMethod(
                "ReadNativeSkillOverlay",
                typeof(void),
                Type.EmptyTypes
            ).GetILGenerator();
            var original = PatchProcessor.GetOriginalInstructions(target, generator);
            var patched = DesktopSkillOverlayPlatformPatch.Transpiler(original).ToArray();
            Assert.Equal(original.Count, patched.Length);
            var changed = 0;
            for (var i = 0; i < original.Count; i++)
            {
                Assert.Equal(original[i].labels, patched[i].labels);
                Assert.Equal(original[i].blocks, patched[i].blocks);
                if (original[i].operand is MethodInfo { Name: "IsMobilePlatform" })
                {
                    Assert.Equal(OpCodes.Ldc_I4_1, patched[i].opcode);
                    Assert.Null(patched[i].operand);
                    changed++;
                }
                else
                {
                    Assert.Equal(original[i].opcode, patched[i].opcode);
                    Assert.Equal(original[i].operand, patched[i].operand);
                }
            }
            Assert.Equal(1, changed);
        }
    }

    [Fact]
    public void Dismissing_a_tooltip_does_not_block_the_same_click_while_its_fade_is_pending()
    {
        var target = DesktopSkillOverlayPlatformPatch
            .TargetMethods()
            .Single(method => method.Name == "TryOpenOverlay");
        var generator = new DynamicMethod(
            "ReadNativeSkillBoardEntry",
            typeof(void),
            Type.EmptyTypes
        ).GetILGenerator();
        var original = PatchProcessor.GetOriginalInstructions(target, generator);
        var getterIndex = original.FindIndex(code =>
            code.operand is MethodInfo { Name: "get_IsPrimaryTooltipVisible" }
        );
        Assert.True(getterIndex >= 0);
        var getter = (MethodInfo)original[getterIndex].operand;
        var patched = DesktopSkillBoardTooltipPatch.Transpiler(original).ToArray();

        Assert.Equal(original.Count + 1, patched.Length);
        Assert.Equal(OpCodes.Pop, patched[getterIndex].opcode);
        Assert.Null(patched[getterIndex].operand);
        Assert.Equal(original[getterIndex].labels, patched[getterIndex].labels);
        Assert.Equal(original[getterIndex].blocks, patched[getterIndex].blocks);
        Assert.Equal(OpCodes.Ldc_I4_0, patched[getterIndex + 1].opcode);
        for (var i = 0; i < original.Count; i++)
        {
            if (i == getterIndex)
                continue;
            var after = patched[i < getterIndex ? i : i + 1];
            Assert.Equal(original[i].opcode, after.opcode);
            Assert.Equal(original[i].operand, after.operand);
            Assert.Equal(original[i].labels, after.labels);
            Assert.Equal(original[i].blocks, after.blocks);
        }

        // Execute the replacement: it must not consult the tooltip's lifetime or require
        // a live Unity receiver. The rest of TryOpenOverlay still runs its native gates.
        var gate = new DynamicMethod(
            "SkillOverlayTooltipGate",
            typeof(bool),
            [getter.DeclaringType!]
        );
        var emitter = gate.GetILGenerator();
        foreach (
            var code in DesktopSkillBoardTooltipPatch.Transpiler([
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Callvirt, getter),
                new CodeInstruction(OpCodes.Ret),
            ])
        )
            emitter.Emit(code.opcode);
        Assert.False((bool)gate.Invoke(null, [null])!);
    }

    [Fact]
    public void Changed_native_tooltip_gate_is_rejected()
    {
        Assert.Throws<InvalidOperationException>(() =>
            DesktopSkillBoardTooltipPatch.Transpiler([new CodeInstruction(OpCodes.Ret)]).ToList()
        );
    }

    [Fact]
    public void Changed_native_platform_gate_is_rejected()
    {
        Assert.Throws<InvalidOperationException>(() =>
            DesktopSkillOverlayPlatformPatch.Transpiler([new CodeInstruction(OpCodes.Ret)]).ToList()
        );
    }
}

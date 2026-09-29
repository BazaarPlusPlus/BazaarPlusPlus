using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using BazaarPlusPlus.Localization;
using Xunit;

namespace RuntimeIntegration.Tests;

public sealed class TraditionalChineseConversionTests
{
    // Every Chinese literal compiled into the mod, run through the Taiwan-mode fallback, must come
    // out free of Simplified-only characters. Literals that already carry Traditional text pass
    // through unchanged, so the sweep also catches a stray Simplified character in authored zh-Hant.
    [Fact]
    public void Taiwan_mode_leaves_no_simplified_only_character_in_compiled_mod_text()
    {
        var simplifiedOnly = OpenCcSimplifiedOnlyCharacters
            .Text.EnumerateRunes()
            .Where(rune => !Rune.IsWhiteSpace(rune))
            .ToHashSet();
        var literals = CompiledChineseLiterals(
            Path.Combine(AppContext.BaseDirectory, "BazaarPlusPlus.dll")
        );

        var leftovers = literals
            .Select(literal =>
                (
                    Literal: literal,
                    Missing: ChineseScriptConverter
                        .Convert(literal, null, BppChineseLocaleMode.Taiwan)
                        .EnumerateRunes()
                        .Where(simplifiedOnly.Contains)
                        .Select(rune => rune.ToString())
                        .Distinct()
                        .ToArray()
                )
            )
            .Where(result => result.Missing.Length > 0)
            .Select(result => $"{string.Concat(result.Missing)} in \"{result.Literal}\"")
            .ToArray();

        Assert.NotEmpty(literals);
        Assert.True(leftovers.Length == 0, string.Join(Environment.NewLine, leftovers));
    }

    // Characters that are valid Traditional on their own but take another form in these words.
    [Theory]
    [InlineData("已同步 3 场幽灵对战。", "已同步 3 場幽靈對戰。")]
    [InlineData("选择一场战斗进行回放。", "選擇一場戰鬥進行回放。")]
    [InlineData("无法开始录制", "無法開始錄製")]
    [InlineData("已复制 {displayName}: {templateId}", "已複製 {displayName}: {templateId}")]
    [InlineData("标签", "標籤")]
    [InlineData("请等待游戏恢复操作", "請等待遊戲恢復操作")]
    [InlineData("正在准备回放。", "正在準備回放。")]
    [InlineData("具体以游戏内实际为准。", "具體以遊戲內實際為準。")]
    [InlineData("十胜推荐", "十勝推薦")]
    [InlineData("暂时不能在这个面板里删除幽灵战斗。", "暫時不能在這個面板裡刪除幽靈戰鬥。")]
    [InlineData("当前商店选择里没有物品。", "當前商店選擇裡沒有物品。")]
    public void Taiwan_mode_converts_mod_phrases(string mainland, string expected)
    {
        Assert.Equal(
            expected,
            ChineseScriptConverter.Convert(mainland, null, BppChineseLocaleMode.Taiwan)
        );
    }

    private static List<string> CompiledChineseLiterals(string assembly)
    {
        using var stream = File.OpenRead(assembly);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var literals = new List<string>();
        for (
            var handle = MetadataTokens.UserStringHandle(1);
            !handle.IsNil;
            handle = metadata.GetNextHandle(handle)
        )
        {
            var literal = metadata.GetUserString(handle);
            if (literal.EnumerateRunes().Any(IsHan))
                literals.Add(literal);
        }
        return literals;
    }

    private static bool IsHan(Rune rune) =>
        rune.Value is >= 0x3400 and <= 0x9FFF or >= 0x20000 and <= 0x3FFFF;
}

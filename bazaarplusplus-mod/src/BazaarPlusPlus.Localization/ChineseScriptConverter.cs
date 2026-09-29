#nullable enable
namespace BazaarPlusPlus.Localization;

internal static class ChineseScriptConverter
{
    // Words whose one-to-many characters the character table would convert wrongly, matched
    // longest first before falling back to single characters: the OpenCC STPhrases entries that
    // mod text and the voice-line catalog use, minus those wrong for that text (海里 where 里
    // means "inside", 上游 in 街上游荡), plus transliterated names that keep 里.
    private static readonly Dictionary<string, string> TraditionalPhraseMap = new(
        StringComparer.Ordinal
    )
    {
        ["一只"] = "一隻",
        ["一周"] = "一週",
        ["一见钟情"] = "一見鍾情",
        ["三只"] = "三隻",
        ["上不了台面"] = "上不了檯面",
        ["下周"] = "下週",
        ["不了解"] = "不瞭解",
        ["不划算"] = "不划算",
        ["了解"] = "瞭解",
        ["事迹"] = "事蹟",
        ["佣金"] = "佣金",
        ["借口"] = "藉口",
        ["克制"] = "剋制",
        ["关系"] = "關係",
        ["养家糊口"] = "養家餬口",
        ["准入"] = "准入",
        ["准许"] = "准許",
        ["划算"] = "划算",
        ["制作"] = "製作",
        ["制药"] = "製藥",
        ["制造"] = "製造",
        ["制造出"] = "製造出",
        ["割舍"] = "割捨",
        ["加里"] = "加里",
        ["升华"] = "昇華",
        ["卷入"] = "捲入",
        ["卷土重来"] = "捲土重來",
        ["反复"] = "反覆",
        ["反复无常"] = "反覆無常",
        ["发型"] = "髮型",
        ["叮当"] = "叮噹",
        ["叮当响"] = "叮噹響",
        ["同流合污"] = "同流合汙",
        ["向导"] = "嚮導",
        ["吞并"] = "吞併",
        ["命中注定"] = "命中註定",
        ["喂养"] = "餵養",
        ["喂给"] = "餵給",
        ["回归"] = "迴歸",
        ["坏家伙"] = "壞傢伙",
        ["复制"] = "複製",
        ["复合"] = "複合",
        ["复杂"] = "複雜",
        ["大只"] = "大隻",
        ["头发"] = "頭髮",
        ["夸克"] = "夸克",
        ["奇迹"] = "奇蹟",
        ["委托"] = "委託",
        ["定制"] = "定製",
        ["家伙"] = "傢伙",
        ["小家伙"] = "小傢伙",
        ["小杰"] = "小杰",
        ["尝到"] = "嚐到",
        ["尝尝"] = "嚐嚐",
        ["尽可"] = "儘可",
        ["尽管"] = "儘管",
        ["尽量"] = "儘量",
        ["巴里"] = "巴里",
        ["布置"] = "佈置",
        ["干净"] = "乾淨",
        ["干干净净"] = "乾乾淨淨",
        ["干扰"] = "干擾",
        ["干涉"] = "干涉",
        ["干站着"] = "乾站著",
        ["干等"] = "乾等",
        ["干草"] = "乾草",
        ["幸免于难"] = "倖免於難",
        ["幸存"] = "倖存",
        ["弦断"] = "絃斷",
        ["录制"] = "錄製",
        ["征服"] = "征服",
        ["恶心"] = "噁心",
        ["惊叹"] = "驚歎",
        ["想象"] = "想像",
        ["扎实"] = "紮實",
        ["托付"] = "託付",
        ["拜托"] = "拜託",
        ["拨弦"] = "撥絃",
        ["挨饿"] = "捱餓",
        ["操作台"] = "操作檯",
        ["收获"] = "收穫",
        ["放松"] = "放鬆",
        ["放轻松"] = "放輕鬆",
        ["日志"] = "日誌",
        ["是只"] = "是隻",
        ["是里格"] = "是里格",
        ["杂志"] = "雜誌",
        ["松了"] = "鬆了",
        ["松开"] = "鬆開",
        ["松懈"] = "鬆懈",
        ["标签"] = "標籤",
        ["榨干"] = "榨乾",
        ["欲望"] = "慾望",
        ["没关系"] = "沒關係",
        ["治愈"] = "治癒",
        ["注定"] = "註定",
        ["洗干净"] = "洗乾淨",
        ["淬炼"] = "淬鍊",
        ["激荡"] = "激盪",
        ["炼金"] = "鍊金",
        ["炼金术"] = "鍊金術",
        ["烟草"] = "菸草",
        ["焚毁"] = "焚燬",
        ["特里"] = "特里",
        ["玛里"] = "瑪里",
        ["痊愈"] = "痊癒",
        ["精致"] = "精緻",
        ["索里安"] = "索里安",
        ["老板"] = "老闆",
        ["老里格"] = "老里格",
        ["联系"] = "聯繫",
        ["背着"] = "揹著",
        ["背负"] = "揹負",
        ["舍不得"] = "捨不得",
        ["舍弃"] = "捨棄",
        ["英里"] = "英里",
        ["蒙蔽"] = "矇蔽",
        ["调制"] = "調製",
        ["费纳里"] = "費納里",
        ["轻松"] = "輕鬆",
        ["这周"] = "這週",
        ["遍布"] = "遍佈",
        ["遗迹"] = "遺蹟",
        ["那只"] = "那隻",
        ["那只是"] = "那只是",
        ["里亚"] = "里亞",
        ["里欧"] = "里歐",
        ["重复"] = "重複",
        ["锻炼"] = "鍛鍊",
        ["锻炼身体"] = "鍛鍊身體",
        ["防御"] = "防禦",
        ["雅致"] = "雅緻",
        ["面包"] = "麵包",
        ["面包师"] = "麵包師",
        ["面包房"] = "麵包房",
        ["预制"] = "預製",
        ["风采"] = "風采",
        ["饼干"] = "餅乾",
        ["香蕉干"] = "香蕉乾",
    };

    private static readonly int MaxPhraseLength = TraditionalPhraseMap.Keys.Max(key => key.Length);

    private static readonly HashSet<char> PhraseStarts = new(
        TraditionalPhraseMap.Keys.Select(key => key[0])
    );

    private static readonly Dictionary<char, string> TraditionalCharacterMap = CreateCharacterMap();

    internal static BppChineseLocaleMode NormalizeMode(BppChineseLocaleMode mode)
    {
        return mode == BppChineseLocaleMode.Mainland
            ? BppChineseLocaleMode.Mainland
            : BppChineseLocaleMode.Taiwan;
    }

    internal static string Convert(string mainland, string? traditional, BppChineseLocaleMode mode)
    {
        if (string.IsNullOrEmpty(mainland))
            return mainland;

        var normalizedMode = NormalizeMode(mode);
        if (normalizedMode == BppChineseLocaleMode.Mainland)
            return mainland;

        if (!string.IsNullOrWhiteSpace(traditional))
            return traditional;

        var converted = ConvertToTraditional(mainland);
        return normalizedMode switch
        {
            BppChineseLocaleMode.Taiwan => ApplyTraditionalChineseTerms(converted),
            _ => mainland,
        };
    }

    internal static string ResolveModeStatus(BppChineseLocaleMode mode)
    {
        return NormalizeMode(mode) switch
        {
            BppChineseLocaleMode.Mainland => "CN",
            _ => "TW",
        };
    }

    // Curated deviations from the OpenCC first candidate: 签 and 钟 take their common Taiwan
    // forms, and 着, absent from STCharacters, becomes 著.
    private static Dictionary<char, string> CreateCharacterMap()
    {
        var map = OpenCcTraditionalCharacters.Create();
        map['签'] = "簽";
        map['钟'] = "鐘";
        map['着'] = "著";
        return map;
    }

    private static string ConvertToTraditional(string text)
    {
        var buffer = new System.Text.StringBuilder(text.Length);
        var index = 0;
        while (index < text.Length)
        {
            if (
                PhraseStarts.Contains(text[index])
                && TryMatchPhrase(text, index, out var length, out var phrase)
            )
            {
                buffer.Append(phrase);
                index += length;
                continue;
            }

            var character = text[index++];
            buffer.Append(
                TraditionalCharacterMap.TryGetValue(character, out var mapped) ? mapped : character
            );
        }

        return buffer.ToString();
    }

    private static bool TryMatchPhrase(string text, int index, out int length, out string phrase)
    {
        for (length = Math.Min(MaxPhraseLength, text.Length - index); length > 1; length--)
        {
            if (TraditionalPhraseMap.TryGetValue(text.Substring(index, length), out phrase!))
                return true;
        }

        phrase = string.Empty;
        return false;
    }

    // Shared Traditional Chinese fallback terms for the single TW mode: Taiwan vocabulary, not
    // script conversion, so they match the already converted text.
    private static string ApplyTraditionalChineseTerms(string text)
    {
        return text.Replace("數據庫", "資料庫", StringComparison.Ordinal)
            .Replace("查看", "檢視", StringComparison.Ordinal);
    }
}

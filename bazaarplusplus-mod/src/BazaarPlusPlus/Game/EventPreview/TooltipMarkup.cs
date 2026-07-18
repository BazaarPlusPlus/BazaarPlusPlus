#nullable enable
using System.Text;

namespace BazaarPlusPlus.Game.EventPreview;

// TMP has no CSS box model, so model prose semantics first and translate them here.
// Callers describe paragraphs and lists; only this renderer owns vertical rhythm.
internal static class TooltipMarkup
{
    private const string ProseLine = "<line-height=1.4em>";
    private const string ParagraphGap = "<line-height=1.9em>\n";
    private const string ListItemGap = "<line-height=1.9em>\n";
    private const string NestedItemGap = "<line-height=1.65em>\n";
    private const string NativeLineHeightOpen = "<line-height=1.6em>";
    private const string LineHeightClose = "</line-height>";

    internal abstract class Block
    {
        protected Block(int fontSizePercent)
        {
            FontSizePercent = fontSizePercent;
        }

        public int FontSizePercent { get; }
    }

    internal sealed class Paragraph : Block
    {
        public Paragraph(string content, int fontSizePercent = 100)
            : base(fontSizePercent)
        {
            Content = content ?? string.Empty;
        }

        public string Content { get; }
    }

    internal sealed class ListBlock : Block
    {
        public ListBlock(string? header, IReadOnlyList<ListItem> items, int fontSizePercent = 100)
            : base(fontSizePercent)
        {
            Header = header;
            Items = items ?? Array.Empty<ListItem>();
        }

        public string? Header { get; }

        public IReadOnlyList<ListItem> Items { get; }
    }

    internal sealed class ListItem
    {
        public ListItem(
            string content,
            IReadOnlyList<ListItem>? children = null,
            int fontSizePercent = 100,
            string? color = null
        )
        {
            Content = content ?? string.Empty;
            Children = children ?? Array.Empty<ListItem>();
            FontSizePercent = fontSizePercent;
            Color = color;
        }

        public string Content { get; }

        public IReadOnlyList<ListItem> Children { get; }

        public int FontSizePercent { get; }

        public string? Color { get; }
    }

    public static string Render(IReadOnlyList<Block> blocks)
    {
        if (blocks == null || blocks.Count == 0)
            return string.Empty;

        var builder = new StringBuilder();
        for (var index = 0; index < blocks.Count; index++)
            AppendBlock(builder, blocks[index], hasFollowingBlock: index + 1 < blocks.Count);
        return builder.ToString();
    }

    private static void AppendBlock(StringBuilder builder, Block block, bool hasFollowingBlock)
    {
        OpenSize(builder, block.FontSizePercent);
        switch (block)
        {
            case Paragraph paragraph:
                builder.Append(ProseLine).Append(paragraph.Content);
                break;
            case ListBlock list:
                if (!string.IsNullOrEmpty(list.Header))
                    builder.Append(ProseLine).Append(list.Header);
                AppendListItems(
                    builder,
                    list.Items,
                    depth: 0,
                    hasLeadingContent: !string.IsNullOrEmpty(list.Header)
                );
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(block),
                    block.GetType().FullName,
                    "Unsupported tooltip prose block type."
                );
        }
        if (hasFollowingBlock)
            builder.Append(ParagraphGap);
        CloseSize(builder, block.FontSizePercent);
    }

    private static void AppendListItems(
        StringBuilder builder,
        IReadOnlyList<ListItem> items,
        int depth,
        bool hasLeadingContent
    )
    {
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            if (index == 0 && hasLeadingContent)
                builder.Append(depth == 0 ? ListItemGap : NestedItemGap);
            OpenSize(builder, item.FontSizePercent);
            builder.Append(ProseLine);
            if (!string.IsNullOrEmpty(item.Color))
                builder.Append("<color=").Append(item.Color).Append(">");
            if (depth == 0)
                builder.Append("· <indent=1em>").Append(item.Content).Append("</indent>");
            else
                builder.Append("<indent=2.2em>- ").Append(item.Content).Append("</indent>");
            AppendListItems(builder, item.Children, depth + 1, hasLeadingContent: true);
            if (!string.IsNullOrEmpty(item.Color))
                builder.Append("</color>");
            if (index + 1 < items.Count)
                builder.Append(depth == 0 ? ListItemGap : NestedItemGap);
            CloseSize(builder, item.FontSizePercent);
        }
    }

    private static void OpenSize(StringBuilder builder, int percent)
    {
        if (percent != 100)
            builder.Append("<size=").Append(percent).Append("%>");
    }

    private static void CloseSize(StringBuilder builder, int percent)
    {
        if (percent != 100)
            builder.Append("</size>");
    }

    // ColorKeywords wraps its result in a native line-height pair. Embedded inline
    // content must inherit the prose element's line-height instead.
    public static string NormalizeInlineFragment(string content)
    {
        if (
            string.IsNullOrEmpty(content)
            || !content.StartsWith(NativeLineHeightOpen, StringComparison.Ordinal)
            || !content.EndsWith(LineHeightClose, StringComparison.Ordinal)
        )
            return content;

        return content.Substring(
            NativeLineHeightOpen.Length,
            content.Length - NativeLineHeightOpen.Length - LineHeightClose.Length
        );
    }
}

using BazaarPlusPlus.GameInterop.MonsterBoardPreview;
using Xunit;

public sealed class NativeMonsterBoardHealthBarTests
{
    [Fact]
    public void Bar_grouped_with_the_skills_under_an_offset_node_is_found()
    {
        // The tooltip prefab of game build 1.0.13081.
        var board = new Node("Tooltip_MonsterBoard_P");
        var main = board.Add("MainSize");
        var carpet = main.Add("Carpet");
        carpet.Add("Socket_01");
        var offset = main.Add("OffsetParent");
        var bar = offset.Add("Healthbar_Parent");
        bar.Add("Border_Left");
        var text = bar.Add("Health_Value");
        var skills = offset.Add("SkillParent");
        main.Add("CardPosition");

        Assert.Same(bar, Root(text, carpet, skills));
    }

    [Fact]
    public void Bar_directly_under_the_board_frame_is_found()
    {
        // The shape the earlier path MainSize/Healthbar_Parent addressed.
        var board = new Node("Tooltip_MonsterBoard_P");
        var main = board.Add("MainSize");
        var carpet = main.Add("Carpet");
        var bar = main.Add("Healthbar_Parent");
        var text = bar.Add("Health_Value");
        var skills = main.Add("SkillParent");

        Assert.Same(bar, Root(text, carpet, skills));
    }

    [Fact]
    public void Text_whose_parent_holds_a_board_part_is_its_own_root()
    {
        var board = new Node("Tooltip_MonsterBoard_P");
        var main = board.Add("MainSize");
        var carpet = main.Add("Carpet");
        var text = main.Add("Health_Value");
        var skills = main.Add("SkillParent");

        Assert.Same(text, Root(text, carpet, skills));
    }

    private static Node Root(Node text, Node carpet, Node skills) =>
        NativeMonsterBoardHealthBar.Root(
            text,
            node => node.Parent,
            node => carpet.IsWithin(node) || skills.IsWithin(node)
        );

    private sealed class Node(string name)
    {
        internal Node? Parent { get; private init; }

        internal Node Add(string child) => new(child) { Parent = this };

        // Transform.IsChildOf semantics: true for the ancestor itself.
        internal bool IsWithin(Node ancestor)
        {
            for (var node = this; node != null; node = node.Parent)
                if (node == ancestor)
                    return true;
            return false;
        }

        public override string ToString() => name;
    }
}

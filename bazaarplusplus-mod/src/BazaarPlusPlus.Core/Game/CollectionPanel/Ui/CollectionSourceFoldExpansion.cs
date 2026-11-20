#nullable enable
namespace BazaarPlusPlus.Game.CollectionPanel.Ui;

public sealed class CollectionSourceFoldExpansion
{
    private CollectionSourceGroup? _open;

    public CollectionSourceGroup? Open => _open;

    public bool IsOpen(CollectionSourceGroup group) => _open == group;

    // Null signals a fold-only action and leaves the source selection alone.
    public string? ClickCell(CollectionSourceGroup group)
    {
        if (!group.IsFolded)
        {
            _open = null;
            return group.Lead.SourceKey;
        }
        _open = _open == group ? null : group;
        return null;
    }

    public string ClickMember(CollectionSourceOptionViewModel member)
    {
        _open = null;
        return member.SourceKey;
    }

    public bool Collapse()
    {
        var wasOpen = _open != null;
        _open = null;
        return wasOpen;
    }
}

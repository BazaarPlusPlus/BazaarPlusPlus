#nullable enable
namespace BazaarPlusPlus.Game.CollectionPanel.Ui;

// Which folded source cell shows its members on the desktop Collection. At most one is open. A
// click on a folded cell opens or closes its members and changes no selection; a click on a
// member closes them and toggles that member's own key, as a single cell toggles its own.
public sealed class CollectionSourceFoldExpansion
{
    private CollectionSourceGroup? _open;

    public CollectionSourceGroup? Open => _open;

    public bool IsOpen(CollectionSourceGroup group) => _open == group;

    // The key a cell click toggles, or null when the click only opened or closed a fold.
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

    // Escape, a press outside the fold, a rebuilt row or a hidden panel. True when one was open.
    public bool Collapse()
    {
        var wasOpen = _open != null;
        _open = null;
        return wasOpen;
    }
}

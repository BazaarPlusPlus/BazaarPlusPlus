#nullable enable
namespace BazaarPlusPlus.Game.CollectionPanel.Ui;

// One source cell on the desktop Collection: the sources whose click shows the same cards
// (CollectionSourceFold). The group only draws; the selection, the stored selection and the
// encountered set still name a member's own source key.
public sealed class CollectionSourceGroup
{
    public CollectionSourceGroup(IReadOnlyList<CollectionSourceOptionViewModel> members)
    {
        if (members == null || members.Count == 0)
            throw new ArgumentException("A source group needs a member.", nameof(members));
        Members = members;
    }

    // Roster order.
    public IReadOnlyList<CollectionSourceOptionViewModel> Members { get; }

    public CollectionSourceOptionViewModel Lead => Members[0];

    public bool IsFolded => Members.Count > 1;

    public CollectionSourceOptionViewModel? SelectedMember(string? selectedSourceKey)
    {
        if (string.IsNullOrEmpty(selectedSourceKey))
            return null;
        foreach (var member in Members)
            if (string.Equals(member.SourceKey, selectedSourceKey, StringComparison.Ordinal))
                return member;
        return null;
    }

    public bool IsSelected(string? selectedSourceKey) => SelectedMember(selectedSourceKey) != null;

    public bool IsEncountered(IReadOnlyCollection<string> encounteredSourceKeys) =>
        FirstEncountered(encounteredSourceKeys) != null;

    // The member whose portrait draws the cell: the selected member, else the first member the
    // run met, else the first member.
    public CollectionSourceOptionViewModel Face(
        string? selectedSourceKey,
        IReadOnlyCollection<string> encounteredSourceKeys
    ) => SelectedMember(selectedSourceKey) ?? FirstEncountered(encounteredSourceKeys) ?? Lead;

    public bool HasSameMembers(CollectionSourceGroup other)
    {
        if (other.Members.Count != Members.Count)
            return false;
        for (var i = 0; i < Members.Count; i++)
            if (
                !string.Equals(
                    Members[i].SourceKey,
                    other.Members[i].SourceKey,
                    StringComparison.Ordinal
                )
            )
                return false;
        return true;
    }

    private CollectionSourceOptionViewModel? FirstEncountered(
        IReadOnlyCollection<string> encounteredSourceKeys
    )
    {
        foreach (var member in Members)
            if (encounteredSourceKeys.Contains(member.SourceKey))
                return member;
        return null;
    }
}

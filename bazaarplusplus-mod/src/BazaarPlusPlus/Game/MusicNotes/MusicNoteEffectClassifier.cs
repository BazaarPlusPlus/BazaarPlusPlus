#nullable enable
using BazaarGameShared.Domain.Cards.Socket;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Effect.AuraActions;
using BazaarGameShared.Domain.Prerequisites.Conditionals;
using BazaarGameShared.Domain.Targeting;

namespace BazaarPlusPlus.Game.MusicNotes;

/// <summary>One item-category tag a note socket effect keys on: either a hidden tag
/// (Burn, Heal, ...) or a card tag (Weapon). Exactly one side is set.</summary>
internal readonly struct MusicNoteTag : IEquatable<MusicNoteTag>
{
    private MusicNoteTag(EHiddenTag? hiddenTag, ECardTag? cardTag)
    {
        HiddenTag = hiddenTag;
        CardTag = cardTag;
    }

    internal EHiddenTag? HiddenTag { get; }
    internal ECardTag? CardTag { get; }

    internal static MusicNoteTag From(EHiddenTag tag) => new(tag, null);

    internal static MusicNoteTag From(ECardTag tag) => new(null, tag);

    public bool Equals(MusicNoteTag other) =>
        HiddenTag == other.HiddenTag && CardTag == other.CardTag;

    public override bool Equals(object? obj) => obj is MusicNoteTag other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(HiddenTag, CardTag);
}

/// <summary>
/// Extracts the item-category tags a note socket effect keys on, so the badge can reuse the
/// game's own keyword icons for them. Note templates express their category as
/// TCardConditionalHiddenTag / TCardConditionalTag conditions on the occupying item — on
/// ability trigger subjects and on aura action targets (e.g. "B Note (Burn)" conditions on
/// the Burn hidden tag). "*Reference" hidden tags are companion pseudo-tags and are skipped.
/// Pure over the template graph; safe to call from worker threads.
/// </summary>
internal static class MusicNoteEffectClassifier
{
    private const int MaxConditionDepth = 6;

    internal static IReadOnlyList<MusicNoteTag> GetTags(TCardSocketEffect? template)
    {
        if (template == null)
            return Array.Empty<MusicNoteTag>();

        var tags = new List<MusicNoteTag>(2);
        try
        {
            if (template.Abilities != null)
            {
                foreach (var ability in template.Abilities.Values)
                {
                    if (ability?.Trigger?.GetSubject() is TTargetCardBase subject)
                        Collect(subject.Conditions, tags, MaxConditionDepth);
                }
            }

            if (template.Auras != null)
            {
                foreach (var aura in template.Auras.Values)
                {
                    if (aura?.Action is TAuraActionCardBase cardAction)
                        Collect(cardAction.Target?.Conditions, tags, MaxConditionDepth);
                }
            }
        }
        catch
        {
            // Template graphs are data-driven; keep whatever was collected before the surprise.
        }

        return tags;
    }

    private static void Collect(object? condition, List<MusicNoteTag> tags, int depth)
    {
        if (condition == null || depth <= 0)
            return;

        switch (condition)
        {
            case TCardConditionalHiddenTag hiddenTags:
                foreach (var tag in hiddenTags.Tags)
                {
                    if (!IsReferenceTag(tag))
                        Add(tags, MusicNoteTag.From(tag));
                }
                break;
            case TCardConditionalTag cardTags:
                foreach (var tag in cardTags.Tags)
                    Add(tags, MusicNoteTag.From(tag));
                break;
            case TCardConditionalAnd and:
                foreach (var nested in and.Conditions)
                    Collect(nested, tags, depth - 1);
                break;
            case TCardConditionalOr or:
                foreach (var nested in or.Conditions)
                    Collect(nested, tags, depth - 1);
                break;
        }
    }

    private static void Add(List<MusicNoteTag> tags, MusicNoteTag tag)
    {
        if (!tags.Contains(tag))
            tags.Add(tag);
    }

    private static bool IsReferenceTag(EHiddenTag tag) =>
        tag.ToString().EndsWith("Reference", StringComparison.Ordinal);
}

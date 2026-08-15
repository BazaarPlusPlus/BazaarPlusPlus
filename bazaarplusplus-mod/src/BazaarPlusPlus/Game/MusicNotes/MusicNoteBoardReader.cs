#nullable enable
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Socket;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Prerequisites.Conditionals;
using TheBazaar;

namespace BazaarPlusPlus.Game.MusicNotes;

internal enum MusicNoteSocketBadgeKind
{
    /// <summary>A music note occupies this socket.</summary>
    Active,

    /// <summary>The socket is empty (or holds a non-note effect) but its letter is fixed by
    /// the anchor rule — a note placed here will be this letter.</summary>
    Implied,
}

internal readonly struct MusicNoteSocketBadge
{
    internal MusicNoteSocketBadge(
        int socketIndex,
        MusicNoteSocketBadgeKind kind,
        EMusicNote letter,
        TCardMusicNoteSocketEffect? placedTemplate,
        ICard? placedNote,
        IReadOnlyList<ITCardConditional>? occupancyGate,
        bool isBoosted
    )
    {
        SocketIndex = socketIndex;
        Kind = kind;
        Letter = letter;
        PlacedTemplate = placedTemplate;
        PlacedNote = placedNote;
        OccupancyGate = occupancyGate;
        IsBoosted = isBoosted;
    }

    internal int SocketIndex { get; }
    internal MusicNoteSocketBadgeKind Kind { get; }
    internal EMusicNote Letter { get; }
    internal TCardMusicNoteSocketEffect? PlacedTemplate { get; }

    /// <summary>The placed note entity, as conditional-context targeting card.</summary>
    internal ICard? PlacedNote { get; }

    /// <summary>
    /// The note's occupancy conditions — from the placed template when a note is present,
    /// else the letter's catalog entry. Null while the catalog is still loading.
    /// </summary>
    internal IReadOnlyList<ITCardConditional>? OccupancyGate { get; }

    /// <summary>A placed note whose occupying item passes the gate: the buff is live.</summary>
    internal bool IsBoosted { get; }
}

/// <summary>
/// Main-thread read of the player board's music-note state into badge models. Placed notes come
/// from the same source the board visuals use (player-owned SocketEffect entities keyed by
/// LeftSocketId); locked sockets come from the player's socket container. Returns null whenever
/// the board is not readable or no anchor note exists — this is a per-frame path, so failures
/// stay silent and render as "nothing to show".
/// </summary>
/// <summary>
/// One frame's board read: the socket badges plus the items layer the hover comparison needs
/// to judge placeability (who covers each hand socket, and which hand sockets are locked).
/// </summary>
internal sealed class MusicNoteBoardView
{
    internal MusicNoteBoardView(
        List<MusicNoteSocketBadge> badges,
        ICard?[] handOccupants,
        bool[] handLocked
    )
    {
        Badges = badges;
        HandOccupants = handOccupants;
        HandLocked = handLocked;
    }

    internal List<MusicNoteSocketBadge> Badges { get; }

    /// <summary>The item covering each hand socket (repeated across a multi-slot span).</summary>
    internal ICard?[] HandOccupants { get; }

    internal bool[] HandLocked { get; }
}

internal static class MusicNoteBoardReader
{
    internal static MusicNoteBoardView? Read()
    {
        try
        {
            return ReadCore();
        }
        catch
        {
            // Touches game statics per frame; never log here. Hide if unsure.
            return null;
        }
    }

    private static MusicNoteBoardView? ReadCore()
    {
        var run = Data.Run;
        var player = run?.Player;
        var container = player?.Socket?.Container;
        if (player == null || container == null)
            return null;

        var socketCount = container.Sockets?.Length ?? 0;
        if (socketCount <= 0)
            return null;

        var placedTemplates = new TCardMusicNoteSocketEffect?[socketCount];
        var placedNotes = new SocketEffect?[socketCount];
        foreach (var entity in Data.Entities.Values)
        {
            if (
                entity is SocketEffect socketEffect
                && ReferenceEquals(socketEffect.Owner, player)
                && socketEffect.Section == EInventorySection.Hand
                && socketEffect.LeftSocketId is EContainerSocketId socketId
                && (int)socketId >= 0
                && (int)socketId < socketCount
                && socketEffect.Template is TCardMusicNoteSocketEffect noteTemplate
            )
            {
                placedTemplates[(int)socketId] = noteTemplate;
                placedNotes[(int)socketId] = socketEffect;
            }
        }

        var placedLetters = new int?[socketCount];
        for (var i = 0; i < socketCount; i++)
        {
            if (placedTemplates[i] is TCardMusicNoteSocketEffect note)
                placedLetters[i] = (int)note.MusicNote;
        }

        var letters = MusicNoteLetterMath.InferLetters(placedLetters);

        // The items layer: the card covering socket i is the note's occupying card, exactly
        // as the game's TTargetCardOccupying resolves it (Hand.Container.Sockets repeats a
        // multi-slot item across its whole span).
        var handContainer = player.Hand?.Container;
        var handSockets = handContainer?.Sockets;
        var handOccupants = new ICard?[socketCount];
        var handLocked = new bool[socketCount];
        for (var i = 0; i < socketCount; i++)
        {
            if (handSockets != null && i < handSockets.Length)
                handOccupants[i] = handSockets[i] as ICard;
            handLocked[i] = handContainer == null || handContainer.IsSocketLocked(i);
        }

        List<MusicNoteSocketBadge>? badges = null;
        for (var i = 0; i < socketCount; i++)
        {
            if (letters[i] is not int letter || container.IsSocketLocked(i))
                continue;

            badges ??= new List<MusicNoteSocketBadge>(socketCount);
            var placed = placedTemplates[i];
            var occupancyGate =
                placed != null
                    ? MusicNoteFitEvaluator.ExtractOccupyingConditions(placed)
                    : MusicNoteTemplateCatalog.TryGetOccupancyGateForLetter((EMusicNote)letter);
            var isBoosted =
                placed != null
                && handOccupants[i] is ICard occupying
                && MusicNoteFitEvaluator.Satisfies(occupancyGate, occupying, run, placedNotes[i]);
            badges.Add(
                new MusicNoteSocketBadge(
                    i,
                    placed != null
                        ? MusicNoteSocketBadgeKind.Active
                        : MusicNoteSocketBadgeKind.Implied,
                    (EMusicNote)letter,
                    placed,
                    placedNotes[i],
                    occupancyGate,
                    isBoosted
                )
            );
        }

        return badges != null ? new MusicNoteBoardView(badges, handOccupants, handLocked) : null;
    }
}

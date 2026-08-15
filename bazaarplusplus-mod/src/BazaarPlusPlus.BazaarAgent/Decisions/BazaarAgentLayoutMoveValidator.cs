#nullable enable
namespace BazaarPlusPlus.BazaarAgent;

/// <summary>Pure mechanical preflight for native inventory drag operations.</summary>
public static class BazaarAgentLayoutMoveValidator
{
    private const int SocketCount = 10;

    public static BazaarAgentValidationResult Validate(
        BazaarAgentContextSnapshot snapshot,
        BazaarAgentAction action
    )
    {
        var card = snapshot
            .Context.BoardItems.Concat(snapshot.Context.ChestItems)
            .FirstOrDefault(card => card.InstanceId == action.CardInstanceId);
        if (card is null)
            return Fail("item not found in inventory");
        if (
            action.TargetSection
            is not BazaarAgentTargetSection.Hand
                and not BazaarAgentTargetSection.Stash
        )
            return Fail("unsupported target section");

        var size = BazaarAgentCardSize.Parse(card.Size, fallback: 0);
        if (
            size is < 1 or > 3
            || !TryParseSockets(action.TargetSockets, size, out var targetSockets)
        )
            return Fail("target sockets must be contiguous and match item size");

        var source =
            card.Location == BazaarAgentCardLocation.Board
                ? snapshot.Context.BoardItems
                : snapshot.Context.ChestItems;
        var destination =
            action.TargetSection == BazaarAgentTargetSection.Hand
                ? snapshot.Context.BoardItems
                : snapshot.Context.ChestItems;
        var destinationLocks =
            action.TargetSection == BazaarAgentTargetSection.Hand
                ? snapshot.Context.LockedBoardSockets
                : Array.Empty<string>();
        if (
            targetSockets.Any(socket =>
                destinationLocks.Any(locked =>
                    string.Equals(
                        locked,
                        "Socket_"
                            + socket.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        StringComparison.Ordinal
                    )
                )
            )
        )
            return Fail("target contains a locked socket");
        var targetIsSource = ReferenceEquals(source, destination);
        if (targetIsSource)
        {
            if (string.Equals(card.SocketId, action.TargetSockets![0], StringComparison.Ordinal))
                return Fail("item is already at target socket");
            return Ok();
        }

        var destinationSlots = Occupancy(destination);
        var sourceFreeSlots =
            SocketCount - OccupiedOrLockedSocketCount(snapshot.Context, source) + size;
        var displaced = targetSockets
            .Select(socket => destinationSlots[socket])
            .Where(static item => item is not null)
            .Distinct(StringComparer.Ordinal)
            .Select(id => destination.First(item => item.InstanceId == id))
            .ToArray();
        var displacedSize = displaced.Sum(item =>
            BazaarAgentCardSize.Parse(item.Size, fallback: 0)
        );
        if (displacedSize > sourceFreeSlots)
            return Fail("target occupants cannot fit back into source inventory");

        return Ok();
    }

    private static string?[] Occupancy(IReadOnlyList<BazaarAgentCardSnapshot> cards)
    {
        var result = new string?[SocketCount];
        foreach (var card in cards)
        {
            if (!TryParseSocket(card.SocketId, out var start))
                continue;
            var size = BazaarAgentCardSize.Parse(card.Size, fallback: 0);
            for (var index = start; index < start + size && index < SocketCount; index++)
                result[index] = card.InstanceId;
        }
        return result;
    }

    private static int OccupiedOrLockedSocketCount(
        BazaarAgentContext context,
        IReadOnlyList<BazaarAgentCardSnapshot> cards
    )
    {
        var occupied = Occupancy(cards)
            .Select((item, index) => item is not null ? index : -1)
            .Where(static index => index >= 0)
            .ToHashSet();
        var locks = ReferenceEquals(cards, context.BoardItems)
            ? context.LockedBoardSockets
            : Array.Empty<string>();
        foreach (var socket in locks)
            if (TryParseSocket(socket, out var index))
                occupied.Add(index);
        return occupied.Count;
    }

    private static bool TryParseSockets(
        IReadOnlyList<string>? values,
        int expectedCount,
        out int[] sockets
    )
    {
        sockets = Array.Empty<int>();
        if (values is null || values.Count != expectedCount)
            return false;
        var parsed = new int[values.Count];
        for (var index = 0; index < values.Count; index++)
        {
            if (
                !TryParseSocket(values[index], out parsed[index])
                || parsed[index] != parsed[0] + index
            )
                return false;
        }
        if (parsed[^1] >= SocketCount)
            return false;
        sockets = parsed;
        return true;
    }

    private static bool TryParseSocket(string? value, out int socket)
    {
        socket = -1;
        const string prefix = "Socket_";
        return value is not null
            && value.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(value.AsSpan(prefix.Length), out socket)
            && socket is >= 0 and < SocketCount;
    }

    private static BazaarAgentValidationResult Ok() =>
        new(BazaarAgentValidationCode.Ok, 200, null, null);

    private static BazaarAgentValidationResult Fail(string details) =>
        new(BazaarAgentValidationCode.StaleOrUnavailable, 409, details, null);
}

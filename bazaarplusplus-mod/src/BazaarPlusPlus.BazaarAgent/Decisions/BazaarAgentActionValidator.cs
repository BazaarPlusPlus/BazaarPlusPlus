#nullable enable
namespace BazaarPlusPlus.BazaarAgent;

public enum BazaarAgentValidationCode
{
    Ok,
    Invalid,
    StaleOrUnavailable,
    Cooldown,
    Unavailable,
}

public readonly record struct BazaarAgentValidationResult(
    BazaarAgentValidationCode Code,
    int HttpStatus,
    string? Details,
    IReadOnlyDictionary<string, object?>? Extra
);

public static class BazaarAgentActionValidator
{
    // Hardcoded sets — no dependency on game enums.
    private static readonly HashSet<string> _validHeroes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Common",
        "Pygmalien",
        "Vanessa",
        "Stelle",
        "Jules",
        "Dooley",
        "Mak",
        "Karnok",
    };

    private static readonly HashSet<string> _validPlayModes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Unranked",
        "Ranked",
    };

    // Kinds that carry a (CardInstanceId, TargetSection, TargetSockets) triplet.
    private static readonly HashSet<BazaarAgentActionKind> _cardBearingKinds = new()
    {
        BazaarAgentActionKind.SelectItem,
        BazaarAgentActionKind.SelectSkill,
        BazaarAgentActionKind.SelectEncounter,
        BazaarAgentActionKind.CommitToPedestal,
        BazaarAgentActionKind.MoveItem,
        BazaarAgentActionKind.SellItem,
    };

    private static BazaarAgentValidationResult Ok() =>
        new(BazaarAgentValidationCode.Ok, 200, null, null);

    private static BazaarAgentValidationResult Fail(
        BazaarAgentValidationCode code,
        int httpStatus,
        string details,
        IReadOnlyDictionary<string, object?>? extra = null
    ) => new(code, httpStatus, details, extra);

    public static BazaarAgentValidationResult Validate(
        BazaarAgentContextSnapshot snapshot,
        BazaarAgentAction action,
        double cooldownRemainingSeconds
    )
    {
        var kind = action.ActionKind;

        // ── Rule 1: known actionKind ──────────────────────────────────────────
        if (!Enum.IsDefined(typeof(BazaarAgentActionKind), kind))
            return Fail(BazaarAgentValidationCode.Invalid, 400, "unknown actionKind");

        // ── Rule 2: actionKind in AvailableActions (Wait exempt) ──────────────
        if (kind != BazaarAgentActionKind.Wait)
        {
            var found = false;
            foreach (var opt in snapshot.Context.AvailableActions)
            {
                if (opt.ActionKind == kind)
                {
                    found = true;
                    break;
                }
            }
            if (!found)
                return Fail(
                    BazaarAgentValidationCode.StaleOrUnavailable,
                    409,
                    "actionKind not in availableActions",
                    new Dictionary<string, object?> { ["currentTickId"] = snapshot.TickId }
                );
        }

        // ── Rule 3: card-bearing exact match ──────────────────────────────────
        BazaarAgentDecisionOption? matchedOption = null;

        if (_cardBearingKinds.Contains(kind))
        {
            foreach (var opt in snapshot.Context.AvailableActions)
            {
                if (
                    opt.ActionKind == kind
                    && opt.CardInstanceId == action.CardInstanceId
                    && opt.TargetSection == action.TargetSection
                    && SocketsEqual(opt.TargetSockets, action.TargetSockets)
                )
                {
                    matchedOption = opt;
                    break;
                }
            }
            if (matchedOption is null)
                return Fail(
                    BazaarAgentValidationCode.StaleOrUnavailable,
                    409,
                    "no matching option for card-bearing action",
                    new Dictionary<string, object?> { ["currentTickId"] = snapshot.TickId }
                );
        }

        // ── Rule 4: Hero / PlayMode (StartOrContinueRun only) ─────────────────
        if (kind == BazaarAgentActionKind.StartOrContinueRun)
        {
            if (action.Hero is { } hero && !_validHeroes.Contains(hero))
                return Fail(BazaarAgentValidationCode.Invalid, 400, "unknown hero");

            if (action.PlayMode is { } playMode && !_validPlayModes.Contains(playMode))
                return Fail(BazaarAgentValidationCode.Invalid, 400, "unknown playMode");
        }

        // ── Rule 5: CanSelect != false ────────────────────────────────────────
        if (
            matchedOption is not null
            && kind
                is BazaarAgentActionKind.SelectItem
                    or BazaarAgentActionKind.SelectSkill
                    or BazaarAgentActionKind.SelectEncounter
                    or BazaarAgentActionKind.CommitToPedestal
        )
        {
            if (matchedOption.Card?.CanSelect == false)
                return Fail(
                    BazaarAgentValidationCode.StaleOrUnavailable,
                    409,
                    "option marked CanSelect=false"
                );
        }

        if (
            matchedOption is not null
            && kind is BazaarAgentActionKind.SelectItem or BazaarAgentActionKind.SelectSkill
        )
        {
            if (matchedOption.Card?.CanAfford == false)
                return Fail(
                    BazaarAgentValidationCode.StaleOrUnavailable,
                    409,
                    "option not affordable"
                );
        }

        if (matchedOption is not null && kind == BazaarAgentActionKind.SelectItem)
        {
            if (matchedOption.Card?.CanFit == false)
                return Fail(
                    BazaarAgentValidationCode.StaleOrUnavailable,
                    409,
                    "option does not fit"
                );
        }

        // ── Rule 6: CanSell == true ────────────────────────────────────────────
        if (kind == BazaarAgentActionKind.SellItem && matchedOption is not null)
        {
            if (matchedOption.Card?.CanSell != true)
                return Fail(
                    BazaarAgentValidationCode.StaleOrUnavailable,
                    409,
                    "option not sellable"
                );
        }

        // ── Rule 7: forTickId match ────────────────────────────────────────────
        if (action.ForTickId is { } want && want != snapshot.TickId)
            return Fail(
                BazaarAgentValidationCode.StaleOrUnavailable,
                409,
                "stale tickId",
                new Dictionary<string, object?> { ["currentTickId"] = snapshot.TickId }
            );

        // ── Rule 8: cooldown (Wait exempt) ────────────────────────────────────
        if (kind != BazaarAgentActionKind.Wait && cooldownRemainingSeconds > 0)
            return Fail(
                BazaarAgentValidationCode.Cooldown,
                429,
                "action min-delay not yet elapsed",
                new Dictionary<string, object?> { ["retryAfterSeconds"] = cooldownRemainingSeconds }
            );

        return Ok();
    }

    private static bool SocketsEqual(IReadOnlyList<string>? a, IReadOnlyList<string>? b)
    {
        if (ReferenceEquals(a, b))
            return true;
        if (a is null && b is null)
            return true;
        if (a is null || b is null)
            return false;
        if (a.Count != b.Count)
            return false;
        for (var i = 0; i < a.Count; i++)
        {
            if (a[i] != b[i])
                return false;
        }
        return true;
    }
}

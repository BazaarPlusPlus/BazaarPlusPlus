namespace BazaarPlusPlus.Android.GameInterop;

// The Run Message Stream names a command by its HttpGameClient method and keys its arguments by
// that method's parameter names (docs/contracts/run-payload-v6.md). The desktop reads both from
// the patched method; this host sees only the command object, so the names are written here.
internal static class AndroidRunCommands
{
    public sealed record Shape(string Method, params string[] Parameters);

    public static readonly Shape InitializeRun = new(
        "SendInitializeRun",
        "playmode",
        "selectedHero",
        "gameModeId"
    );
    public static readonly Shape ReestablishSession = new("ReestablishSession");
    public static readonly Shape SelectItem = new(
        "SendSelectItem",
        "instanceId",
        "desiredSockets",
        "targetSection"
    );
    public static readonly Shape MoveItem = new(
        "SendMoveItem",
        "instanceId",
        "desiredSockets",
        "targetSection"
    );
    public static readonly Shape SelectSkill = new("SendSelectSkill", "id");
    public static readonly Shape SelectEncounter = new("SendSelectEncounter", "id");
    public static readonly Shape ReRollSelection = new("SendReRollSelection");
    public static readonly Shape ExitCurrentState = new("SendExitCurrentState");
    public static readonly Shape SellCard = new("SendSellCard", "cardId");
    public static readonly Shape CommitToPedestal = new("SendCommitToPedestal", "cardId");
    public static readonly Shape AbandonRun = new("SendAbandonRun");

    public static IReadOnlyList<Shape> All { get; } =
    [
        InitializeRun,
        ReestablishSession,
        SelectItem,
        MoveItem,
        SelectSkill,
        SelectEncounter,
        ReRollSelection,
        ExitCurrentState,
        SellCard,
        CommitToPedestal,
        AbandonRun,
    ];
}

#nullable enable
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.PvpBattles;

/// <summary>
/// Runs each of a host's PvP battle reads once, at most one read a frame, before the first battle
/// needs them, and throws the results away. On a host that compiles and binds its code on first
/// use (the Android host's JIT and IL2CPP interop), the window's first battle otherwise pays for
/// every read at once, inside the one frame its opening, combat and closing messages arrive in.
/// The player's reads run from <see cref="Tick"/>, and the message reads inside the
/// <see cref="Offer"/> call that delivered a game sim no battle is using, since a message is valid
/// only during that call. Both wait until the host says the player is readable: in a run, outside
/// a fight. A hand without a card tries again on a later frame until one holds a card, so the card
/// reads run too: a game sim outside a battle holds the opponent's cards only as a monster fight
/// opens. A step that throws tries again until its attempts run out.
/// </summary>
public sealed class PvpBattleReadsRehearsal<TMessage>
    where TMessage : class
{
    public const int DefaultAttempts = 30;

    private readonly IPvpBattleReads<TMessage> _reads;
    private readonly Func<bool> _playerReadable;
    private readonly Func<int> _frame;
    private readonly int _attempts;
    private readonly List<Func<bool>> _playerSteps = [];
    private readonly List<Func<TMessage, bool>> _messageSteps = [];
    private int _playerStep;
    private int _messageStep;
    private int _playerThrows;
    private int _messageThrows;
    private int _gaveUp;
    private int? _steppedFrame;
    private int? _deferredFrame;

    /// <param name="reads">The reads the host's window uses.</param>
    /// <param name="playerReadable">Whether the live player can be read now: in a run, outside a fight.</param>
    /// <param name="frame">The host's frame number.</param>
    /// <param name="serialize">The host's replay serialization of a game sim, run once.</param>
    /// <param name="attempts">Throws of a step before it gives up.</param>
    public PvpBattleReadsRehearsal(
        IPvpBattleReads<TMessage> reads,
        Func<bool> playerReadable,
        Func<int> frame,
        Action<TMessage> serialize,
        int attempts = DefaultAttempts
    )
    {
        _reads = reads ?? throw new ArgumentNullException(nameof(reads));
        _playerReadable = playerReadable ?? throw new ArgumentNullException(nameof(playerReadable));
        _frame = frame ?? throw new ArgumentNullException(nameof(frame));
        if (serialize == null)
            throw new ArgumentNullException(nameof(serialize));
        _attempts = attempts;

        _playerSteps.Add(() => reads.PlayerBoard(PvpSnapshotSection.Hand).Count > 0);
        _playerSteps.Add(() => Done(reads.PlayerBoard(PvpSnapshotSection.Skills)));
        foreach (var field in Values<PvpPlayerText>())
            _playerSteps.Add(() => Done(reads.PlayerText(field)));
        foreach (var field in Values<PvpPlayerNumber>())
            _playerSteps.Add(() => Done(reads.PlayerNumber(field)));

        // The opening's facts are cheap, so they run as one step; the hand runs last, since a game
        // sim outside a battle seldom holds the opponent's cards.
        _messageSteps.Add(message =>
        {
            foreach (var field in Values<PvpOpeningText>())
                reads.OpeningText(message, field);
            foreach (var field in Values<PvpOpeningNumber>())
                reads.OpeningNumber(message, field);
            return true;
        });
        _messageSteps.Add(message => Done(reads.OpponentBoard(message, PvpSnapshotSection.Skills)));
        _messageSteps.Add(message =>
        {
            serialize(message);
            return true;
        });
        _messageSteps.Add(message =>
            reads.OpponentBoard(message, PvpSnapshotSection.Hand).Count > 0
        );
    }

    public bool IsDone => _playerStep == _playerSteps.Count && _messageStep == _messageSteps.Count;

    /// <summary>The host's frame carries a battle: no step runs in it.</summary>
    public void Defer() => _deferredFrame = _frame();

    /// <summary>Every frame: at most one of the player's reads.</summary>
    public void Tick()
    {
        if (_playerStep == _playerSteps.Count || !CanStep() || !Readable())
            return;
        if (Step(_playerSteps[_playerStep], ref _playerThrows))
        {
            _playerStep++;
            LogWhenDone();
        }
    }

    /// <summary>
    /// A message no battle is using, inside the call that delivered it: at most one message read
    /// on a game sim.
    /// </summary>
    public void Offer(TMessage message)
    {
        if (message == null || _messageStep == _messageSteps.Count || !CanStep())
            return;
        if (!Readable() || !IsGameSim(message))
            return;
        var step = _messageSteps[_messageStep];
        if (Step(() => step(message), ref _messageThrows))
        {
            _messageStep++;
            LogWhenDone();
        }
    }

    private static T[] Values<T>()
        where T : struct, Enum => (T[])Enum.GetValues(typeof(T));

    private static bool Done<T>(T _) => true;

    private bool IsGameSim(TMessage message)
    {
        try
        {
            return _reads.Classify(message).Kind == PvpBattleSignalKind.GameSim;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private bool CanStep()
    {
        var frame = _frame();
        return frame != _steppedFrame && frame != _deferredFrame;
    }

    private bool Readable()
    {
        try
        {
            return _playerReadable();
        }
        catch (Exception)
        {
            return false;
        }
    }

    // Runs one try of a step in this frame. True when the step is over: it ran, or it gave up. A
    // read can throw while the run is still loading, so a throw only ends this try.
    private bool Step(Func<bool> run, ref int throws)
    {
        _steppedFrame = _frame();
        try
        {
            if (!run())
                return false;
        }
        catch (Exception)
        {
            if (++throws < _attempts)
                return false;
            _gaveUp++;
        }
        throws = 0;
        return true;
    }

    private void LogWhenDone()
    {
        if (IsDone)
            BppLog.InfoEvent(
                new BppLogEvent(BppLogFeatureScope.PvpBattles, "pvp_battles.reads.rehearsed"),
                ("gave_up", _gaveUp)
            );
    }
}

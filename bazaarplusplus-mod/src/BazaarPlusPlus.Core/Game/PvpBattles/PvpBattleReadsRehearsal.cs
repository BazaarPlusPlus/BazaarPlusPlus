#nullable enable
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.PvpBattles;

// Spread the Android host's first-use JIT and IL2CPP binding across frames so the first battle doesn't pay for all reads at once.
// Keep message reads inside Offer because the native message expires when its callback returns.
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

        // An unused game sim rarely carries an opponent hand, so try that read after the cheap facts.
        _messageSteps.Add(message =>
        {
            foreach (var field in Values<PvpOpeningText>())
                reads.OpeningText(message, field);
            foreach (var field in Values<PvpOpeningNumber>())
                reads.OpeningNumber(message, field);
            reads.PlayerAttributes(message);
            reads.PlayerStash();
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

    public void Defer() => _deferredFrame = _frame();

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

    // A read may throw while the run is still loading, so retry on a later frame.
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

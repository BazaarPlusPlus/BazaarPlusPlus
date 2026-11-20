#nullable enable
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Storage.RunLog;
using Newtonsoft.Json.Linq;

namespace BazaarPlusPlus.Game.RunLogging;

// The caller serializes access. Buffer early frames because their command or reconnect message can arrive
// before the run start event they must follow.
internal sealed class RunMessageStreamWriter
{
    // Bound the pending buffer so a client that never starts a run cannot grow it indefinitely.
    private const int MaxHeldFrames = 4096;

    private readonly List<(RunMessageStreamFrameObserved Frame, DateTimeOffset At)> _held = new();
    private readonly Dictionary<int, long> _commandSeqs = new();
    private string? _runId;

    internal void Observe(
        RunMessageStreamFrameObserved frame,
        RunLogSessionManager sessions,
        DateTimeOffset now
    )
    {
        if (frame.StartsRun)
        {
            _held.Clear();
            _commandSeqs.Clear();
            _runId = null;
        }
        if (
            _runId != null
            && string.Equals(sessions.ActiveSession?.RunId, _runId, StringComparison.Ordinal)
        )
        {
            Append(frame, now, sessions);
            return;
        }
        if (_held.Count < MaxHeldFrames)
            _held.Add((frame, now));
    }

    internal void Attach(string runId, RunLogSessionManager sessions)
    {
        _runId = runId;
        foreach (var (frame, at) in _held)
            Append(frame, at, sessions);
        _held.Clear();
    }

    private void Append(
        RunMessageStreamFrameObserved frame,
        DateTimeOffset at,
        RunLogSessionManager sessions
    )
    {
        if (frame.Message != null)
        {
            sessions.AppendEvent(
                new RunLogEvent
                {
                    Kind = RunMessageStreamKinds.Message,
                    Ts = at,
                    Message = Convert.ToBase64String(frame.Message),
                    RespondsToSeq =
                        frame.RespondsToCommand is { } number
                        && _commandSeqs.TryGetValue(number, out var seq)
                            ? seq
                            : null,
                }
            );
            return;
        }
        if (frame.Command == null)
            return;
        var entry = sessions.AppendEvent(
            new RunLogEvent
            {
                Kind = RunMessageStreamKinds.Command,
                Ts = at,
                Command = frame.Command,
                CommandArgs = string.IsNullOrEmpty(frame.CommandArgsJson)
                    ? null
                    : JObject.Parse(frame.CommandArgsJson),
            }
        );
        if (frame.CommandNumber is { } commandNumber && entry != null)
            _commandSeqs[commandNumber] = entry.Seq;
    }
}

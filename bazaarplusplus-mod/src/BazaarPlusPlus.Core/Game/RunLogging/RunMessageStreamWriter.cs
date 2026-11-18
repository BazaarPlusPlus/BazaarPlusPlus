#nullable enable
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Storage.RunLog;
using Newtonsoft.Json.Linq;

namespace BazaarPlusPlus.Game.RunLogging;

/// <summary>
/// Writes the Run Message Stream (ADR-0028) into the active run as <c>run_events</c> rows. Frames
/// that arrive before the run's start event is written (the command that starts the run, its first
/// messages, a resumed run's reconnect state) are held and written right after it. The caller
/// serializes access.
/// </summary>
internal sealed class RunMessageStreamWriter
{
    // Frames are held only between a run's first command and its start event; the bound keeps a
    // client that never starts a run from growing the list.
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

    /// <summary>The run's start event is written: its held frames and every later one go to it.</summary>
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

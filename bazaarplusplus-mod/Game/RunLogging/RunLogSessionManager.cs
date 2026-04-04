#nullable enable
using System;
using BazaarPlusPlus.Game.RunLogging.Models;
using BazaarPlusPlus.Game.RunLogging.Persistence;

namespace BazaarPlusPlus.Game.RunLogging;

public sealed class RunLogSessionManager
{
    private readonly IRunLogStore _store;
    private readonly Func<DateTimeOffset> _utcNow;

    public RunLogSessionManager(IRunLogStore store, Func<DateTimeOffset>? utcNow = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public RunLogSessionState? ActiveSession { get; private set; }

    public bool HasActiveSession => ActiveSession != null;

    public RunLogSessionState? RestoreActiveSession()
    {
        if (ActiveSession != null)
            return ActiveSession;

        ActiveSession = _store.TryResumeActiveRun();
        return ActiveSession;
    }

    public RunLogSessionState EnsureActiveSession(RunLogCreateRequest request)
    {
        ActiveSession ??= RestoreActiveSession();

        if (
            ActiveSession != null
            && !string.Equals(ActiveSession.RunId, request.RunId, StringComparison.Ordinal)
        )
        {
            _store.MarkRunAbandoned(
                ActiveSession.RunId,
                new RunLogAbandonment
                {
                    SchemaVersion = ActiveSession.SchemaVersion,
                    EndedAtUtc = _utcNow(),
                    FinalDay = ActiveSession.Day,
                    FinalHour = ActiveSession.Hour,
                    Reason = "session_mismatch",
                }
            );
            ActiveSession = null;
        }

        ActiveSession ??= _store.CreateRun(request);
        return ActiveSession;
    }

    public RunLogEvent? AppendEvent(RunLogEvent entry)
    {
        var session =
            ActiveSession ?? throw new InvalidOperationException("No active run session.");

        entry.SchemaVersion =
            entry.SchemaVersion == 0 ? session.SchemaVersion : entry.SchemaVersion;
        entry.RunId = session.RunId;
        entry.Seq = session.LastSeq + 1;
        entry.Ts = entry.Ts == default ? _utcNow() : entry.Ts;

        _store.AppendEvent(session.RunId, entry);

        session.LastSeq = entry.Seq;
        session.LastSeenAtUtc = entry.Ts;
        session.Day = entry.Day ?? session.Day;
        session.Hour = entry.Hour ?? session.Hour;

        return entry;
    }

    public RunLogCheckpoint SaveCheckpoint()
    {
        var session =
            ActiveSession ?? throw new InvalidOperationException("No active run session.");
        if (RunLoggingGameDataReader.TryBuildRunLogPlayerStats(out var stats))
        {
            session.MaxHealth = stats.MaxHealth;
            session.Prestige = stats.Prestige;
            session.Level = stats.Level;
            session.Income = stats.Income;
            session.Gold = stats.Gold;
        }

        var checkpoint = new RunLogCheckpoint
        {
            SchemaVersion = session.SchemaVersion,
            RunId = session.RunId,
            LastSeq = session.LastSeq,
            LastSeenAtUtc = session.LastSeenAtUtc,
            Day = session.Day,
            Hour = session.Hour,
            MaxHealth = session.MaxHealth,
            Prestige = session.Prestige,
            Level = session.Level,
            Income = session.Income,
            Gold = session.Gold,
            Completed = session.Completed,
        };

        _store.SaveCheckpoint(session.RunId, checkpoint);
        return checkpoint;
    }

    public void CompleteRun(RunLogCompletion completion)
    {
        var session =
            ActiveSession ?? throw new InvalidOperationException("No active run session.");
        completion.SchemaVersion =
            completion.SchemaVersion == 0 ? session.SchemaVersion : completion.SchemaVersion;
        completion.RunId = session.RunId;
        if (completion.EndedAtUtc == default)
            completion.EndedAtUtc = _utcNow();
        completion.FinalDay ??= session.Day;
        completion.FinalHour ??= session.Hour;
        completion.MaxHealth ??= session.MaxHealth;
        completion.Prestige ??= session.Prestige;
        completion.Level ??= session.Level;
        completion.Income ??= session.Income;
        completion.Gold ??= session.Gold;

        _store.CompleteRun(session.RunId, completion);
        session.Completed = true;
        ActiveSession = null;
    }

    public void MarkRunAbandoned(RunLogAbandonment abandonment)
    {
        var session =
            ActiveSession ?? throw new InvalidOperationException("No active run session.");
        abandonment.SchemaVersion =
            abandonment.SchemaVersion == 0 ? session.SchemaVersion : abandonment.SchemaVersion;
        abandonment.RunId = session.RunId;
        if (abandonment.EndedAtUtc == default)
            abandonment.EndedAtUtc = _utcNow();

        _store.MarkRunAbandoned(session.RunId, abandonment);
        session.Completed = true;
        ActiveSession = null;
    }
}

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace BazaarPlusPlus.Game.RunLogging.Models;

public sealed class RunLogPendingSelectionState
{
    public int? Day { get; set; }

    public int? Hour { get; set; }

    public string? State { get; set; }

    public string? EncounterId { get; set; }

    public string? ParentEncounterId { get; set; }

    public long SelectionSeq { get; set; }

    public IList<RunLogOptionSnapshot> Options { get; set; } = new List<RunLogOptionSnapshot>();

    public static RunLogPendingSelectionState FromEvent(RunLogEvent selectionEvent)
    {
        if (selectionEvent == null)
            throw new ArgumentNullException(nameof(selectionEvent));

        return new RunLogPendingSelectionState
        {
            Day = selectionEvent.Day,
            Hour = selectionEvent.Hour,
            State = selectionEvent.State,
            EncounterId = selectionEvent.EncounterId,
            ParentEncounterId = selectionEvent.ParentEncounterId,
            SelectionSeq = selectionEvent.Seq,
            Options = selectionEvent.Options.ToList(),
        };
    }
}

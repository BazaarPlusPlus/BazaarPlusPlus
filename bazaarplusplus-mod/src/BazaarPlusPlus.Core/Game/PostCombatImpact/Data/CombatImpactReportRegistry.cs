#nullable enable

namespace BazaarPlusPlus.Game.PostCombatImpact.Data;

public sealed class CombatImpactReportRegistry
{
    private readonly object _gate = new();
    private long _generation;
    private CombatImpactReport _published = CombatImpactReport.Empty;

    public CombatImpactReport Latest => Volatile.Read(ref _published);

    public long BeginGeneration()
    {
        lock (_gate)
        {
            _generation = unchecked(_generation + 1);
            Volatile.Write(ref _published, CombatImpactReport.Empty);
            return _generation;
        }
    }

    public bool TryPublish(long generation, CombatImpactReport report)
    {
        if (report == null)
            throw new ArgumentNullException(nameof(report));

        lock (_gate)
        {
            if (generation != _generation)
                return false;

            Volatile.Write(ref _published, report);
            return true;
        }
    }

    public void Reset() => _ = BeginGeneration();
}

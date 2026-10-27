using System.Runtime.CompilerServices;

// Test assemblies only (ADR-0012 §4): a host reaches Core through its public types.
[assembly: InternalsVisibleTo("CombatImpact.Corpus")]
[assembly: InternalsVisibleTo("CombatImpactAdapter.Tests")]
[assembly: InternalsVisibleTo("GhostBattleSync.Tests")]
[assembly: InternalsVisibleTo("HistoryPanelFiltering.Tests")]
[assembly: InternalsVisibleTo("HistoryPanelRepository.Tests")]
[assembly: InternalsVisibleTo("PostCombatImpact.Tests")]
[assembly: InternalsVisibleTo("SavedReplayPlayback.Tests")]
[assembly: InternalsVisibleTo("ReplayPayloadMaintenance.Tests")]

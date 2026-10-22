using System.Runtime.CompilerServices;

// Test projects only: hosts and Core use the public surface (ADR-0012).
[assembly: InternalsVisibleTo("BppLog.Tests")]
[assembly: InternalsVisibleTo("BundlePipeline.Tests")]
[assembly: InternalsVisibleTo("BundleV6Codec.Tests")]
[assembly: InternalsVisibleTo("CombatImpact.Corpus")]
[assembly: InternalsVisibleTo("GhostBattleSync.Tests")]
[assembly: InternalsVisibleTo("HistoryPanelFiltering.Tests")]
[assembly: InternalsVisibleTo("HistoryPanelRepository.Tests")]
[assembly: InternalsVisibleTo("ModApi.Tests")]

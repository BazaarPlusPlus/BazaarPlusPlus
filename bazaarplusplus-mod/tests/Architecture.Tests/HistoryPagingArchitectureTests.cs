#nullable enable
using Xunit;
using static Architecture.Tests.ArchitectureRules;

namespace Architecture.Tests;

public sealed class HistoryPagingArchitectureTests
{
    private const string Storage = "BazaarPlusPlus.Game.HistoryPanel.Storage";

    // HistoryPanelRepository's list and paging paths; LoadSnapshots is the one detail read
    // allowed to open snapshot documents.
    private static readonly string[] RepositoryListMethods =
    {
        "ListRuns",
        "ListBattles",
        "ListGhostBattles",
        "ListHiddenGhosts",
        "ReadCursorPage",
        "ReadCountedPage",
        "InPageRead",
        "ReadRows",
        "SeekFrom",
    };

    [Fact]
    public void Lists_do_not_read_snapshot_documents_or_payload_files()
    {
        Holds(
            "History lists page by keyset over summary columns; snapshot JSON stays a detail read.",
            build =>
            {
                var repository = build.Type(Storage + ".HistoryPanelRepository");
                var lists = RepositoryListMethods
                    .SelectMany(repository.MethodClosure)
                    .Concat(build.Type(Storage + ".HistoryPageQuery").Methods);
                return Literals(
                    lists,
                    StringComparison.OrdinalIgnoreCase,
                    "battle_snapshots",
                    "json_valid",
                    "OFFSET"
                );
            }
        );
    }
}

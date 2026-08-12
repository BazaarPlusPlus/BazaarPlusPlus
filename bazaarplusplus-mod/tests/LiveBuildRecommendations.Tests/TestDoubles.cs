#nullable enable

using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Infrastructure.RemoteEmbeddedCatalog;

namespace BazaarGameShared.Domain.Core.Types
{
    internal enum ECardSize
    {
        Small = 1,
        Medium = 2,
        Large = 3,
    }

    internal enum ETier
    {
        Bronze = 0,
        Silver = 1,
        Gold = 2,
        Diamond = 3,
        Legendary = 4,
    }

    internal enum EEnchantmentType
    {
        Burn,
        Shielded,
    }

    internal enum ECardAttributeType
    {
        None,
    }

    internal enum EContainerSocketId
    {
        Slot0 = 0,
        Slot1 = 1,
        Slot2 = 2,
        Slot3 = 3,
        Slot4 = 4,
        Slot5 = 5,
        Slot6 = 6,
        Slot7 = 7,
        Slot8 = 8,
        Slot9 = 9,
    }
}

namespace BazaarPlusPlus.Localization
{
    internal sealed class LocalizedTextSet
    {
        internal LocalizedTextSet(
            string english,
            string simplifiedChinese,
            string? traditionalChinese = null
        )
        {
            English = english;
        }

        internal string English { get; }
    }

    internal static class L
    {
        internal static string Resolve(LocalizedTextSet text) => text.English;
    }
}

namespace BazaarPlusPlus.GameInterop.StaticCards
{
    internal sealed class TestCardTemplate
    {
        internal ECardSize Size { get; init; }
    }

    internal static class BppStaticDataAccess
    {
        internal static object? TryGetReadyManagerObject() => null;

        internal static TestCardTemplate? GetCardTemplate(object? staticData, Guid templateId) =>
            null;
    }
}

namespace BazaarPlusPlus.GameInterop.ItemBoardPreview
{
    internal static class BppItemBoardSpan
    {
        internal static int Resolve(ECardSize size, int explicitSpan = 0) =>
            explicitSpan > 0 ? explicitSpan : (int)size;
    }
}

namespace BazaarPlusPlus.Game.LiveBuildPanel.Recommendations
{
    internal enum LiveBuildRefreshFailureReasonCode
    {
        RemoteEmptyResponse,
        RemoteInvalidResponse,
        RemoteRequestFailed,
        RefreshException,
    }

    internal readonly struct BuildRecommendationRemoteRefreshResult
    {
        internal static BuildRecommendationRemoteRefreshResult Success() => new();

        internal static BuildRecommendationRemoteRefreshResult Failure(
            LiveBuildRefreshFailureReasonCode reason,
            string? error,
            Exception? exception
        ) => new();
    }

    internal sealed class SnapshotCatalog : IRemoteEmbeddedCatalog<TenWinBuildCorpus>
    {
        private readonly CatalogSnapshot<TenWinBuildCorpus> _snapshot;

        internal SnapshotCatalog(TenWinBuildCorpus corpus)
        {
            _snapshot = new CatalogSnapshot<TenWinBuildCorpus>(
                corpus,
                CatalogSource.Embedded,
                DateTime.UtcNow,
                IsStale: false,
                Issue: null
            );
        }

        public bool TryGet(out CatalogSnapshot<TenWinBuildCorpus> snapshot)
        {
            snapshot = _snapshot;
            return true;
        }

        public ValueTask WarmAsync(CancellationToken cancellationToken = default) => default;

        public ValueTask<CatalogRefreshResult<TenWinBuildCorpus>> RefreshAsync(
            CancellationToken cancellationToken = default
        ) =>
            new(
                CatalogRefreshResult<TenWinBuildCorpus>.Published(
                    _snapshot with
                    {
                        Source = CatalogSource.Remote,
                    },
                    degraded: false
                )
            );

        public void Dispose() { }
    }
}

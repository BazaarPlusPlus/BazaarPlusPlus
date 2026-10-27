using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Core.Cards;
using TheBazaar;

// These shims model the native boundary, not the warmer under test. In particular,
// scope release destroys assets without knowing about any BPP-side success cache.
namespace BazaarGameShared.Domain.Core.Types
{
    internal enum ECardSize
    {
        Small,
        Medium,
        Large,
    }
}

namespace UnityEngine
{
    internal sealed class GameObject;
}

namespace TheBazaar.AppFramework
{
    internal static class Services
    {
        internal static AssetLoader? Loader { get; set; }

        internal static void TryGet<T>(out T? service)
            where T : class => service = Loader as T;
    }
}

namespace TheBazaar
{
    internal sealed class CombatSequenceMessages
    {
        internal CombatMessage CombatMessage { get; } = new();
    }

    internal sealed class CombatMessage
    {
        internal CombatData Data { get; } = new();
    }

    internal sealed class CombatData
    {
        internal string[] VfxKeys { get; set; } = [];
    }

    internal sealed class AssetLoader
    {
        internal bool CanPreload { get; set; } = true;
        private bool _preloadComplete;
        internal bool SharedLoaded { get; private set; }
        internal int SharedCalls { get; private set; }
        internal int CardCalls { get; private set; }
        internal bool FailNextCard { get; set; }
        internal bool FailNextShared { get; set; }
        internal HashSet<(Guid, ECardSize)> Cards { get; } = [];

        internal Task PreloadAssets()
        {
            SharedCalls++;
            if (_preloadComplete)
                return Task.CompletedTask;
            _preloadComplete = true;
            if (FailNextShared)
            {
                FailNextShared = false;
                throw new InvalidOperationException("shared preload failed");
            }
            SharedLoaded = true;
            return Task.CompletedTask;
        }

        internal Task PreloadCard(Guid template, ECardSize size)
        {
            CardCalls++;
            if (FailNextCard)
            {
                FailNextCard = false;
                throw new InvalidOperationException("card preload failed");
            }
            Cards.Add((template, size));
            return Task.CompletedTask;
        }

        internal void ReleaseScope()
        {
            _preloadComplete = false;
            SharedLoaded = false;
            Cards.Clear();
        }
    }
}

namespace BazaarPlusPlus.GameInterop.Cards
{
    internal static class DesktopCardFacets
    {
        internal static ECardSize GameSize(CardSize? size) =>
            size switch
            {
                CardSize.Medium => ECardSize.Medium,
                CardSize.Large => ECardSize.Large,
                _ => ECardSize.Small,
            };
    }
}

namespace BazaarPlusPlus.GameInterop.AssetLoading
{
    internal enum NativeAssetScopeIntent
    {
        Current,
    }

    internal static class NativeGlobalAssetLoader
    {
        internal static bool ReturnNull { get; set; }
        internal static int OverrideCalls { get; set; }

        internal static bool CanRunNativeSharedPreload(AssetLoader loader) => loader.CanPreload;

        internal static Task<T?> LoadByAddressAsync<T>(
            AssetLoader loader,
            string key,
            NativeAssetScopeIntent scope
        )
            where T : class, new()
        {
            OverrideCalls++;
            return Task.FromResult(ReturnNull ? null : new T());
        }
    }
}

namespace BazaarPlusPlus.Game.CombatReplay.Warmup
{
    internal static class CombatVfxWarmer
    {
        internal static ReplayWarmupStats? LastStats { get; private set; }

        internal static Task WarmCombatVfxAsync(
            CombatSequenceMessages sequence,
            ReplayWarmupStats stats,
            IReplayPlaybackOutcomeSink outcome
        )
        {
            LastStats = stats;
            return Task.CompletedTask;
        }
    }
}

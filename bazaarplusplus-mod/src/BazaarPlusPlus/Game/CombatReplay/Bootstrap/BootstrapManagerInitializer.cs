#nullable enable

using System;
using System.Reflection;
using System.Threading.Tasks;
using BazaarPlusPlus.Infrastructure;
using TheBazaar;
using TheBazaar.AppFramework;
using UnityEngine.AddressableAssets;

namespace BazaarPlusPlus.Game.CombatReplay.Bootstrap;

internal static class BootstrapManagerInitializer
{
    internal static async Task BootstrapManagersAsync()
    {
        var runManager = Services.Get<RunManager>();
        if (runManager == null)
            throw new InvalidOperationException("RunManager is unavailable.");

        var gameServiceManager = Singleton<GameServiceManager>.Instance;
        if (gameServiceManager == null)
            throw new InvalidOperationException("GameServiceManager is unavailable.");

        if (Singleton<BoardManager>.Instance != null && gameServiceManager.IsInitialized)
            return;

        var boardReferenceField = typeof(RunManager).GetField(
            "_baseBoardReference",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
        );
        var boardReference =
            boardReferenceField?.GetValue(runManager) as AssetReference
            ?? throw new MissingFieldException(typeof(RunManager).FullName, "_baseBoardReference");

        var boardBuilder = new BoardBuilder();
        runManager.BoardBuilder = boardBuilder;
        var boardManager = await boardBuilder.SetUpBoard(boardReference);
        await gameServiceManager.Init(boardManager);
    }

    internal static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException("Timed out while bootstrapping replay environment.");

            await Task.Delay(100);
        }
    }
}

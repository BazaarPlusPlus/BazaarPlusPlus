#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BazaarGameShared.Domain.Cards;
using TheBazaar;
using TheBazaar.DataManagement.Json;

namespace BazaarPlusPlus.GameInterop.StaticCards;

/// <summary>
/// <c>Data.GetStatic()</c> has shipped as both a synchronous manager return and a completed
/// task-returning accessor. This helper centralises that version seam.
/// </summary>
internal static class BppStaticDataAccess
{
    public static TCardBase? GetCardTemplate(object? staticData, Guid templateId)
    {
        if (staticData is not JsonGameDataManager manager || templateId == Guid.Empty)
            return null;

        return manager.GetCardById(templateId) as TCardBase;
    }

    /// <summary>
    /// Non-blocking handle to the static data manager. Returns the manager as an opaque object
    /// only when it is fully materialised (created, and any task-returning <c>GetStatic()</c>
    /// already completed); returns <c>null</c> otherwise. Never blocks the main thread waiting
    /// on the static-data task.
    /// </summary>
    public static object? TryGetReadyManagerObject()
    {
        if (!Data.IsManagerCreated())
            return null;

        object? staticData = Data.GetStatic();
        if (staticData is Task<JsonGameDataManager> task)
            return task.IsCompleted ? task.Result : null;

        return staticData as JsonGameDataManager;
    }

    /// <summary>
    /// Materialises the full card map (<c>JsonGameDataManager.GetCardMap()</c> → <c>ReadAllCards</c>:
    /// a full-table SQLite read plus polymorphic JSON deserialize — the dominant first-open cost).
    /// Intended to run on a worker thread: the game opens its own SQLite connection, deserializes
    /// on PLINQ workers, builds a fresh dictionary, and publishes it via an atomic reference
    /// assignment, so calling it off the main thread does not tear the shared map. <paramref
    /// name="source"/> must come from <see cref="TryGetReadyManagerObject"/>.
    /// </summary>
    public static Dictionary<Guid, ITCard>? LoadCardMap(object? source) =>
        source is JsonGameDataManager manager ? manager.GetCardMap() : null;
}

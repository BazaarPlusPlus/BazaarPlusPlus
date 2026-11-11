using BazaarPlusPlus.Android.Patches;
using BazaarPlusPlus.Core.Cards.CardRental;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using Il2CppTheBazaar.UI;
using UnityEngine;

namespace BazaarPlusPlus.Android.GameInterop.CardRental;

/// <summary>
/// The host's one native card rental (#396), shared by every panel that shows the game's own
/// cards. A panel rents a <see cref="CardRentalSubject"/> through <see cref="Core"/>, which the
/// Mobile Platform Port hands it, reads the lease each frame, and returns it when the card
/// leaves its view. The melon ticks the rental every frame and releases everything on a scene
/// load; a panel switched off releases its own leases.
/// </summary>
internal static class AndroidCardRental
{
    private static readonly AndroidCardRentalBackend Backend = new();

    /// <summary>The rental itself, which the Mobile Platform Port hands the Mobile UI.</summary>
    internal static readonly CardRentalCore<CardPreviewBase, RectTransform> Core = new(Backend);

    /// <summary>Lets the rental set a template up without looking it up in the game's data again.</summary>
    public static void Remember(
        Guid templateId,
        Il2CppBazaarGameShared.Domain.Cards.TCardBase template
    ) => Backend.Remember(templateId, template);

    /// <summary>Main thread, every frame: polls the native instantiate and setup tasks.</summary>
    public static void Tick()
    {
        try
        {
            Core.Tick();
        }
        catch (Exception exception)
        {
            AndroidPatchHost.ReportProbeError("card_rental", exception);
        }
    }

    /// <summary>
    /// A scene load or a feature switching off: every lease ends and every instance, held or
    /// pooled, is destroyed, so none outlives the scene or the panel that rented it.
    /// </summary>
    public static void ReleaseAll(string reason)
    {
        var destroyedBefore = Core.Destroyed;
        var released = Core.ReleaseAll();
        var destroyed = Core.Destroyed - destroyedBefore;
        if (released == 0 && destroyed == 0)
            return;
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.card_rental.released"),
            [("reason", reason), ("leases", released), ("destroyed_now", destroyed), .. Summary()]
        );
    }

    /// <summary>The counts the periodic hook summary and the release log carry.</summary>
    public static BppLogField[] Summary() =>
        [
            ("card_rented", Core.Rented),
            ("card_returned", Core.Returned),
            ("card_rent_failed", Core.Failed),
            ("card_instantiated", Core.Instantiated),
            ("card_pool_hits", Core.PoolHits),
            ("card_destroyed", Core.Destroyed),
            ("card_lost", Core.Lost),
            ("card_leases", Core.ActiveLeases),
            ("card_held", Core.Held),
            ("card_pooled", Core.Pooled),
            ("card_balanced", Core.IsBalanced),
        ];
}

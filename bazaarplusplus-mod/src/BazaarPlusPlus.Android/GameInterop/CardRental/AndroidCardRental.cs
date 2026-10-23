using BazaarPlusPlus.Android.Patches;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using Il2CppTheBazaar.UI;
using UnityEngine;

namespace BazaarPlusPlus.Android.GameInterop.CardRental;

/// <summary>
/// The host's one native card rental (#396), shared by every panel that shows the game's own
/// cards: Collection today, LiveBuild (#397) and History boards (#399) next. A panel rents a
/// <see cref="CardRentalSubject"/> under one of its RectTransforms, reads the lease each frame,
/// and returns it when the card leaves its view. The melon ticks the rental every frame and
/// releases everything on a scene load; a panel switched off releases its own leases.
/// </summary>
internal static class AndroidCardRental
{
    private static readonly AndroidCardRentalBackend Backend = new();
    private static readonly CardRentalCore<CardPreviewBase, RectTransform> Core = new(Backend);

    public static int ActiveLeases => Core.ActiveLeases;

    public static bool IsBalanced => Core.IsBalanced;

    /// <summary>Main thread. The lease is Pending until its card is set up and shown.</summary>
    public static CardRentalLease<CardPreviewBase> Rent(
        CardRentalSubject subject,
        RectTransform parent,
        string owner
    ) => Core.Rent(subject, parent, owner);

    /// <summary>Main thread. Sets the lease's card up again for another tier or enchantment.</summary>
    public static void Retarget(
        CardRentalLease<CardPreviewBase> lease,
        CardRentalSubject subject
    ) => Core.Retarget(lease, subject);

    /// <summary>Main thread. Null and already released leases are ignored.</summary>
    public static void Return(CardRentalLease<CardPreviewBase>? lease) => Core.Return(lease);

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
        var released = Core.ReleaseAll();
        if (released == 0 && Core.Instantiated == Core.Destroyed + Core.Lost)
            return;
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.card_rental.released"),
            [("reason", reason), ("leases", released), .. Summary()]
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

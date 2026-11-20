using BazaarPlusPlus.Android.Patches;
using BazaarPlusPlus.Core.Cards.CardRental;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using Il2CppTheBazaar.UI;
using UnityEngine;

namespace BazaarPlusPlus.Android.GameInterop.CardRental;

internal static class AndroidCardRental
{
    private static readonly AndroidCardRentalBackend Backend = new();

    internal static readonly CardRentalCore<CardPreviewBase, RectTransform> Core = new(Backend);

    public static void Remember(
        Guid templateId,
        Il2CppBazaarGameShared.Domain.Cards.TCardBase template
    ) => Backend.Remember(templateId, template);

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

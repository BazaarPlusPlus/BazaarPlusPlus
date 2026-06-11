#nullable enable
using System;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Infrastructure;
using UnityEngine;

namespace BazaarPlusPlus.Game.CollectionPanel;

// Custom mountable for CollectionPanel: ComponentMount<T> would attach the MonoBehaviour but
// could not subscribe to ChineseLocaleModeChanged, which the catalog cache + UI labels need
// in order to regenerate when the user cycles BPP's Chinese variant.
internal sealed class CollectionPanelMount : IBppMountable
{
    private IDisposable? _localeChangedSubscription;

    public void Mount(GameObject host, IBppServices services)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        var panel = host.AddComponent<CollectionPanel>();
        panel.Initialize(services);

        _localeChangedSubscription = services.EventBus.Subscribe<ChineseLocaleModeChanged>(_ =>
            CollectionPanel.NotifyLocaleChanged()
        );
        BppLog.Info("CollectionPanelMount", "CollectionPanel mounted.");
    }

    public void Unmount(GameObject host)
    {
        _localeChangedSubscription?.Dispose();
        _localeChangedSubscription = null;

        var panel = host.GetComponent<CollectionPanel>();
        if (panel != null)
            UnityEngine.Object.DestroyImmediate(panel);
    }
}

using System;
using System.Threading.Tasks;
using TheBazaar;
using UnityEngine;

namespace BazaarPlusPlus.Game.MonsterPreview;

internal sealed class MonsterPreviewWarmupController : MonoBehaviour
{
    private bool _started;

    private async void Start()
    {
        if (_started)
            return;

        _started = true;

        await Task.Yield();

        var catalogReady = LocalCardTemplateCatalog.Warm();
        var attributesReady = ItemAttr.Warm();
        var staticDataReady = await WarmStaticDataAsync();

        BppLog.Info(
            "MonsterPreviewWarmupController",
            $"Warmup finished catalogReady={catalogReady} attributesReady={attributesReady} staticDataReady={staticDataReady}"
        );
    }

    private static async Task<bool> WarmStaticDataAsync()
    {
        try
        {
            return await Data.GetStatic() != null;
        }
        catch (Exception ex)
        {
            BppLog.Error("MonsterPreviewWarmupController", "Static data warmup failed", ex);
            return false;
        }
    }
}

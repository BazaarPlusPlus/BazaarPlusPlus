#nullable enable
using BazaarGameShared.Domain.Cards.Socket;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Effect.AuraActions;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.GameInterop.Cards;
using BazaarPlusPlus.GameInterop.StaticCards;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.HistoryPanel;

/// <summary>
/// History's card templates on the Mono host: the game's static data, read on each lookup, so
/// nothing is built ahead. A socket effect's attribute is cached per template and tier.
/// </summary>
internal sealed class DesktopHistoryCardTemplates : IHistoryCardTemplates
{
    public static readonly DesktopHistoryCardTemplates Instance = new();

    private static readonly object SocketEffectTemplateLock = new();
    private static readonly Dictionary<
        (Guid TemplateId, ETier Tier),
        string?
    > SocketEffectAttributeCache = new();
    private static object? _staticGameData;

    private DesktopHistoryCardTemplates() { }

    public void EnsureBuilt() { }

    public bool Contains(Guid templateId) =>
        templateId != Guid.Empty
        && BppStaticDataAccess.GetCardTemplate(TryGetStaticGameData(), templateId) != null;

    public string? SocketEffectAttribute(Guid templateId, CardTier tier)
    {
        var nativeTier = DesktopCardFacets.Tier.TryToSource(tier, out var source)
            ? source
            : ETier.Bronze;
        var cacheKey = (templateId, nativeTier);
        lock (SocketEffectTemplateLock)
        {
            if (SocketEffectAttributeCache.TryGetValue(cacheKey, out var cached))
                return cached;
        }

        string? resolved = null;
        try
        {
            if (
                BppStaticDataAccess.GetCardTemplate(GetStaticGameData(), templateId)
                is TCardSocketEffect template
            )
            {
                foreach (var aura in template.GetAuraTemplatesByTier(nativeTier))
                {
                    if (
                        aura?.Action is TAuraActionCardModifyAttribute action
                        && (
                            action.AttributeType == ECardAttributeType.Heated
                            || action.AttributeType == ECardAttributeType.Chilled
                        )
                    )
                    {
                        resolved = action.AttributeType.ToString();
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.HistoryPanel,
                    "history_panel.preview.socket_effect_degraded",
                    storm: ["reason_code"]
                ),
                ex,
                ("template_id", templateId),
                ("reason_code", HistoryPanelPreviewReasonCode.SocketEffectLookupFailed)
            );
        }

        lock (SocketEffectTemplateLock)
            SocketEffectAttributeCache[cacheKey] = resolved;
        return resolved;
    }

    private static object? TryGetStaticGameData()
    {
        try
        {
            return GetStaticGameData();
        }
        catch (Exception ex)
        {
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.HistoryPanel,
                    "history_panel.preview.static_data_degraded",
                    storm: ["reason_code"]
                ),
                ex,
                ("reason_code", HistoryPanelPreviewReasonCode.StaticDataAccessFailed)
            );
            return null;
        }
    }

    private static object? GetStaticGameData()
    {
        lock (SocketEffectTemplateLock)
        {
            if (_staticGameData != null)
                return _staticGameData;
        }

        var staticData = BppStaticDataAccess.TryGetReadyManagerObject();
        if (staticData == null)
        {
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.HistoryPanel,
                    "history_panel.preview.static_data_degraded",
                    storm: ["reason_code"]
                ),
                ("reason_code", HistoryPanelPreviewReasonCode.StaticDataUnavailable)
            );
            return null;
        }

        lock (SocketEffectTemplateLock)
        {
            _staticGameData ??= staticData;
            return _staticGameData;
        }
    }
}

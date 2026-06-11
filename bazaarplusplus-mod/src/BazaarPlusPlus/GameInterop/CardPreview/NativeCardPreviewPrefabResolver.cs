#nullable enable
using System.Collections.Generic;
using System.Reflection;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Infrastructure;
using HarmonyLib;
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.CardPreview;

internal static class NativeCardPreviewPrefabResolver
{
    private const string MonsterBoardTooltipTypeName = "TheBazaar.UI.Tooltips.MonsterBoardTooltip";

    private static readonly object Lock = new();
    private static readonly Dictionary<NativeCardPreviewKind, Component> PrefabRefs = new();
    private static NativeCardPreviewSocketTemplate[]? _socketTemplates;
    private static bool _resolved;

    private static readonly System.Type? MonsterBoardTooltipType = AccessTools.TypeByName(
        MonsterBoardTooltipTypeName
    );

    private static readonly FieldInfo? SmallItemReferenceField =
        MonsterBoardTooltipType != null
            ? AccessTools.Field(MonsterBoardTooltipType, "_smallItemReference")
            : null;

    private static readonly FieldInfo? MediumItemReferenceField =
        MonsterBoardTooltipType != null
            ? AccessTools.Field(MonsterBoardTooltipType, "_mediumItemReference")
            : null;

    private static readonly FieldInfo? LargeItemReferenceField =
        MonsterBoardTooltipType != null
            ? AccessTools.Field(MonsterBoardTooltipType, "_largeItemReference")
            : null;

    private static readonly FieldInfo? SkillReferenceField =
        MonsterBoardTooltipType != null
            ? AccessTools.Field(MonsterBoardTooltipType, "_skillReference")
            : null;

    private static readonly FieldInfo? SocketsField =
        MonsterBoardTooltipType != null
            ? AccessTools.Field(MonsterBoardTooltipType, "_sockets")
            : null;

    public static bool TryGetPrefab(
        NativeCardPreviewKind kind,
        bool requireSkill,
        bool requireSockets,
        string logComponent,
        out Component? prefab
    )
    {
        prefab = null;
        if (!TryEnsureResolved(requireSkill, requireSockets, logComponent))
            return false;

        lock (Lock)
            return PrefabRefs.TryGetValue(kind, out prefab) && prefab != null;
    }

    public static NativeCardPreviewSocketTemplate[]? TryGetSocketTemplates()
    {
        lock (Lock)
            return _resolved ? _socketTemplates : null;
    }

    public static bool TryEnsureResolved(
        bool requireSkill,
        bool requireSockets,
        string logComponent
    )
    {
        lock (Lock)
        {
            if (_resolved && HasRequiredRefs(requireSkill, requireSockets))
                return true;
        }

        if (
            MonsterBoardTooltipType == null
            || SmallItemReferenceField == null
            || MediumItemReferenceField == null
            || LargeItemReferenceField == null
            || (requireSkill && SkillReferenceField == null)
            || (requireSockets && SocketsField == null)
        )
        {
            BppLog.Warn(
                logComponent,
                $"{MonsterBoardTooltipTypeName} reflection metadata missing; native card preview unavailable on this game version."
            );
            return false;
        }

        var tooltips = Resources.FindObjectsOfTypeAll(MonsterBoardTooltipType);
        if (tooltips == null || tooltips.Length == 0)
            return false;

        foreach (var tooltipObj in tooltips)
        {
            if (tooltipObj is not Component tooltip || tooltip == null)
                continue;

            var small = SmallItemReferenceField.GetValue(tooltip) as Component;
            var medium = MediumItemReferenceField.GetValue(tooltip) as Component;
            var large = LargeItemReferenceField.GetValue(tooltip) as Component;
            var skill = SkillReferenceField?.GetValue(tooltip) as Component;
            var sockets = SocketsField?.GetValue(tooltip) as RectTransform[];

            if (
                small == null
                || medium == null
                || large == null
                || (requireSkill && skill == null)
                || (requireSockets && (sockets == null || sockets.Length == 0))
            )
                continue;

            lock (Lock)
            {
                PrefabRefs[NativeCardPreviewKind.ForItem(ECardSize.Small)] = small;
                PrefabRefs[NativeCardPreviewKind.ForItem(ECardSize.Medium)] = medium;
                PrefabRefs[NativeCardPreviewKind.ForItem(ECardSize.Large)] = large;
                if (skill != null)
                    PrefabRefs[NativeCardPreviewKind.ForSkill()] = skill;
                if (sockets != null && sockets.Length > 0)
                    _socketTemplates = CaptureSocketTemplates(sockets);
                _resolved = true;
            }

            BppLog.Info(
                logComponent,
                $"Acquired native card preview refs (small='{small.name}', medium='{medium.name}', large='{large.name}', skill='{skill?.name ?? "(none)"}', sockets={sockets?.Length ?? 0})."
            );
            return true;
        }

        return false;
    }

    private static bool HasRequiredRefs(bool requireSkill, bool requireSockets)
    {
        if (!PrefabRefs.ContainsKey(NativeCardPreviewKind.ForItem(ECardSize.Small)))
            return false;
        if (!PrefabRefs.ContainsKey(NativeCardPreviewKind.ForItem(ECardSize.Medium)))
            return false;
        if (!PrefabRefs.ContainsKey(NativeCardPreviewKind.ForItem(ECardSize.Large)))
            return false;
        if (requireSkill && !PrefabRefs.ContainsKey(NativeCardPreviewKind.ForSkill()))
            return false;
        if (requireSockets && (_socketTemplates == null || _socketTemplates.Length == 0))
            return false;
        return true;
    }

    private static NativeCardPreviewSocketTemplate[] CaptureSocketTemplates(RectTransform[] sockets)
    {
        var templates = new NativeCardPreviewSocketTemplate[sockets.Length];
        for (var i = 0; i < sockets.Length; i++)
        {
            var socket = sockets[i];
            if (socket == null)
            {
                templates[i] = new NativeCardPreviewSocketTemplate(
                    Vector2.zero,
                    new Vector2(160f, 220f),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f)
                );
                continue;
            }

            templates[i] = new NativeCardPreviewSocketTemplate(
                socket.anchoredPosition,
                socket.sizeDelta,
                socket.anchorMin,
                socket.anchorMax,
                socket.pivot
            );
        }
        return templates;
    }
}

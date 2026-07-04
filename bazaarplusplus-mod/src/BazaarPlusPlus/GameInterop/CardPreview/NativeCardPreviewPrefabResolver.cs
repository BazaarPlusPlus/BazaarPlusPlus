#nullable enable
using System.Collections.Generic;
using System.Reflection;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Infrastructure;
using HarmonyLib;
using TheBazaar.AppFramework;
using UnityEngine.AddressableAssets;
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.CardPreview;

internal static class NativeCardPreviewPrefabResolver
{
    private const string MonsterBoardTooltipTypeName = "TheBazaar.UI.Tooltips.MonsterBoardTooltip";

    private static readonly object Lock = new();
    private static readonly Dictionary<NativeCardPreviewKind, Component> PrefabRefs = new();
    private static NativeCardPreviewSocketTemplate[]? _socketTemplates;
    private static bool _resolved;
    private static bool _missingMetadataLogged;
    private static bool _assetLoaderFailureLogged;

    private static readonly System.Type? MonsterBoardTooltipType = AccessTools.TypeByName(
        MonsterBoardTooltipTypeName
    );

    private static readonly FieldInfo? SmallItemReferenceField = FindMonsterBoardTooltipField(
        "_smallItemReference"
    );

    private static readonly FieldInfo? MediumItemReferenceField = FindMonsterBoardTooltipField(
        "_mediumItemReference"
    );

    private static readonly FieldInfo? LargeItemReferenceField = FindMonsterBoardTooltipField(
        "_largeItemReference"
    );

    private static readonly FieldInfo? SkillReferenceField = FindMonsterBoardTooltipField(
        "_skillReference"
    );

    private static readonly FieldInfo? SocketsField = FindMonsterBoardTooltipField("_sockets");

    private static readonly FieldInfo? SmallCardUiAssetRefField = FindAssetLoaderField(
        "SmallCardUIAssetRef"
    );

    private static readonly FieldInfo? MediumCardUiAssetRefField = FindAssetLoaderField(
        "MediumCardUIAssetRef"
    );

    private static readonly FieldInfo? LargeCardUiAssetRefField = FindAssetLoaderField(
        "LargeCardUIAssetRef"
    );

    private static readonly FieldInfo? SkillUiAssetRefField = FindAssetLoaderField(
        "SkillUIAssetRef"
    );

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

        if (TryResolveFromAssetLoader(requireSkill, requireSockets, logComponent))
            return true;

        if (!HasTooltipMetadata(requireSkill, requireSockets))
            return LogMissingMetadataOnce(logComponent);

        var tooltips = Resources.FindObjectsOfTypeAll(MonsterBoardTooltipType);
        if (tooltips == null || tooltips.Length == 0)
            return false;

        foreach (var tooltipObj in tooltips)
        {
            if (tooltipObj is not Component tooltip || tooltip == null)
                continue;

            var small = SmallItemReferenceField!.GetValue(tooltip) as Component;
            var medium = MediumItemReferenceField!.GetValue(tooltip) as Component;
            var large = LargeItemReferenceField!.GetValue(tooltip) as Component;
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

    private static bool TryResolveFromAssetLoader(
        bool requireSkill,
        bool requireSockets,
        string logComponent
    )
    {
        if (
            SmallCardUiAssetRefField == null
            || MediumCardUiAssetRefField == null
            || LargeCardUiAssetRefField == null
            || (requireSkill && SkillUiAssetRefField == null)
        )
            return false;

        if (!Services.TryGet<AssetLoader>(out var assetLoader) || assetLoader == null)
            return false;

        try
        {
            var small = LoadPreviewPrefab(assetLoader, SmallCardUiAssetRefField);
            var medium = LoadPreviewPrefab(assetLoader, MediumCardUiAssetRefField);
            var large = LoadPreviewPrefab(assetLoader, LargeCardUiAssetRefField);
            var skill = SkillUiAssetRefField != null
                ? LoadPreviewPrefab(assetLoader, SkillUiAssetRefField)
                : null;

            if (
                small == null
                || medium == null
                || large == null
                || (requireSkill && skill == null)
                || (requireSockets && (_socketTemplates == null || _socketTemplates.Length == 0))
            )
                return false;

            lock (Lock)
            {
                PrefabRefs[NativeCardPreviewKind.ForItem(ECardSize.Small)] = small;
                PrefabRefs[NativeCardPreviewKind.ForItem(ECardSize.Medium)] = medium;
                PrefabRefs[NativeCardPreviewKind.ForItem(ECardSize.Large)] = large;
                if (skill != null)
                    PrefabRefs[NativeCardPreviewKind.ForSkill()] = skill;
                _resolved = true;
            }

            BppLog.Info(
                logComponent,
                $"Acquired native card preview refs from AssetLoader (small='{small.name}', medium='{medium.name}', large='{large.name}', skill='{skill?.name ?? "(none)"}')."
            );
            return true;
        }
        catch (System.Exception ex)
        {
            if (!_assetLoaderFailureLogged)
            {
                _assetLoaderFailureLogged = true;
                BppLog.Warn(
                    logComponent,
                    $"AssetLoader card preview prefab resolution failed: {ex.Message}"
                );
            }
            return false;
        }
    }

    private static Component? LoadPreviewPrefab(AssetLoader assetLoader, FieldInfo field)
    {
        if (field.GetValue(assetLoader) is not AssetReference assetReference)
            return null;
        if (!assetReference.RuntimeKeyIsValid())
            return null;

        var handle = Addressables.LoadAssetAsync<GameObject>(assetReference);
        var prefab = handle.WaitForCompletion();
        if (prefab == null || NativeCardPreviewReflection.CardPreviewBaseType == null)
            return null;

        return prefab.GetComponent(NativeCardPreviewReflection.CardPreviewBaseType);
    }

    private static bool HasTooltipMetadata(bool requireSkill, bool requireSockets)
    {
        return MonsterBoardTooltipType != null
            && SmallItemReferenceField != null
            && MediumItemReferenceField != null
            && LargeItemReferenceField != null
            && (!requireSkill || SkillReferenceField != null)
            && (!requireSockets || SocketsField != null);
    }

    private static bool LogMissingMetadataOnce(string logComponent)
    {
        if (!_missingMetadataLogged)
        {
            _missingMetadataLogged = true;
            BppLog.Warn(
                logComponent,
                $"{MonsterBoardTooltipTypeName} and AssetLoader reflection metadata missing; native card preview unavailable on this game version."
            );
        }
        return false;
    }

    private static FieldInfo? FindMonsterBoardTooltipField(string fieldName)
    {
        return MonsterBoardTooltipType?.GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
        );
    }

    private static FieldInfo? FindAssetLoaderField(string fieldName)
    {
        return typeof(AssetLoader).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
        );
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

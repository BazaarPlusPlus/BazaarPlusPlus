#nullable enable
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Item;
using BazaarGameShared.Domain.Cards.Skill;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.GameInterop.Cards;
using BazaarPlusPlus.GameInterop.StaticCards;
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.CardPreview;

internal sealed class NativeCardPreviewResource
{
    internal NativeCardPreviewResource(
        Component card,
        RectTransform rect,
        NativeCardPreviewKind kind,
        NativeCardPreviewSubject subject,
        INativeCardPreviewOwner owner,
        NativeCardPreviewPresentation presentation
    )
    {
        Card = card;
        Root = card.gameObject;
        Rect = rect;
        Kind = kind;
        Subject = subject;
        Owner = owner;
        Presentation = presentation;
    }

    internal Component Card { get; }
    internal GameObject Root { get; }
    internal RectTransform Rect { get; }
    internal NativeCardPreviewKind Kind { get; }
    internal NativeCardPreviewSubject Subject { get; }
    internal INativeCardPreviewOwner Owner { get; }
    internal NativeCardPreviewPresentation Presentation { get; }

    internal NativeCardPreviewOwnerContext CreateOwnerContext()
    {
        NativeCardPreviewReflection.TryGetTooltipData(
            Card,
            out var tooltipData,
            failure =>
            {
                try
                {
                    Owner.ReportFailure(failure);
                }
                catch
                {
                    // Diagnostics cannot prevent the owner from receiving its release hook.
                }
            }
        );
        return new NativeCardPreviewOwnerContext(Root, Rect, Subject, tooltipData);
    }
}

internal readonly record struct NativeCardPreviewCoreAcquireOutcome(
    NativeCardPreviewResource? Resource,
    NativeCardPreviewFailure? Failure
);

internal sealed class NativeCardPreviewFactory
{
    private readonly NativeCardPreviewPool _pool;
    private readonly NativeCardPreviewAssetLoader _assetLoader = new();
    private int _instanceCounter;

    internal NativeCardPreviewFactory(NativeCardPreviewPool pool) =>
        _pool = pool ?? throw new ArgumentNullException(nameof(pool));

    internal NativeCardMeasureResult Measure(NativeCardPreviewSubject? subject)
    {
        if (!TryResolveTemplate(subject, out var template, out var failure))
        {
            return new NativeCardMeasureResult(
                failure == null
                    ? NativeCardMeasureStatus.Unavailable
                    : NativeCardMeasureStatus.Failed,
                1,
                failure
            );
        }

        return new NativeCardMeasureResult(
            NativeCardMeasureStatus.Measured,
            CardSizeSpan.Resolve(template.Size),
            null
        );
    }

    internal async Task<NativeCardPreviewCoreAcquireOutcome> AcquireAsync(
        NativeCardPreviewSubject? subject,
        INativeCardPreviewOwner owner,
        CancellationToken token = default
    )
    {
        if (!TryResolveTemplate(subject, out var template, out var resolveFailure))
            return new NativeCardPreviewCoreAcquireOutcome(null, resolveFailure);
        if (!TryResolveKind(template, out var kind))
        {
            return Failed(
                new NativeCardPreviewFailure(
                    NativeCardPreviewOperation.ResolveKind,
                    NativeCardPreviewFailureReason.UnsupportedCardType,
                    template.Id
                )
            );
        }

        var instance = BuildSyntheticInstance(subject!, kind, ++_instanceCounter);

        Transform? parent;
        try
        {
            parent = owner.ResolveParent(subject!);
        }
        catch (Exception ex)
        {
            return Failed(OwnerFailure(NativeCardPreviewOperation.OwnerPrepare, template.Id, ex));
        }
        if (parent == null)
            return default;

        NativeCardPreviewFailure? instantiateFailure = null;
        var card = await _pool.TakeInactiveAsync(
            kind,
            parent,
            async () =>
            {
                var outcome = await _assetLoader.InstantiateInactiveCardAsync(
                    template,
                    parent,
                    token
                );
                instantiateFailure = outcome.Failure;
                return outcome.Card;
            },
            token
        );
        if (card == null)
            return new NativeCardPreviewCoreAcquireOutcome(null, instantiateFailure);

        var prepared = false;
        var ownsCard = true;
        try
        {
            var rect = card.transform as RectTransform ?? card.GetComponent<RectTransform>();
            if (rect == null)
            {
                return Failed(
                    new NativeCardPreviewFailure(
                        NativeCardPreviewOperation.ResolveRect,
                        NativeCardPreviewFailureReason.RectUnavailable,
                        template.Id
                    )
                );
            }

            NativeCardPreviewPresentation presentation;
            try
            {
                presentation = NativeCardPreviewPresentation.Create(card);
            }
            catch (Exception ex)
            {
                return Failed(
                    new NativeCardPreviewFailure(
                        NativeCardPreviewOperation.Show,
                        NativeCardPreviewFailureReason.Unexpected,
                        template.Id,
                        ex
                    )
                );
            }

            try
            {
                prepared = true;
                owner.PrepareWhileInactive(
                    new NativeCardPreviewOwnerContext(card.gameObject, rect, subject!, null)
                );
            }
            catch (Exception ex)
            {
                return Failed(
                    OwnerFailure(NativeCardPreviewOperation.OwnerPrepare, template.Id, ex)
                );
            }

            card.transform.localScale = Vector3.one;
            card.transform.localRotation = Quaternion.identity;
            card.gameObject.SetActive(true);
            NativeCardPreviewReflection.ApplyLayerRecursive(card.gameObject, owner.Layer);

            var setUpFailure = await NativeCardPreviewRuntime.InvokeSetUpSafe(
                card,
                template,
                instance,
                token
            );
            if (setUpFailure != null)
                return Failed(setUpFailure);

            token.ThrowIfCancellationRequested();
            var resizeFailure = NativeCardPreviewRuntime.Resize(card, template.Id);
            if (resizeFailure != null)
                return Failed(resizeFailure);

            var resource = new NativeCardPreviewResource(
                card,
                rect,
                kind,
                subject!,
                owner,
                presentation
            );
            try
            {
                owner.OnAcquired(resource.CreateOwnerContext());
            }
            catch (Exception ex)
            {
                return Failed(
                    OwnerFailure(NativeCardPreviewOperation.OwnerAcquired, template.Id, ex)
                );
            }

            ownsCard = false;
            return new NativeCardPreviewCoreAcquireOutcome(resource, null);
        }
        finally
        {
            if (ownsCard)
            {
                try
                {
                    if (prepared)
                    {
                        try
                        {
                            var rect =
                                card.transform as RectTransform
                                ?? card.GetComponent<RectTransform>();
                            if (rect != null)
                            {
                                NativeCardPreviewReflection.TryGetTooltipData(
                                    card,
                                    out var tooltipData
                                );
                                owner.BeforeRelease(
                                    new NativeCardPreviewOwnerContext(
                                        card.gameObject,
                                        rect,
                                        subject!,
                                        tooltipData
                                    )
                                );
                            }
                        }
                        catch (Exception ex)
                        {
                            try
                            {
                                owner.ReportFailure(
                                    OwnerFailure(
                                        NativeCardPreviewOperation.OwnerRelease,
                                        template.Id,
                                        ex
                                    )
                                );
                            }
                            catch
                            {
                                // Diagnostics cannot interrupt mandatory native cleanup.
                            }
                        }
                    }
                }
                finally
                {
                    NativeCardPreviewPool.Destroy(card);
                }
            }
        }
    }

    private static bool TryResolveTemplate(
        NativeCardPreviewSubject? subject,
        out TCardBase template,
        out NativeCardPreviewFailure? failure
    )
    {
        template = null!;
        failure = null;
        if (subject == null || subject.TemplateId == Guid.Empty)
            return false;

        var staticData = BppStaticDataAccess.TryGetReadyManagerObject();
        if (staticData == null)
        {
            failure = new NativeCardPreviewFailure(
                NativeCardPreviewOperation.ResolveTemplate,
                NativeCardPreviewFailureReason.StaticDataUnavailable,
                subject.TemplateId
            );
            return false;
        }

        var resolved = BppStaticDataAccess.GetCardTemplate(staticData, subject.TemplateId);
        if (resolved == null)
        {
            failure = new NativeCardPreviewFailure(
                NativeCardPreviewOperation.ResolveTemplate,
                NativeCardPreviewFailureReason.TemplateUnavailable,
                subject.TemplateId
            );
            return false;
        }

        template = resolved;
        return true;
    }

    private static bool TryResolveKind(TCardBase template, out NativeCardPreviewKind kind)
    {
        kind = default;
        if (template.Type == ECardType.Skill)
        {
            kind = NativeCardPreviewKind.ForSkill();
            return true;
        }
        if (template.Type != ECardType.Item)
            return false;

        kind = NativeCardPreviewKind.ForItem(ResolveCardSize(template.Size));
        return true;
    }

    private static TCardInstance BuildSyntheticInstance(
        NativeCardPreviewSubject subject,
        NativeCardPreviewKind kind,
        int index
    )
    {
        var attributes =
            subject.Attributes != null
                ? new Dictionary<ECardAttributeType, int>(subject.Attributes)
                : new Dictionary<ECardAttributeType, int>();
        var instanceId = $"{subject.InstanceIdPrefix}-{Mathf.Max(0, index)}";
        if (kind.Type == ECardType.Skill)
        {
            return new TCardInstanceSkill
            {
                TemplateId = subject.TemplateId,
                TemplateVersion = string.Empty,
                InstanceId = instanceId,
                Tier = subject.Tier,
                Attributes = attributes,
            };
        }

        return new TCardInstanceItem
        {
            TemplateId = subject.TemplateId,
            TemplateVersion = string.Empty,
            InstanceId = instanceId,
            Tier = subject.Tier,
            SocketId = subject.SocketId ?? (EContainerSocketId)Mathf.Clamp(index, 0, 9),
            EnchantmentType = subject.EnchantmentType,
            Attributes = attributes,
        };
    }

    private static ECardSize ResolveCardSize(ECardSize size) =>
        size switch
        {
            ECardSize.Small => ECardSize.Small,
            ECardSize.Medium => ECardSize.Medium,
            ECardSize.Large => ECardSize.Large,
            _ => ECardSize.Small,
        };

    private static NativeCardPreviewCoreAcquireOutcome Failed(NativeCardPreviewFailure failure) =>
        new(null, failure);

    private static NativeCardPreviewFailure OwnerFailure(
        NativeCardPreviewOperation operation,
        Guid templateId,
        Exception exception
    ) => new(operation, NativeCardPreviewFailureReason.OwnerHookException, templateId, exception);
}

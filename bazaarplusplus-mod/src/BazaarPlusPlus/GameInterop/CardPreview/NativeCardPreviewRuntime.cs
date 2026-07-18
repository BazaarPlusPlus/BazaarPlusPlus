#nullable enable
using System.Reflection;
using System.Runtime.ExceptionServices;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Core.Types;
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.CardPreview;

internal static class NativeCardPreviewRuntime
{
    public static ECardSize ResolveCardSize(Component card)
    {
        var sizeProperty = NativeCardPreviewReflection.SizeProperty;
        if (sizeProperty == null)
            return ECardSize.Small;

        try
        {
            var raw = sizeProperty.GetValue(card);
            if (raw is int intValue)
            {
                return intValue switch
                {
                    1 => ECardSize.Small,
                    2 => ECardSize.Medium,
                    3 => ECardSize.Large,
                    _ => ECardSize.Small,
                };
            }
        }
        catch
        {
            // fall through
        }

        return ECardSize.Small;
    }

    public static NativeCardPreviewFailure? Resize(Component card, Guid? templateId)
    {
        var method = NativeCardPreviewReflection.ResizeMethod;
        if (method == null)
            return new NativeCardPreviewFailure(
                NativeCardPreviewOperation.Resize,
                NativeCardPreviewFailureReason.ReflectionUnavailable,
                templateId
            );

        try
        {
            method.Invoke(card, Array.Empty<object>());
        }
        catch (Exception ex)
        {
            return new NativeCardPreviewFailure(
                NativeCardPreviewOperation.Resize,
                NativeCardPreviewFailureReason.ResizeException,
                templateId,
                ex
            );
        }
        return null;
    }

    public static NativeCardPreviewFailure? Show(Component card, bool show, Guid? templateId)
    {
        var method = NativeCardPreviewReflection.ShowMethod;
        if (method == null || card == null)
            return method == null
                ? new NativeCardPreviewFailure(
                    NativeCardPreviewOperation.Show,
                    NativeCardPreviewFailureReason.ReflectionUnavailable,
                    templateId
                )
                : null;

        try
        {
            method.Invoke(card, new object[] { show });
        }
        catch (Exception ex)
        {
            return new NativeCardPreviewFailure(
                NativeCardPreviewOperation.Show,
                NativeCardPreviewFailureReason.ShowException,
                templateId,
                ex
            );
        }
        return null;
    }

    public static async Task<NativeCardPreviewFailure?> InvokeSetUpSafe(
        Component card,
        TCardBase template,
        TCardInstance instance,
        CancellationToken token = default
    )
    {
        var method = NativeCardPreviewReflection.SetUpMethod;
        if (method == null)
            return new NativeCardPreviewFailure(
                NativeCardPreviewOperation.SetUp,
                NativeCardPreviewFailureReason.ReflectionUnavailable,
                template?.Id
            );

        try
        {
            var raw = method.Invoke(card, new object[] { template, false, instance, token });
            if (raw is Task task)
                await task;
            token.ThrowIfCancellationRequested();
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is OperationCanceledException)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException!).Throw();
            throw;
        }
        catch (TargetInvocationException ex)
        {
            return new NativeCardPreviewFailure(
                NativeCardPreviewOperation.SetUp,
                NativeCardPreviewFailureReason.SetUpException,
                template?.Id,
                ex.InnerException ?? ex
            );
        }
        catch (Exception ex)
        {
            return new NativeCardPreviewFailure(
                NativeCardPreviewOperation.SetUp,
                NativeCardPreviewFailureReason.SetUpException,
                template?.Id,
                ex
            );
        }
    }
}

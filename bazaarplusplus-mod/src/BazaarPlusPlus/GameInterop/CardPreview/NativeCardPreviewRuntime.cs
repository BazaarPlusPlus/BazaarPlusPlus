#nullable enable
using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Infrastructure;
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

    public static void Resize(Component card, string logComponent)
    {
        try
        {
            NativeCardPreviewReflection.ResizeMethod?.Invoke(card, Array.Empty<object>());
        }
        catch (Exception ex)
        {
            BppLog.Warn(logComponent, $"CardPreviewBase.Resize threw: {ex.Message}");
        }
    }

    public static void Show(Component card, bool show, string logComponent)
    {
        var method = NativeCardPreviewReflection.ShowMethod;
        if (method == null || card == null)
            return;

        try
        {
            method.Invoke(card, new object[] { show });
        }
        catch (Exception ex)
        {
            BppLog.Warn(logComponent, $"CardPreviewBase.Show threw: {ex.Message}");
        }
    }

    public static async Task InvokeSetUpSafe(
        Component card,
        TCardBase template,
        TCardInstance instance,
        string logComponent
    )
    {
        var method = NativeCardPreviewReflection.SetUpMethod;
        if (method == null)
            return;

        try
        {
            var raw = method.Invoke(card, BuildSetUpArguments(method, template, instance));
            if (raw is Task task)
                await task;
        }
        catch (TargetInvocationException ex)
        {
            BppLog.Warn(
                logComponent,
                $"CardPreviewBase.SetUp threw for template={template?.Id}: {ex.InnerException?.Message ?? ex.Message}"
            );
            throw;
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                logComponent,
                $"CardPreviewBase.SetUp invocation failed for template={template?.Id}: {ex.Message}"
            );
            throw;
        }
    }

    internal static object[] BuildSetUpArgumentsForTest(
        MethodInfo method,
        TCardBase template,
        TCardInstance instance
    ) => BuildSetUpArguments(method, template, instance);

    private static object[] BuildSetUpArguments(
        MethodInfo method,
        TCardBase template,
        TCardInstance instance
    )
    {
        var parameters = method.GetParameters();
        if (
            parameters.Length == 4
            && typeof(TCardBase).IsAssignableFrom(parameters[0].ParameterType)
            && parameters[1].ParameterType == typeof(bool)
            && typeof(TCardInstance).IsAssignableFrom(parameters[2].ParameterType)
            && parameters[3].ParameterType == typeof(CancellationToken)
        )
        {
            return new object[] { template, false, instance, CancellationToken.None };
        }

        if (
            parameters.Length == 3
            && typeof(TCardBase).IsAssignableFrom(parameters[0].ParameterType)
            && parameters[1].ParameterType == typeof(bool)
            && typeof(TCardInstance).IsAssignableFrom(parameters[2].ParameterType)
        )
        {
            return new object[] { template, false, instance };
        }

        throw new InvalidOperationException(
            $"Unsupported CardPreviewBase.SetUp signature: {method}."
        );
    }
}

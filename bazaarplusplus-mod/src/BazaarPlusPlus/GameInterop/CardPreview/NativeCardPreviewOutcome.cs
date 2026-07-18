#nullable enable
namespace BazaarPlusPlus.GameInterop.CardPreview;

internal enum NativeCardPreviewOperation
{
    Acquire,
    ResolveTemplate,
    ResolveKind,
    Instantiate,
    SetUp,
    ResolveRect,
    Resize,
    Show,
    GetTooltipData,
    GetClientCard,
    CreateTooltipData,
    SetTooltipData,
    InvokeHover,
    InvokeHoverOut,
    Release,
    OwnerPrepare,
    OwnerAcquired,
    OwnerRelease,
}

internal enum NativeCardPreviewFailureReason
{
    Unexpected,
    StaticDataUnavailable,
    TemplateUnavailable,
    UnsupportedCardType,
    AssetLoaderUnavailable,
    PreviewTypeUnavailable,
    PreviewComponentUnavailable,
    InstantiateException,
    SetUpException,
    RectUnavailable,
    ResizeException,
    ShowException,
    ReflectionUnavailable,
    ReflectionException,
    OwnerHookException,
}

internal sealed class NativeCardPreviewFailure
{
    internal NativeCardPreviewFailure(
        NativeCardPreviewOperation operation,
        NativeCardPreviewFailureReason reason,
        Guid? templateId,
        Exception? exception = null
    )
    {
        Operation = operation;
        Reason = reason;
        TemplateId = templateId;
        Exception = exception;
    }

    internal NativeCardPreviewOperation Operation { get; }
    internal NativeCardPreviewFailureReason Reason { get; }
    internal Guid? TemplateId { get; }
    internal Exception? Exception { get; }
}

internal enum NativePreviewActionStatus
{
    Applied,
    AlreadyApplied,
    Released,
    Failed,
}

internal readonly record struct NativePreviewActionResult(
    NativePreviewActionStatus Status,
    NativeCardPreviewFailure? Failure
);

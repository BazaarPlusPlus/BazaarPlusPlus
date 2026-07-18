#nullable enable
using System.Runtime.ExceptionServices;
using HarmonyLib;
using TheBazaar.UI.Tooltips;

namespace BazaarPlusPlus.GameInterop.CardPreview;

internal sealed class NativeCardPreviewHost : INativeCardPreviewHost
{
    private readonly INativeTooltipDataFactory _tooltipDataFactory;
    private readonly NativeCardPreviewHoverState<Scope, NativeCardPreviewResource> _hover = new();
    private readonly NativeCardPreviewFactory _measureFactory = new(
        new NativeCardPreviewPool(maxPoolSizePerKind: 1)
    );

    internal NativeCardPreviewHost(INativeTooltipDataFactory tooltipDataFactory) =>
        _tooltipDataFactory =
            tooltipDataFactory ?? throw new ArgumentNullException(nameof(tooltipDataFactory));

    public NativeCardMeasureResult Measure(NativeCardPreviewSubject subject)
    {
        try
        {
            return _measureFactory.Measure(subject);
        }
        catch (Exception ex)
        {
            return new NativeCardMeasureResult(
                NativeCardMeasureStatus.Failed,
                1,
                UnexpectedFailure(subject?.TemplateId, ex)
            );
        }
    }

    public INativeCardPreviewScope OpenScope(INativeCardPreviewOwner owner) =>
        new Scope(this, owner ?? throw new ArgumentNullException(nameof(owner)));

    public NativeTooltipRefreshResult RefreshHoveredTooltip(NativeTooltipRefreshRequest request)
    {
        if (
            !_hover.TryGet((scope, candidate) => scope.IsActive(candidate), out _, out var resource)
            || resource == null
        )
            return Result(NativeTooltipRefreshStatus.NoHoveredPreview);

        try
        {
            return RefreshHoveredTooltip(resource, request);
        }
        catch (Exception ex)
        {
            var failure = new NativeCardPreviewFailure(
                NativeCardPreviewOperation.GetTooltipData,
                NativeCardPreviewFailureReason.Unexpected,
                resource.Subject.TemplateId,
                ex
            );
            SafeReport(resource.Owner, failure);
            return Result(NativeTooltipRefreshStatus.Failed, failure);
        }
    }

    private NativeTooltipRefreshResult RefreshHoveredTooltip(
        NativeCardPreviewResource resource,
        NativeTooltipRefreshRequest request
    )
    {
        NativeCardPreviewFailure? reflectionFailure = null;
        void Report(NativeCardPreviewFailure failure)
        {
            failure = WithTemplateId(failure, resource.Subject.TemplateId);
            reflectionFailure ??= failure;
            SafeReport(resource.Owner, failure);
        }

        if (
            !NativeCardPreviewReflection.TryGetTooltipData(
                resource.Card,
                out var currentTooltipData,
                Report
            )
            || !NativeCardPreviewReflection.TryGetClientCard(
                resource.Card,
                out var clientCard,
                Report
            )
        )
            return Result(NativeTooltipRefreshStatus.Failed, reflectionFailure);

        var tooltipParent = request.TooltipParent;
        if (tooltipParent == null)
            return Result(NativeTooltipRefreshStatus.TooltipMismatch);
        var primaryController = Traverse
            .Create(tooltipParent)
            .Property("CardTooltipController")
            .GetValue<CardTooltipController>();
        if (
            primaryController?.CurrentTooltipData == null
            || !ReferenceEquals(primaryController.CurrentTooltipData, currentTooltipData)
        )
            return Result(NativeTooltipRefreshStatus.TooltipMismatch);

        if (!NativeCardPreviewReflection.CanInvokeOnHover(resource.Card))
        {
            var unavailableFailure = new NativeCardPreviewFailure(
                NativeCardPreviewOperation.InvokeHover,
                NativeCardPreviewFailureReason.ReflectionUnavailable,
                resource.Subject.TemplateId
            );
            SafeReport(resource.Owner, unavailableFailure);
            return Result(NativeTooltipRefreshStatus.Failed, unavailableFailure);
        }

        reflectionFailure = null;
        var transaction = NativeTooltipRefreshTransaction.Execute(
            currentTooltipData,
            () => _tooltipDataFactory.Create(clientCard, currentTooltipData, request.Mode),
            value => NativeCardPreviewReflection.TrySetTooltipData(resource.Card, value, Report),
            tooltipParent.HideCardTooltipController,
            () => NativeCardPreviewReflection.TryInvokeOnHover(resource.Card, Report)
        );
        if (transaction.Status == NativeTooltipRefreshTransactionStatus.Refreshed)
        {
            return new NativeTooltipRefreshResult(
                NativeTooltipRefreshStatus.Refreshed,
                clientCard,
                null
            );
        }
        if (transaction.Status == NativeTooltipRefreshTransactionStatus.NoChange)
        {
            return new NativeTooltipRefreshResult(
                NativeTooltipRefreshStatus.NoChange,
                clientCard,
                null
            );
        }

        var transactionFailure = reflectionFailure;
        if (transactionFailure == null && transaction.Exception != null)
        {
            transactionFailure = new NativeCardPreviewFailure(
                transaction.Status switch
                {
                    NativeTooltipRefreshTransactionStatus.CreateFailed =>
                        NativeCardPreviewOperation.CreateTooltipData,
                    NativeTooltipRefreshTransactionStatus.RehoverFailed =>
                        NativeCardPreviewOperation.InvokeHover,
                    _ => NativeCardPreviewOperation.SetTooltipData,
                },
                NativeCardPreviewFailureReason.ReflectionException,
                resource.Subject.TemplateId,
                transaction.Exception
            );
            SafeReport(resource.Owner, transactionFailure);
        }
        return Result(NativeTooltipRefreshStatus.Failed, transactionFailure);
    }

    private static NativeTooltipRefreshResult Result(
        NativeTooltipRefreshStatus status,
        NativeCardPreviewFailure? failure = null
    ) => new(status, null, failure);

    private static void SafeReport(INativeCardPreviewOwner owner, NativeCardPreviewFailure failure)
    {
        try
        {
            owner.ReportFailure(failure);
        }
        catch
        {
            // Diagnostics must not turn a typed preview outcome into a UI exception.
        }
    }

    private static NativeCardPreviewFailure WithTemplateId(
        NativeCardPreviewFailure failure,
        Guid templateId
    ) =>
        failure.TemplateId.HasValue
            ? failure
            : new NativeCardPreviewFailure(
                failure.Operation,
                failure.Reason,
                templateId,
                failure.Exception
            );

    private void SetHovered(Scope scope, NativeCardPreviewResource resource) =>
        _hover.Set(scope, resource);

    private void ClearHovered(Scope scope, NativeCardPreviewResource resource) =>
        _hover.Clear(scope, resource);

    private void ClearHovered(Scope scope) => _hover.Clear(scope);

    private sealed class Scope : INativeCardPreviewScope
    {
        private readonly NativeCardPreviewHost _host;
        private readonly INativeCardPreviewOwner _owner;
        private readonly NativeCardPreviewPool _pool = new();
        private readonly NativeCardPreviewFactory _factory;
        private readonly NativeCardPreviewScopeLifetime<NativeCardPreviewResource> _lifetime;
        private readonly CancellationTokenSource _shutdown = new();
        private readonly NativeCardPreviewScopeDisposal _disposal;

        internal Scope(NativeCardPreviewHost host, INativeCardPreviewOwner owner)
        {
            _host = host;
            _owner = owner;
            _factory = new NativeCardPreviewFactory(_pool);
            _lifetime = new NativeCardPreviewScopeLifetime<NativeCardPreviewResource>(
                ReturnToPool,
                Destroy
            );
            _disposal = new NativeCardPreviewScopeDisposal(DisposeCoreAsync);
        }

        public async ValueTask<NativeCardAcquireResult> AcquireAsync(
            NativeCardPreviewSubject subject,
            CancellationToken cancellationToken = default
        )
        {
            if (!_lifetime.TryBeginAcquire(out var acquisition))
            {
                return new NativeCardAcquireResult(NativeCardAcquireStatus.ScopeClosed, null, null);
            }

            using (acquisition)
            using (
                var linked = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    _shutdown.Token
                )
            )
            {
                NativeCardPreviewCoreAcquireOutcome outcome;
                try
                {
                    outcome = await _factory.AcquireAsync(subject, _owner, linked.Token);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    var failure = UnexpectedFailure(subject?.TemplateId, ex);
                    SafeReport(_owner, failure);
                    return new NativeCardAcquireResult(
                        NativeCardAcquireStatus.Failed,
                        null,
                        failure
                    );
                }
                if (outcome.Resource == null)
                {
                    if (outcome.Failure != null)
                        SafeReport(_owner, outcome.Failure);
                    return new NativeCardAcquireResult(
                        outcome.Failure == null
                            ? NativeCardAcquireStatus.Unavailable
                            : NativeCardAcquireStatus.Failed,
                        null,
                        outcome.Failure
                    );
                }

                if (!acquisition.TryDeliver(outcome.Resource))
                {
                    Destroy(outcome.Resource);
                    linked.Token.ThrowIfCancellationRequested();
                    return new NativeCardAcquireResult(
                        NativeCardAcquireStatus.ScopeClosed,
                        null,
                        null
                    );
                }

                try
                {
                    return new NativeCardAcquireResult(
                        NativeCardAcquireStatus.Acquired,
                        new Session(this, outcome.Resource),
                        null
                    );
                }
                catch (Exception ex)
                {
                    Release(outcome.Resource);
                    var failure = UnexpectedFailure(subject?.TemplateId, ex);
                    SafeReport(_owner, failure);
                    return new NativeCardAcquireResult(
                        NativeCardAcquireStatus.Failed,
                        null,
                        failure
                    );
                }
            }
        }

        public ValueTask DisposeAsync() => _disposal.DisposeAsync();

        private async Task DisposeCoreAsync()
        {
            var closing = _lifetime.CloseAsync();
            _host.ClearHovered(this);
            List<Exception>? failures = null;
            try
            {
                _shutdown.Cancel();
            }
            catch (Exception ex)
            {
                (failures ??= new List<Exception>()).Add(ex);
            }

            try
            {
                await closing;
            }
            catch (Exception ex)
            {
                (failures ??= new List<Exception>()).Add(ex);
            }

            try
            {
                _pool.DestroyAll();
            }
            catch (Exception ex)
            {
                (failures ??= new List<Exception>()).Add(ex);
            }

            try
            {
                _shutdown.Dispose();
            }
            catch (Exception ex)
            {
                (failures ??= new List<Exception>()).Add(ex);
            }

            if (failures?.Count == 1)
                ExceptionDispatchInfo.Capture(failures[0]).Throw();
            if (failures?.Count > 1)
                throw new AggregateException(failures);
        }

        internal bool IsActive(NativeCardPreviewResource resource) => _lifetime.IsActive(resource);

        internal bool Release(NativeCardPreviewResource resource) => _lifetime.Release(resource);

        internal NativePreviewActionResult Show(NativeCardPreviewResource resource, bool show)
        {
            if (!IsActive(resource))
                return new NativePreviewActionResult(NativePreviewActionStatus.Released, null);

            try
            {
                var result = NativePreviewPresentationTransaction.Apply(
                    show,
                    ApplyNative,
                    resource.Presentation.RevealSupplementalVisuals,
                    resource.Presentation.ConcealSupplementalVisuals
                );
                if (result.Failure != null)
                    SafeReport(resource.Owner, result.Failure);
                return result;
            }
            catch (Exception ex)
            {
                try
                {
                    resource.Presentation.ConcealSupplementalVisuals();
                }
                catch
                {
                    // Preserve the first actionable presentation failure.
                }

                var failure = new NativeCardPreviewFailure(
                    NativeCardPreviewOperation.Show,
                    NativeCardPreviewFailureReason.Unexpected,
                    resource.Subject.TemplateId,
                    ex
                );
                SafeReport(resource.Owner, failure);
                return new NativePreviewActionResult(NativePreviewActionStatus.Failed, failure);
            }

            NativePreviewActionResult ApplyNative()
            {
                var failure = NativeCardPreviewRuntime.Show(
                    resource.Card,
                    show,
                    resource.Subject.TemplateId
                );
                return failure == null
                    ? new NativePreviewActionResult(NativePreviewActionStatus.Applied, null)
                    : new NativePreviewActionResult(NativePreviewActionStatus.Failed, failure);
            }
        }

        internal void SetHovered(NativeCardPreviewResource resource) =>
            _host.SetHovered(this, resource);

        internal void ClearHovered(NativeCardPreviewResource resource) =>
            _host.ClearHovered(this, resource);

        internal void OnNativeDestroyed(NativeCardPreviewResource resource)
        {
            if (!_lifetime.ForgetDestroyed(resource))
                return;

            PrepareRelease(resource);
        }

        private void ReturnToPool(NativeCardPreviewResource resource)
        {
            try
            {
                NativeCardPreviewLifecycle.Disarm(resource.Root);
            }
            catch (Exception ex)
            {
                ReportReleaseFailure(resource, ex);
            }
            PrepareRelease(resource);
            try
            {
                _pool.Return(resource.Card, resource.Kind);
            }
            catch (Exception ex)
            {
                try
                {
                    NativeCardPreviewPool.Destroy(resource.Card);
                }
                catch
                {
                    // The original return failure is the actionable diagnostic.
                }
                ReportReleaseFailure(resource, ex);
            }
        }

        private void Destroy(NativeCardPreviewResource resource)
        {
            try
            {
                NativeCardPreviewLifecycle.Disarm(resource.Root);
            }
            catch (Exception ex)
            {
                ReportReleaseFailure(resource, ex);
            }
            PrepareRelease(resource);
            try
            {
                NativeCardPreviewPool.Destroy(resource.Card);
            }
            catch (Exception ex)
            {
                ReportReleaseFailure(resource, ex);
            }
        }

        private void PrepareRelease(NativeCardPreviewResource resource)
        {
            _host.ClearHovered(this, resource);
            try
            {
                resource.Owner.BeforeRelease(resource.CreateOwnerContext());
            }
            catch (Exception ex)
            {
                SafeReport(
                    resource.Owner,
                    new NativeCardPreviewFailure(
                        NativeCardPreviewOperation.OwnerRelease,
                        NativeCardPreviewFailureReason.OwnerHookException,
                        resource.Subject.TemplateId,
                        ex
                    )
                );
            }
        }

        private static void ReportReleaseFailure(
            NativeCardPreviewResource resource,
            Exception exception
        ) =>
            SafeReport(
                resource.Owner,
                new NativeCardPreviewFailure(
                    NativeCardPreviewOperation.Release,
                    NativeCardPreviewFailureReason.Unexpected,
                    resource.Subject.TemplateId,
                    exception
                )
            );
    }

    private static NativeCardPreviewFailure UnexpectedFailure(
        Guid? templateId,
        Exception exception
    ) =>
        new(
            NativeCardPreviewOperation.Acquire,
            NativeCardPreviewFailureReason.Unexpected,
            templateId,
            exception
        );

    private sealed class Session : INativeCardPreviewSession
    {
        private readonly Scope _scope;
        private readonly NativeCardPreviewResource _resource;
        private readonly NativePreviewSessionActions _actions = new();

        internal Session(Scope scope, NativeCardPreviewResource resource)
        {
            _scope = scope;
            _resource = resource;
            NativeCardPreviewLifecycle.Bind(resource.Root, () => scope.OnNativeDestroyed(resource));
        }

        public UnityEngine.GameObject Root => _resource.Root;
        public UnityEngine.RectTransform Rect => _resource.Rect;

        public NativePreviewActionResult Show() =>
            _actions.SetShown(
                _scope.IsActive(_resource),
                show: true,
                () => _scope.Show(_resource, true)
            );

        public NativePreviewActionResult Hide() =>
            _actions.SetShown(
                _scope.IsActive(_resource),
                show: false,
                () => _scope.Show(_resource, false)
            );

        public NativePreviewActionResult HoverEnter()
        {
            return _actions.HoverEnter(_scope.IsActive(_resource), InvokeHoverEnter);
        }

        public NativePreviewActionResult HoverExit()
        {
            return _actions.HoverExit(_scope.IsActive(_resource), InvokeHoverExit);
        }

        public void Dispose()
        {
            if (!_actions.TryDispose(out var wasHovered))
                return;

            if (wasHovered && _scope.IsActive(_resource))
                InvokeHoverExit();
            _scope.ClearHovered(_resource);
            _scope.Release(_resource);
        }

        private NativePreviewActionResult InvokeHoverEnter()
        {
            var result = InvokeHover(NativeCardPreviewOperation.InvokeHover, hoverOut: false);
            if (result.Status == NativePreviewActionStatus.Applied)
                _scope.SetHovered(_resource);
            return result;
        }

        private NativePreviewActionResult InvokeHoverExit()
        {
            var result = InvokeHover(NativeCardPreviewOperation.InvokeHoverOut, hoverOut: true);
            _scope.ClearHovered(_resource);
            return result;
        }

        private NativePreviewActionResult InvokeHover(
            NativeCardPreviewOperation operation,
            bool hoverOut
        )
        {
            NativeCardPreviewFailure? failure = null;
            void Report(NativeCardPreviewFailure reported)
            {
                reported = WithTemplateId(reported, _resource.Subject.TemplateId);
                failure ??= reported;
                SafeReport(_resource.Owner, reported);
            }

            var invoked = hoverOut
                ? NativeCardPreviewReflection.TryInvokeOnHoverOut(_resource.Card, Report)
                : NativeCardPreviewReflection.TryInvokeOnHover(_resource.Card, Report);
            if (invoked)
                return new NativePreviewActionResult(NativePreviewActionStatus.Applied, null);

            failure ??= new NativeCardPreviewFailure(
                operation,
                NativeCardPreviewFailureReason.ReflectionUnavailable,
                _resource.Subject.TemplateId
            );
            return new NativePreviewActionResult(NativePreviewActionStatus.Failed, failure);
        }
    }
}

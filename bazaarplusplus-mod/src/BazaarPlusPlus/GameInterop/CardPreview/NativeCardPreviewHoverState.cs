#nullable enable
namespace BazaarPlusPlus.GameInterop.CardPreview;

internal sealed class NativeCardPreviewHoverState<TScope, TResource>
    where TScope : class
    where TResource : class
{
    private readonly object _gate = new();
    private TScope? _scope;
    private TResource? _resource;

    internal void Set(TScope scope, TResource resource)
    {
        lock (_gate)
        {
            _scope = scope;
            _resource = resource;
        }
    }

    internal void Clear(TScope scope, TResource resource)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_scope, scope) && ReferenceEquals(_resource, resource))
                ClearUnsafe();
        }
    }

    internal void Clear(TScope scope)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_scope, scope))
                ClearUnsafe();
        }
    }

    internal bool TryGet(
        Func<TScope, TResource, bool> isActive,
        out TScope? scope,
        out TResource? resource
    )
    {
        if (isActive == null)
            throw new ArgumentNullException(nameof(isActive));

        lock (_gate)
        {
            scope = _scope;
            resource = _resource;
            if (scope != null && resource != null && isActive(scope, resource))
                return true;

            ClearUnsafe();
            scope = null;
            resource = null;
            return false;
        }
    }

    private void ClearUnsafe()
    {
        _scope = null;
        _resource = null;
    }
}

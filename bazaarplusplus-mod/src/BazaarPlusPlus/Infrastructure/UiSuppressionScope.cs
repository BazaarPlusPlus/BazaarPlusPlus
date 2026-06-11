#nullable enable
using System;
using System.Collections.Generic;

namespace BazaarPlusPlus.Infrastructure;

internal sealed class UiSuppressionScope : IDisposable
{
    private readonly List<IDisposable> _leases;
    private bool _disposed;

    private UiSuppressionScope(List<IDisposable> leases)
    {
        _leases = leases;
    }

    internal static UiSuppressionScope Begin(params Func<IDisposable?>[] suppressionActions)
    {
        if (suppressionActions == null)
            throw new ArgumentNullException(nameof(suppressionActions));

        var leases = new List<IDisposable>(suppressionActions.Length);
        try
        {
            foreach (var suppressionAction in suppressionActions)
            {
                if (suppressionAction == null)
                    throw new ArgumentNullException(nameof(suppressionActions));
                var lease = suppressionAction();
                if (lease != null)
                    leases.Add(lease);
            }

            return new UiSuppressionScope(leases);
        }
        catch
        {
            RestoreLeases(leases);
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        RestoreLeases(_leases);
    }

    private static void RestoreLeases(IReadOnlyList<IDisposable> leases)
    {
        for (var index = leases.Count - 1; index >= 0; index--)
            leases[index].Dispose();
    }
}

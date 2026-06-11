#nullable enable
using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Cards;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

internal sealed class CollectionCatalogBuildSession : IDisposable
{
    private Dictionary<Guid, ITCard>.Enumerator _enumerator;
    private readonly List<CollectionCardVm> _cards;

    public CollectionCatalogBuildSession(object source, Dictionary<Guid, ITCard> map)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        if (map == null)
            throw new ArgumentNullException(nameof(map));

        SourceTemplateCount = map.Count;
        _enumerator = map.GetEnumerator();
        _cards = new List<CollectionCardVm>(map.Count);
    }

    public object Source { get; }
    public int SourceTemplateCount { get; }
    public int ScannedCount { get; private set; }
    public int AcceptedCount { get; private set; }
    public int RejectedCount { get; private set; }
    public bool IsComplete { get; private set; }

    public IReadOnlyList<CollectionCardVm> Cards => _cards;

    public bool Step(Func<bool> shouldPause, int minimumTemplates = 32)
    {
        if (IsComplete)
            return true;

        var stepped = 0;
        while (stepped < minimumTemplates || !shouldPause())
        {
            if (!_enumerator.MoveNext())
            {
                IsComplete = true;
                return true;
            }

            stepped++;
            ScannedCount++;
            if (_enumerator.Current.Value is not TCardBase template)
            {
                RejectedCount++;
                continue;
            }

            var classification = CollectionCardClassifier.Classify(template);
            if (!classification.IsCatalogCard)
            {
                RejectedCount++;
                continue;
            }

            _cards.Add(CollectionCardVm.From(template, classification));
            AcceptedCount++;
        }

        return false;
    }

    public void Dispose() => _enumerator.Dispose();
}

#nullable enable
using BazaarPlusPlus.Infrastructure.RemoteEmbeddedCatalog;
using Newtonsoft.Json;

namespace BazaarPlusPlus.Game.Supporters;

internal static class SupporterCatalogDocument
{
    internal static CatalogParseResult<IReadOnlyList<BPPSupporterEntry>> Parse(string document)
    {
        try
        {
            var parsed = JsonConvert.DeserializeObject<List<BPPSupporterEntry>>(document);
            var entries = parsed
                ?.Where(entry => !string.IsNullOrWhiteSpace(entry?.Name) && entry.Tier > 0)
                .Select(entry => new BPPSupporterEntry
                {
                    Name = entry.Name.Trim(),
                    Tier = entry.Tier,
                })
                .ToArray();
            return entries is { Length: > 0 }
                ? CatalogParseResult<IReadOnlyList<BPPSupporterEntry>>.Success(entries)
                : CatalogParseResult<IReadOnlyList<BPPSupporterEntry>>.Failure("empty_payload");
        }
        catch (JsonException)
        {
            return CatalogParseResult<IReadOnlyList<BPPSupporterEntry>>.Failure("invalid_response");
        }
    }
}

internal sealed class SupporterCatalogParser : ICatalogParser<IReadOnlyList<BPPSupporterEntry>>
{
    public CatalogParseResult<IReadOnlyList<BPPSupporterEntry>> Parse(
        string document,
        CatalogSource source
    ) => SupporterCatalogDocument.Parse(document);
}

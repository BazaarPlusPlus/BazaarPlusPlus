#nullable enable
namespace BazaarPlusPlus.Infrastructure.Logging;

internal static class BppLogSchemaRules
{
    internal const int MaximumFields = 128;

    internal static bool IsKnownPrivacy(BppLogFieldPrivacy privacy) =>
        privacy == BppLogFieldPrivacy.Public
        || privacy == BppLogFieldPrivacy.UntrustedText
        || privacy == BppLogFieldPrivacy.Sensitive
        || privacy == BppLogFieldPrivacy.LocalPath
        || privacy == BppLogFieldPrivacy.RemoteUri;

    internal static bool IsKnownCorrelation(BppLogCorrelationPolicy correlation) =>
        correlation == BppLogCorrelationPolicy.None
        || correlation == BppLogCorrelationPolicy.Full
        || correlation == BppLogCorrelationPolicy.Short
        || correlation == BppLogCorrelationPolicy.Hash;

    internal static bool IsKnownCardinality(BppLogCardinality cardinality) =>
        cardinality == BppLogCardinality.Low || cardinality == BppLogCardinality.High;

    internal static bool IsDottedSnakeIdentifier(string? value, int minimumSegments)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 128)
            return false;
        var segments = value.Split('.');
        if (segments.Length < minimumSegments)
            return false;
        for (var index = 0; index < segments.Length; index++)
        {
            if (!IsSnakeIdentifier(segments[index]))
                return false;
        }
        return true;
    }

    internal static bool IsSnakeIdentifier(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 64)
            return false;
        if (value[0] < 'a' || value[0] > 'z' || value[value.Length - 1] == '_')
            return false;

        var previousUnderscore = false;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character == '_')
            {
                if (previousUnderscore)
                    return false;
                previousUnderscore = true;
                continue;
            }
            previousUnderscore = false;
            if ((character >= 'a' && character <= 'z') || (character >= '0' && character <= '9'))
                continue;
            return false;
        }
        return true;
    }
}

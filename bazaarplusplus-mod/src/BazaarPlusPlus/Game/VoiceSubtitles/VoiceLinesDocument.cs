#nullable enable
using BazaarPlusPlus.Infrastructure;

namespace BazaarPlusPlus.Game.VoiceSubtitles;

internal sealed class VoiceLinesDocument
{
    internal const int SupportedSchemaVersion = VoiceLinesValidationCore.SupportedSchemaVersion;

    internal static VoiceLine[] Parse(string json, VoiceCatalogSource source)
    {
        var parsed = VoiceLinesValidationCore.Parse(json, source.ToString());
        foreach (var skippedRow in parsed.SkippedRows)
        {
            ReportSkippedRow(
                source,
                skippedRow.RowNumber,
                MapSkipReason(skippedRow.Reason),
                skippedRow.Stem
            );
        }

        var result = new VoiceLine[parsed.Lines.Length];
        for (var i = 0; i < parsed.Lines.Length; i++)
        {
            var line = parsed.Lines[i];
            result[i] = new VoiceLine(line.Stem, line.English, line.Chinese, line.DurationSeconds);
        }
        return result;
    }

    private static VoiceCatalogRowSkipReason MapSkipReason(VoiceLinesValidationSkipReason reason) =>
        reason switch
        {
            VoiceLinesValidationSkipReason.MissingStem => VoiceCatalogRowSkipReason.MissingStem,
            VoiceLinesValidationSkipReason.DuplicateStem => VoiceCatalogRowSkipReason.DuplicateStem,
            VoiceLinesValidationSkipReason.EmptyText => VoiceCatalogRowSkipReason.EmptyText,
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
        };

    private static void ReportSkippedRow(
        VoiceCatalogSource source,
        int rowNumber,
        VoiceCatalogRowSkipReason reasonCode,
        string? stem
    )
    {
        BppLog.DebugEvent(
            VoiceCatalogLogEvents.CatalogRowSkipped,
            () =>
                [
                    VoiceCatalogLogEvents.CatalogRowSkippedSource.Bind(source),
                    VoiceCatalogLogEvents.CatalogRowSkippedRowNumber.Bind(rowNumber),
                    VoiceCatalogLogEvents.CatalogRowSkippedReasonCode.Bind(reasonCode),
                    VoiceCatalogLogEvents.CatalogRowSkippedStem.Bind(stem),
                ]
        );
    }

    internal static string ComputeContentHash(IReadOnlyList<VoiceLine> lines)
    {
        var validationLines = new VoiceLinesValidationLine[lines.Count];
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            validationLines[i] = new VoiceLinesValidationLine(
                line.Stem,
                line.English,
                line.Chinese,
                line.DurationSeconds
            );
        }

        return VoiceLinesValidationCore.ComputeContentHash(validationLines);
    }
}

#nullable enable
using System.Globalization;
using System.Text;
using Newtonsoft.Json;

namespace BazaarPlusPlus.ModApi.Bundle;

internal static class BundleManifestV5Writer
{
    internal static byte[] Write(BundleManifestV5 manifest)
    {
        using var text = new StringWriter(CultureInfo.InvariantCulture);
        using (var writer = new JsonTextWriter(text))
        {
            writer.CloseOutput = false;
            writer.Culture = CultureInfo.InvariantCulture;
            writer.Formatting = Formatting.None;
            writer.StringEscapeHandling = StringEscapeHandling.Default;
            WriteManifest(writer, manifest);
        }
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text.ToString());
    }

    private static void WriteManifest(JsonWriter writer, BundleManifestV5 manifest)
    {
        writer.WriteStartObject();
        Write(writer, "bundle_id", manifest.BundleId);
        Write(writer, "bundle_version", manifest.BundleVersion);
        Write(writer, "created_at_ms", manifest.CreatedAtMs);
        writer.WritePropertyName("run");
        WriteRun(writer, manifest.Run);
        if (manifest.Screenshot != null)
        {
            writer.WritePropertyName("screenshot");
            WriteScreenshot(writer, manifest.Screenshot);
        }
        writer.WriteEndObject();
    }

    private static void WriteRun(JsonWriter writer, BundleRunManifestV5 run)
    {
        writer.WriteStartObject();
        Write(writer, "run_id", run.RunId);
        Write(writer, "player_account_id", run.PlayerAccountId);
        Write(writer, "run_format_version", run.RunFormatVersion);
        writer.WritePropertyName("projection");
        writer.WriteStartObject();
        writer.WritePropertyName("run");
        writer.WriteStartObject();
        writer.WriteEndObject();
        writer.WritePropertyName("battles");
        writer.WriteStartArray();
        foreach (var battle in run.Projection.Battles)
            WriteBattle(writer, battle);
        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.WritePropertyName("payload");
        WriteSegment(writer, run.Payload);
        writer.WriteEndObject();
    }

    private static void WriteBattle(JsonWriter writer, BundleBattleProjectionV5 battle)
    {
        writer.WriteStartObject();
        Write(writer, "battle_id", battle.BattleId);
        Write(writer, "recorded_at_ms", battle.RecordedAtMs);
        Write(writer, "day", battle.Day);
        Write(writer, "hour", battle.Hour);
        WriteNullable(writer, "encounter_id", battle.EncounterId);
        Write(writer, "combat_kind", battle.CombatKind);
        Write(writer, "result", battle.Result);
        WriteNullable(writer, "winner_combatant_id", battle.WinnerCombatantId);
        WriteNullable(writer, "loser_combatant_id", battle.LoserCombatantId);
        Write(writer, "is_final_battle", battle.IsFinalBattle);
        writer.WritePropertyName("player");
        WriteCombatant(writer, battle.Player);
        writer.WritePropertyName("opponent");
        WriteCombatant(writer, battle.Opponent);
        writer.WriteEndObject();
    }

    private static void WriteCombatant(JsonWriter writer, BundleCombatantProjectionV5 combatant)
    {
        writer.WriteStartObject();
        Write(writer, "account_id", combatant.AccountId);
        Write(writer, "display_name", combatant.DisplayName);
        WriteNullable(writer, "hero_id", combatant.HeroId);
        WriteNullable(writer, "hero_name", combatant.HeroName);
        WriteNullable(writer, "rank", combatant.Rank);
        WriteNullable(writer, "rating", combatant.Rating);
        WriteNullable(writer, "level", combatant.Level);
        WriteNullable(writer, "prestige", combatant.Prestige);
        WriteNullable(writer, "victories", combatant.Victories);
        writer.WriteEndObject();
    }

    private static void WriteSegment(JsonWriter writer, BundleSegmentManifestV5 segment)
    {
        writer.WriteStartObject();
        Write(writer, "offset", segment.Offset);
        Write(writer, "length", segment.Length);
        Write(writer, "sha256", segment.Sha256);
        Write(writer, "content_type", segment.ContentType);
        writer.WriteEndObject();
    }

    private static void WriteScreenshot(JsonWriter writer, BundleScreenshotManifestV5 screenshot)
    {
        writer.WriteStartObject();
        Write(writer, "offset", screenshot.Offset);
        Write(writer, "length", screenshot.Length);
        Write(writer, "sha256", screenshot.Sha256);
        Write(writer, "content_type", screenshot.ContentType);
        Write(writer, "width", screenshot.Width);
        Write(writer, "height", screenshot.Height);
        Write(writer, "quality", screenshot.Quality);
        Write(writer, "captured_at_ms", screenshot.CapturedAtMs);
        writer.WriteEndObject();
    }

    private static void Write(JsonWriter writer, string name, string value)
    {
        writer.WritePropertyName(name);
        writer.WriteValue(value);
    }

    private static void Write(JsonWriter writer, string name, long value)
    {
        writer.WritePropertyName(name);
        writer.WriteValue(value);
    }

    private static void Write(JsonWriter writer, string name, bool value)
    {
        writer.WritePropertyName(name);
        writer.WriteValue(value);
    }

    private static void WriteNullable(JsonWriter writer, string name, string? value)
    {
        writer.WritePropertyName(name);
        if (value == null)
            writer.WriteNull();
        else
            writer.WriteValue(value);
    }

    private static void WriteNullable(JsonWriter writer, string name, long? value)
    {
        writer.WritePropertyName(name);
        if (value.HasValue)
            writer.WriteValue(value.Value);
        else
            writer.WriteNull();
    }
}

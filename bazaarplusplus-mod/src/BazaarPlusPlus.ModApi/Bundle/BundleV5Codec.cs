#nullable enable
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BazaarPlusPlus.ModApi.Bundle;

public static class BundleV5Codec
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("BPPBNDL5");
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly Regex IdentifierPattern = new(
        "^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$",
        RegexOptions.CultureInvariant
    );
    private static readonly Regex Sha256Pattern = new(
        "^[0-9a-f]{64}$",
        RegexOptions.CultureInvariant
    );

    public static BundleBuildResultV5 Build(BundleBuildInputV5 input)
    {
        if (input == null)
            throw new ArgumentNullException(nameof(input));
        ValidateBundleId(input.BundleId);
        ValidateCreatedAt(input.CreatedAtMs);
        ValidateIdentifier(input.RunId, "run.run_id");
        ValidateIdentifier(input.PlayerAccountId, "run.player_account_id");
        if (input.RunPayload.Length == 0)
            Invalid("run_missing", "Run segment must be non-empty.");
        if (input.RunPayload.Length > BundleLimitsV5.MaxRunBytes)
            Invalid("run_too_large", "Run segment exceeds its byte limit.");

        var battles = input.Battles ?? throw new ArgumentNullException(nameof(input.Battles));
        ValidateBattles(battles, input.PlayerAccountId);
        var runBytes = input.RunPayload.ToArray();
        var manifest = new BundleManifestV5
        {
            BundleId = input.BundleId,
            CreatedAtMs = input.CreatedAtMs,
            Run = new BundleRunManifestV5
            {
                RunId = input.RunId,
                PlayerAccountId = input.PlayerAccountId,
                Projection = new BundleProjectionV5 { Battles = battles.ToList() },
                Payload = new BundleSegmentManifestV5
                {
                    Offset = 0,
                    Length = runBytes.Length,
                    Sha256 = ComputeSha256Hex(runBytes),
                    ContentType = BundleLimitsV5.RunContentType,
                },
            },
        };

        byte[]? screenshotBytes = null;
        if (input.Screenshot != null)
        {
            var screenshot = input.Screenshot;
            screenshotBytes = screenshot.Bytes.ToArray();
            ValidateScreenshotInput(screenshot, screenshotBytes.Length);
            manifest.Screenshot = new BundleScreenshotManifestV5
            {
                Offset = runBytes.Length,
                Length = screenshotBytes.Length,
                Sha256 = ComputeSha256Hex(screenshotBytes),
                ContentType = screenshot.ContentType,
                Width = screenshot.Width,
                Height = screenshot.Height,
                Quality = screenshot.Quality,
                CapturedAtMs = screenshot.CapturedAtMs,
            };
        }

        var manifestBytes = BundleManifestV5Writer.Write(manifest);
        if (manifestBytes.Length == 0 || manifestBytes.Length > BundleLimitsV5.MaxManifestBytes)
            Invalid("manifest_too_large", "Manifest length is outside the accepted range.");
        ValidateProjectionSize(manifest.Run.Projection);

        var totalBytes = checked(
            BundleLimitsV5.PrefixBytes
            + manifestBytes.Length
            + runBytes.Length
            + (screenshotBytes?.Length ?? 0)
        );
        if (totalBytes > BundleLimitsV5.MaxBundleBytes)
            throw new BundleV5Exception(
                "bundle_too_large",
                "bundle_too_large",
                "Bundle exceeds its byte limit."
            );

        var bundle = new byte[totalBytes];
        Buffer.BlockCopy(Magic, 0, bundle, 0, Magic.Length);
        WriteUInt32BigEndian(bundle, 8, BundleLimitsV5.BundleVersion);
        WriteUInt32BigEndian(bundle, 12, manifestBytes.Length);
        Buffer.BlockCopy(
            manifestBytes,
            0,
            bundle,
            BundleLimitsV5.PrefixBytes,
            manifestBytes.Length
        );
        var payloadOffset = BundleLimitsV5.PrefixBytes + manifestBytes.Length;
        Buffer.BlockCopy(runBytes, 0, bundle, payloadOffset, runBytes.Length);
        if (screenshotBytes != null)
            Buffer.BlockCopy(
                screenshotBytes,
                0,
                bundle,
                payloadOffset + runBytes.Length,
                screenshotBytes.Length
            );

        var digestBytes = ComputeSha256(bundle);
        var result = new BundleBuildResultV5(
            bundle,
            manifestBytes,
            manifest,
            ToLowerHex(digestBytes),
            FormatContentDigest(digestBytes)
        );

        // Build is the only writer; make its server-compatible reader the final local schema gate.
        Open(bundle);
        return result;
    }

    public static OpenedBundleV5 Open(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length > BundleLimitsV5.MaxBundleBytes)
            throw new BundleV5Exception(
                "bundle_too_large",
                "bundle_too_large",
                "Bundle exceeds its byte limit."
            );
        if (bytes.Length < BundleLimitsV5.PrefixBytes)
            Invalid("invalid_prefix", "Bundle prefix is incomplete.");

        var source = bytes.Span;
        for (var index = 0; index < Magic.Length; index++)
        {
            if (source[index] != Magic[index])
                Invalid("invalid_prefix", "Bundle magic is invalid.");
        }

        var version = ReadUInt32BigEndian(source, 8);
        if (version != BundleLimitsV5.BundleVersion)
            throw new BundleV5Exception(
                "unsupported_bundle_version",
                "unsupported_bundle_version",
                "Bundle version is not supported."
            );
        var manifestLength = ReadUInt32BigEndian(source, 12);
        if (manifestLength == 0 || manifestLength > BundleLimitsV5.MaxManifestBytes)
            Invalid("manifest_too_large", "Manifest length is outside the accepted range.");
        if ((long)BundleLimitsV5.PrefixBytes + manifestLength >= bytes.Length)
            Invalid("run_missing", "Bundle Run segment is missing.");

        var manifestBytes = bytes
            .Slice(BundleLimitsV5.PrefixBytes, checked((int)manifestLength))
            .ToArray();
        var root = ParseManifest(manifestBytes);
        var manifest = ReadManifest(root);
        var payloadStart = checked(BundleLimitsV5.PrefixBytes + (int)manifestLength);
        var describedBytes = checked(
            (long)payloadStart + manifest.Run.Payload.Length + (manifest.Screenshot?.Length ?? 0)
        );
        if (describedBytes < bytes.Length)
            Invalid("undeclared_trailing_bytes", "Bundle has undeclared trailing bytes.");
        if (describedBytes > bytes.Length)
            Invalid("segment_out_of_bounds", "A segment extends beyond the Bundle body.");

        var runOffset = checked(payloadStart + (int)manifest.Run.Payload.Offset);
        var runLength = checked((int)manifest.Run.Payload.Length);
        var runBytes = bytes.Slice(runOffset, runLength).ToArray();
        ValidateDigest(runBytes, manifest.Run.Payload.Sha256);

        byte[]? screenshotBytes = null;
        if (manifest.Screenshot != null)
        {
            var screenshotOffset = checked(payloadStart + (int)manifest.Screenshot.Offset);
            var screenshotLength = checked((int)manifest.Screenshot.Length);
            screenshotBytes = bytes.Slice(screenshotOffset, screenshotLength).ToArray();
            ValidateDigest(screenshotBytes, manifest.Screenshot.Sha256);
        }

        var completeBytes = bytes.ToArray();
        var completeDigest = ComputeSha256(completeBytes);
        return new OpenedBundleV5(
            manifest,
            manifestBytes,
            runBytes,
            screenshotBytes,
            ToLowerHex(completeDigest),
            FormatContentDigest(completeDigest)
        );
    }

    public static string ComputeSha256Hex(ReadOnlyMemory<byte> bytes) =>
        ToLowerHex(ComputeSha256(bytes.ToArray()));

    public static string FormatContentDigest(ReadOnlyMemory<byte> bundleBytes) =>
        FormatContentDigest(ComputeSha256(bundleBytes.ToArray()));

    private static JObject ParseManifest(byte[] manifestBytes)
    {
        string json;
        try
        {
            json = StrictUtf8.GetString(manifestBytes);
        }
        catch (DecoderFallbackException ex)
        {
            throw new BundleV5Exception(
                "invalid_bundle",
                "manifest_not_json",
                "Manifest is not valid UTF-8 JSON.",
                ex
            );
        }

        try
        {
            using var stringReader = new StringReader(json);
            using var reader = new JsonTextReader(stringReader)
            {
                DateParseHandling = DateParseHandling.None,
                FloatParseHandling = FloatParseHandling.Decimal,
                MaxDepth = 64,
            };
            var token = JToken.ReadFrom(
                reader,
                new JsonLoadSettings
                {
                    DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Replace,
                    LineInfoHandling = LineInfoHandling.Ignore,
                }
            );
            if (reader.Read())
                Invalid("manifest_not_json", "Manifest contains trailing JSON tokens.");
            return RequireObject(token, "Manifest root must be an object.");
        }
        catch (BundleV5Exception)
        {
            throw;
        }
        catch (Exception ex) when (ex is JsonException || ex is InvalidOperationException)
        {
            throw new BundleV5Exception(
                "invalid_bundle",
                "manifest_not_json",
                "Manifest is not valid JSON.",
                ex
            );
        }
    }

    private static BundleManifestV5 ReadManifest(JObject root)
    {
        var bundleVersion = ReadSafeInteger(
            RequireProperty(root, "bundle_version"),
            "bundle_version"
        );
        if (bundleVersion != BundleLimitsV5.BundleVersion)
            throw new BundleV5Exception(
                "unsupported_bundle_version",
                "unsupported_bundle_version",
                "Manifest Bundle version is not supported."
            );
        var bundleId = ReadString(RequireProperty(root, "bundle_id"), "bundle_id");
        ValidateBundleId(bundleId);
        var createdAtMs = ReadSafeInteger(RequireProperty(root, "created_at_ms"), "created_at_ms");
        ValidateCreatedAt(createdAtMs);

        var run = RequireObject(RequireProperty(root, "run"), "run must be an object.");
        var runFormatVersion = ReadSafeInteger(
            RequireProperty(run, "run_format_version"),
            "run.run_format_version"
        );
        if (runFormatVersion != BundleLimitsV5.RunFormatVersion)
            throw new BundleV5Exception(
                "unsupported_run_format",
                "unsupported_run_format",
                "Run format is not supported."
            );
        var runId = ReadIdentifier(RequireProperty(run, "run_id"), "run.run_id");
        var playerAccountId = ReadIdentifier(
            RequireProperty(run, "player_account_id"),
            "run.player_account_id"
        );
        var payload = ReadRunSegment(
            RequireObject(RequireProperty(run, "payload"), "run.payload must be an object.")
        );
        var projectionToken = RequireProperty(run, "projection");
        var projectionObject = RequireObject(projectionToken, "run.projection must be an object.");
        RequireObject(
            RequireProperty(projectionObject, "run"),
            "run.projection.run must be an object."
        );
        ValidateProjectionTokenSize(projectionToken);
        var battleTokens = RequireProperty(projectionObject, "battles") as JArray;
        if (battleTokens == null)
            Invalid("manifest_schema_invalid", "run.projection.battles must be an array.");
        if (battleTokens!.Count > BundleLimitsV5.MaxBattlesPerBundle)
            Invalid("too_many_battles", "Bundle contains too many Battle projections.");
        var battles = battleTokens.Select(token => ReadBattle(token, playerAccountId)).ToList();
        if (
            battles.Select(battle => battle.BattleId).Distinct(StringComparer.Ordinal).Count()
            != battles.Count
        )
            Invalid("manifest_schema_invalid", "Battle IDs must be unique within a Bundle.");

        BundleScreenshotManifestV5? screenshot = null;
        var screenshotProperty = root.Property("screenshot", StringComparison.Ordinal);
        if (screenshotProperty != null)
            screenshot = ReadScreenshot(
                RequireObject(screenshotProperty.Value, "screenshot must be an object."),
                payload.Length
            );

        return new BundleManifestV5
        {
            BundleId = bundleId,
            BundleVersion = BundleLimitsV5.BundleVersion,
            CreatedAtMs = createdAtMs,
            Run = new BundleRunManifestV5
            {
                RunId = runId,
                PlayerAccountId = playerAccountId,
                RunFormatVersion = BundleLimitsV5.RunFormatVersion,
                Projection = new BundleProjectionV5 { Battles = battles },
                Payload = payload,
            },
            Screenshot = screenshot,
        };
    }

    private static BundleSegmentManifestV5 ReadRunSegment(JObject payload)
    {
        var offset = ReadSafeInteger(RequireProperty(payload, "offset"), "run.payload.offset");
        var length = ReadSafeInteger(RequireProperty(payload, "length"), "run.payload.length");
        if (offset != 0 || length == 0)
            Invalid(
                "run_missing",
                "Run segment must start at payload offset zero and be non-empty."
            );
        if (length > BundleLimitsV5.MaxRunBytes)
            Invalid("run_too_large", "Run segment exceeds its byte limit.");
        var contentType = ReadString(
            RequireProperty(payload, "content_type"),
            "run.payload.content_type"
        );
        if (!string.Equals(contentType, BundleLimitsV5.RunContentType, StringComparison.Ordinal))
            Invalid("manifest_schema_invalid", "Run content type is invalid.");
        var sha256 = ReadSha256(RequireProperty(payload, "sha256"), "run.payload.sha256");
        return new BundleSegmentManifestV5
        {
            Offset = offset,
            Length = length,
            Sha256 = sha256,
            ContentType = contentType,
        };
    }

    private static BundleScreenshotManifestV5 ReadScreenshot(JObject image, long runLength)
    {
        var offset = ReadSafeInteger(RequireProperty(image, "offset"), "screenshot.offset");
        var length = ReadSafeInteger(RequireProperty(image, "length"), "screenshot.length");
        if (length == 0 || length > BundleLimitsV5.MaxScreenshotBytes)
            Invalid("screenshot_too_large", "Screenshot segment is empty or too large.");
        var contentType = ReadString(
            RequireProperty(image, "content_type"),
            "screenshot.content_type"
        );
        if (
            !string.Equals(contentType, BundleLimitsV5.JpegContentType, StringComparison.Ordinal)
            && !string.Equals(contentType, BundleLimitsV5.WebpContentType, StringComparison.Ordinal)
        )
            Invalid("screenshot_type_unsupported", "Screenshot content type is unsupported.");
        var sha256 = ReadSha256(RequireProperty(image, "sha256"), "screenshot.sha256");
        var width = ReadSafeInteger(RequireProperty(image, "width"), "screenshot.width");
        var height = ReadSafeInteger(RequireProperty(image, "height"), "screenshot.height");
        var quality = ReadSafeInteger(RequireProperty(image, "quality"), "screenshot.quality");
        var capturedAtMs = ReadSafeInteger(
            RequireProperty(image, "captured_at_ms"),
            "screenshot.captured_at_ms"
        );
        if (width < 1 || height < 1 || quality < 1 || quality > 100)
            Invalid("manifest_schema_invalid", "Screenshot dimensions or quality are invalid.");
        if (offset < runLength)
            Invalid("segment_overlap", "Screenshot overlaps the Run segment.");
        if (offset > runLength)
            Invalid(
                "segment_out_of_bounds",
                "Screenshot does not immediately follow the Run segment."
            );
        return new BundleScreenshotManifestV5
        {
            Offset = offset,
            Length = length,
            Sha256 = sha256,
            ContentType = contentType,
            Width = width,
            Height = height,
            Quality = quality,
            CapturedAtMs = capturedAtMs,
        };
    }

    private static BundleBattleProjectionV5 ReadBattle(JToken token, string uploader)
    {
        var source = RequireObject(token, "Battle projection must be an object.");
        var player = ReadCombatant(RequireProperty(source, "player"), "battle.player");
        var opponent = ReadCombatant(RequireProperty(source, "opponent"), "battle.opponent");
        if (!string.Equals(player.AccountId, uploader, StringComparison.Ordinal))
            Invalid("manifest_schema_invalid", "Battle player identity differs from uploader.");
        var finalToken = RequireProperty(source, "is_final_battle");
        if (finalToken.Type != JTokenType.Boolean)
            Invalid("manifest_schema_invalid", "battle.is_final_battle must be boolean.");
        return new BundleBattleProjectionV5
        {
            BattleId = ReadIdentifier(RequireProperty(source, "battle_id"), "battle.battle_id"),
            RecordedAtMs = ReadSafeInteger(
                RequireProperty(source, "recorded_at_ms"),
                "battle.recorded_at_ms"
            ),
            Day = ReadSafeInteger(RequireProperty(source, "day"), "battle.day"),
            Hour = ReadSafeInteger(RequireProperty(source, "hour"), "battle.hour"),
            EncounterId = ReadNullableString(
                RequireProperty(source, "encounter_id"),
                "battle.encounter_id"
            ),
            CombatKind = ReadIdentifier(
                RequireProperty(source, "combat_kind"),
                "battle.combat_kind"
            ),
            Result = ReadIdentifier(RequireProperty(source, "result"), "battle.result"),
            WinnerCombatantId = ReadNullableString(
                RequireProperty(source, "winner_combatant_id"),
                "battle.winner_combatant_id"
            ),
            LoserCombatantId = ReadNullableString(
                RequireProperty(source, "loser_combatant_id"),
                "battle.loser_combatant_id"
            ),
            IsFinalBattle = finalToken.Value<bool>(),
            Player = player,
            Opponent = opponent,
        };
    }

    private static BundleCombatantProjectionV5 ReadCombatant(JToken token, string field)
    {
        var source = RequireObject(token, $"{field} must be an object.");
        var displayName = ReadString(
            RequireProperty(source, "display_name"),
            $"{field}.display_name"
        );
        if (displayName.Length > 256)
            Invalid("manifest_schema_invalid", $"{field}.display_name is invalid.");
        return new BundleCombatantProjectionV5
        {
            AccountId = ReadIdentifier(
                RequireProperty(source, "account_id"),
                $"{field}.account_id"
            ),
            DisplayName = displayName,
            HeroId = ReadNullableString(RequireProperty(source, "hero_id"), $"{field}.hero_id"),
            HeroName = ReadNullableString(
                RequireProperty(source, "hero_name"),
                $"{field}.hero_name"
            ),
            Rank = ReadNullableString(RequireProperty(source, "rank"), $"{field}.rank"),
            Rating = ReadNullableSafeInteger(RequireProperty(source, "rating"), $"{field}.rating"),
            Level = ReadNullableSafeInteger(RequireProperty(source, "level"), $"{field}.level"),
            Prestige = ReadNullableSafeInteger(
                RequireProperty(source, "prestige"),
                $"{field}.prestige"
            ),
            Victories = ReadNullableSafeInteger(
                RequireProperty(source, "victories"),
                $"{field}.victories"
            ),
        };
    }

    private static void ValidateBattles(
        IReadOnlyList<BundleBattleProjectionV5> battles,
        string uploader
    )
    {
        if (battles.Count > BundleLimitsV5.MaxBattlesPerBundle)
            Invalid("too_many_battles", "Bundle contains too many Battle projections.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var battle in battles)
        {
            if (battle == null)
                Invalid("manifest_schema_invalid", "Battle projection must be an object.");
            ValidateIdentifier(battle.BattleId, "battle.battle_id");
            if (!ids.Add(battle.BattleId))
                Invalid("manifest_schema_invalid", "Battle IDs must be unique within a Bundle.");
            ValidateSafeInteger(battle.RecordedAtMs, "battle.recorded_at_ms");
            ValidateSafeInteger(battle.Day, "battle.day");
            ValidateSafeInteger(battle.Hour, "battle.hour");
            ValidateNullableString(battle.EncounterId, "battle.encounter_id");
            ValidateIdentifier(battle.CombatKind, "battle.combat_kind");
            ValidateIdentifier(battle.Result, "battle.result");
            ValidateNullableString(battle.WinnerCombatantId, "battle.winner_combatant_id");
            ValidateNullableString(battle.LoserCombatantId, "battle.loser_combatant_id");
            ValidateCombatant(battle.Player, "battle.player");
            ValidateCombatant(battle.Opponent, "battle.opponent");
            if (!string.Equals(battle.Player.AccountId, uploader, StringComparison.Ordinal))
                Invalid("manifest_schema_invalid", "Battle player identity differs from uploader.");
        }
    }

    private static void ValidateCombatant(BundleCombatantProjectionV5 combatant, string field)
    {
        if (combatant == null)
            Invalid("manifest_schema_invalid", $"{field} must be an object.");
        ValidateIdentifier(combatant.AccountId, $"{field}.account_id");
        if (combatant.DisplayName == null || combatant.DisplayName.Length > 256)
            Invalid("manifest_schema_invalid", $"{field}.display_name is invalid.");
        ValidateNullableString(combatant.HeroId, $"{field}.hero_id");
        ValidateNullableString(combatant.HeroName, $"{field}.hero_name");
        ValidateNullableString(combatant.Rank, $"{field}.rank");
        ValidateNullableSafeInteger(combatant.Rating, $"{field}.rating");
        ValidateNullableSafeInteger(combatant.Level, $"{field}.level");
        ValidateNullableSafeInteger(combatant.Prestige, $"{field}.prestige");
        ValidateNullableSafeInteger(combatant.Victories, $"{field}.victories");
    }

    private static void ValidateProjectionSize(BundleProjectionV5 projection)
    {
        var bytes = BundleManifestV5Writer.Write(
            new BundleManifestV5
            {
                BundleId = "01J00000000000000000000000",
                Run = new BundleRunManifestV5 { Projection = projection },
            }
        );
        // Subtracting a fixed envelope is unnecessary: this conservative precheck can only reject
        // a projection already beyond the server's limit. Open performs the exact token check.
        if (bytes.Length > BundleLimitsV5.MaxProjectionBytes + 512)
            Invalid("projection_too_large", "Run projection exceeds its byte limit.");
    }

    private static void ValidateProjectionTokenSize(JToken projection)
    {
        var compact = projection.ToString(Formatting.None);
        if (Encoding.UTF8.GetByteCount(compact) > BundleLimitsV5.MaxProjectionBytes)
            Invalid("projection_too_large", "Run projection exceeds its byte limit.");
    }

    private static void ValidateScreenshotInput(BundleScreenshotBuildInputV5 screenshot, int length)
    {
        if (length == 0 || length > BundleLimitsV5.MaxScreenshotBytes)
            Invalid("screenshot_too_large", "Screenshot segment is empty or too large.");
        if (
            !string.Equals(
                screenshot.ContentType,
                BundleLimitsV5.JpegContentType,
                StringComparison.Ordinal
            )
            && !string.Equals(
                screenshot.ContentType,
                BundleLimitsV5.WebpContentType,
                StringComparison.Ordinal
            )
        )
            Invalid("screenshot_type_unsupported", "Screenshot content type is unsupported.");
        if (
            screenshot.Width < 1
            || screenshot.Height < 1
            || screenshot.Quality < 1
            || screenshot.Quality > 100
        )
            Invalid("manifest_schema_invalid", "Screenshot dimensions or quality are invalid.");
        ValidateSafeInteger(screenshot.CapturedAtMs, "screenshot.captured_at_ms");
    }

    private static JObject RequireObject(JToken? token, string message)
    {
        if (token is JObject value)
            return value;
        Invalid("manifest_schema_invalid", message);
        throw new InvalidOperationException();
    }

    private static JToken RequireProperty(JObject source, string name)
    {
        var property = source.Property(name, StringComparison.Ordinal);
        if (property == null)
            Invalid("manifest_schema_invalid", $"Manifest field {name} is required.");
        return property!.Value;
    }

    private static string ReadIdentifier(JToken token, string field)
    {
        var value = ReadString(token, field);
        ValidateIdentifier(value, field);
        return value;
    }

    private static string ReadSha256(JToken token, string field)
    {
        var value = ReadString(token, field);
        if (!Sha256Pattern.IsMatch(value))
            Invalid("manifest_schema_invalid", $"{field} is invalid.");
        return value;
    }

    private static string ReadString(JToken token, string field)
    {
        if (token.Type != JTokenType.String)
            Invalid("manifest_schema_invalid", $"{field} must be a string.");
        return token.Value<string>()!;
    }

    private static string? ReadNullableString(JToken token, string field)
    {
        if (token.Type == JTokenType.Null)
            return null;
        var value = ReadString(token, field);
        if (value.Length > 256)
            Invalid("manifest_schema_invalid", $"{field} must be a string or null.");
        return value;
    }

    private static long? ReadNullableSafeInteger(JToken token, string field)
    {
        if (token.Type == JTokenType.Null)
            return null;
        return ReadSafeInteger(token, field);
    }

    private static long ReadSafeInteger(JToken token, string field)
    {
        if (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)
            Invalid("manifest_schema_invalid", $"{field} must be a non-negative safe integer.");
        if (
            !decimal.TryParse(
                token.ToString(Formatting.None),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var value
            )
            || value != decimal.Truncate(value)
            || value < 0
            || value > BundleLimitsV5.MaxSafeInteger
        )
            Invalid("manifest_schema_invalid", $"{field} must be a non-negative safe integer.");
        return decimal.ToInt64(value);
    }

    private static void ValidateBundleId(string value)
    {
        if (!UlidV5Generator.IsCanonical(value))
            Invalid("manifest_schema_invalid", "bundle_id must be a canonical ULID.");
    }

    private static void ValidateIdentifier(string? value, string field)
    {
        if (value == null || !IdentifierPattern.IsMatch(value))
            Invalid("manifest_schema_invalid", $"{field} is invalid.");
    }

    private static void ValidateCreatedAt(long value)
    {
        ValidateSafeInteger(value, "created_at_ms");
        if (value > BundleLimitsV5.MaxCreatedAtMs)
            Invalid(
                "manifest_schema_invalid",
                "created_at_ms is outside the supported date range."
            );
    }

    private static void ValidateNullableString(string? value, string field)
    {
        if (value != null && value.Length > 256)
            Invalid("manifest_schema_invalid", $"{field} must be a string or null.");
    }

    private static void ValidateNullableSafeInteger(long? value, string field)
    {
        if (value.HasValue)
            ValidateSafeInteger(value.Value, field);
    }

    private static void ValidateSafeInteger(long value, string field)
    {
        if (value < 0 || value > BundleLimitsV5.MaxSafeInteger)
            Invalid("manifest_schema_invalid", $"{field} must be a non-negative safe integer.");
    }

    private static void ValidateDigest(byte[] bytes, string expected)
    {
        if (!string.Equals(ComputeSha256Hex(bytes), expected, StringComparison.Ordinal))
            throw new BundleV5Exception(
                "segment_digest_mismatch",
                "segment_digest_mismatch",
                "A Bundle segment digest does not match its manifest."
            );
    }

    private static byte[] ComputeSha256(byte[] bytes)
    {
        using var sha256 = SHA256.Create();
        return sha256.ComputeHash(bytes);
    }

    private static string ToLowerHex(byte[] bytes)
    {
        var output = new char[bytes.Length * 2];
        const string alphabet = "0123456789abcdef";
        for (var index = 0; index < bytes.Length; index++)
        {
            output[index * 2] = alphabet[bytes[index] >> 4];
            output[index * 2 + 1] = alphabet[bytes[index] & 0x0F];
        }
        return new string(output);
    }

    private static string FormatContentDigest(byte[] digest) =>
        $"sha-256=:{Convert.ToBase64String(digest)}:";

    private static uint ReadUInt32BigEndian(ReadOnlySpan<byte> bytes, int offset) =>
        ((uint)bytes[offset] << 24)
        | ((uint)bytes[offset + 1] << 16)
        | ((uint)bytes[offset + 2] << 8)
        | bytes[offset + 3];

    private static void WriteUInt32BigEndian(byte[] bytes, int offset, int value)
    {
        bytes[offset] = (byte)((uint)value >> 24);
        bytes[offset + 1] = (byte)((uint)value >> 16);
        bytes[offset + 2] = (byte)((uint)value >> 8);
        bytes[offset + 3] = (byte)value;
    }

    [DoesNotReturn]
    private static void Invalid(string reason, string message) =>
        throw new BundleV5Exception("invalid_bundle", reason, message);
}

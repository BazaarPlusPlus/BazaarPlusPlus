#nullable enable
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BazaarPlusPlus.ModApi.Bundle;
using BazaarPlusPlus.TestSupport;
using MessagePack;
using MessagePack.Resolvers;

var fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");

GoldenVector();
CorruptFixtures();
RoundTrip();
InvalidInputs();
ReaderBoundsAndUnknownFields();
Ulids();
RunPayloadRoundTrip();
RunPayloadFailureBoundaries();
RunBundleContract();
MessagePackDtoGraphIsPublic();

Console.WriteLine("All Bundle V5 codec tests passed.");

void MessagePackDtoGraphIsPublic()
{
    var dtoTypes = typeof(RunPayloadV5)
        .Assembly.GetTypes()
        .Where(type => type.Namespace == typeof(RunPayloadV5).Namespace)
        .Where(type =>
            type.GetCustomAttributes(inherit: false)
                .Any(attribute => attribute.GetType().Name == "MessagePackObjectAttribute")
        )
        .ToArray();
    True(dtoTypes.Length > 0, "MessagePack DTO graph is discoverable");
    foreach (var type in dtoTypes)
        True(type.IsPublic, $"MessagePack DTO must be public: {type.FullName}");
}

void GoldenVector()
{
    using var manifestJson = JsonDocument.Parse(
        TestInputs.Scratch(Path.Combine(fixtures, "run-only.manifest.json"))
    );
    var root = manifestJson.RootElement;
    var run = root.GetProperty("run");
    var golden = ReadBundle("run-only.bundle.b64");
    var payload = golden[^9..];
    var built = BundleV5Codec.Build(
        new BundleBuildInputV5
        {
            BundleId = root.GetProperty("bundle_id").GetString()!,
            CreatedAtMs = root.GetProperty("created_at_ms").GetInt64(),
            RunId = run.GetProperty("run_id").GetString()!,
            PlayerAccountId = run.GetProperty("player_account_id").GetString()!,
            RunPayload = payload,
        }
    );

    Equal(399, built.Bytes.Length, "golden Bundle length");
    Equal(374, built.ManifestBytes.Length, "golden manifest length");
    Equal(
        "1f8b08000504030201",
        Convert.ToHexString(payload).ToLowerInvariant(),
        "golden Run bytes"
    );
    Equal(
        "6959c53bf3b3b8f38e7aefb2edf13d3ff4ddb0414e95cf4c0460466a7000b36d",
        built.Sha256Hex,
        "golden Bundle SHA-256"
    );
    Equal(
        "sha-256=:aVnFO/OzuPOOeu+y7fE9P/TdsEFOlc9MBGBGanAAs20=:",
        built.ContentDigest,
        "golden Content-Digest"
    );
    SequenceEqual(golden, built.Bytes, "golden Bundle bytes");
    True(
        !Encoding
            .UTF8.GetString(built.ManifestBytes)
            .Contains("screenshot", StringComparison.Ordinal),
        "Run-only manifest omits screenshot"
    );
}

void CorruptFixtures()
{
    ThrowsReason(
        () => BundleV5Codec.Open(ReadBundle("corrupt-magic.bundle.b64")),
        "invalid_bundle",
        "invalid_prefix"
    );
    ThrowsReason(
        () => BundleV5Codec.Open(ReadBundle("segment-digest-mismatch.bundle.b64")),
        "segment_digest_mismatch",
        "segment_digest_mismatch"
    );
}

void RoundTrip()
{
    var battles = Enumerable.Range(0, 30).Select(index => Battle(index, "玩家😀")).ToList();
    var payload = RunPayloadV5Codec.Encode(SamplePayload(battles.Select(value => value.BattleId)));
    var result = BundleV5Codec.Build(
        new BundleBuildInputV5
        {
            BundleId = "01J00000000000000000000902",
            CreatedAtMs = 1_785_628_800_000,
            RunId = "unicode-run",
            PlayerAccountId = "golden-account",
            Battles = battles,
            RunPayload = payload,
            Screenshot = new BundleScreenshotBuildInputV5
            {
                Bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 },
                ContentType = BundleLimitsV5.JpegContentType,
                Width = 1600,
                Height = 900,
                Quality = 82,
                CapturedAtMs = 1_785_628_800_001,
            },
        }
    );
    var opened = BundleV5Codec.Open(result.Bytes);
    Equal(30, opened.Manifest.Run.Projection.Battles.Count, "30 Battle projection round-trip");
    Equal(
        "玩家😀",
        opened.Manifest.Run.Projection.Battles[0].Player.DisplayName,
        "Unicode display name"
    );
    SequenceEqual(payload, opened.RunPayload, "Run segment round-trip");
    SequenceEqual(
        new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 },
        opened.Screenshot!,
        "Screenshot round-trip"
    );
    Equal(82L, opened.Manifest.Screenshot!.Quality, "Screenshot quality round-trip");
}

void InvalidInputs()
{
    var valid = Battle(0, "Player");
    ThrowsReason(
        () => BuildWith(Enumerable.Range(0, 31).Select(index => Battle(index, "Player")).ToList()),
        "invalid_bundle",
        "too_many_battles"
    );
    ThrowsReason(
        () => BuildWith(new[] { valid, Battle(0, "Player") }),
        "invalid_bundle",
        "manifest_schema_invalid"
    );

    var wrongPlayer = Battle(1, "Player");
    wrongPlayer.Player.AccountId = "someone-else";
    ThrowsReason(
        () => BuildWith(new[] { wrongPlayer }),
        "invalid_bundle",
        "manifest_schema_invalid"
    );

    var invalidId = Battle(1, "Player");
    invalidId.BattleId = "bad/id";
    ThrowsReason(() => BuildWith(new[] { invalidId }), "invalid_bundle", "manifest_schema_invalid");

    var nullName = Battle(1, "Player");
    nullName.Player.DisplayName = null!;
    ThrowsReason(() => BuildWith(new[] { nullName }), "invalid_bundle", "manifest_schema_invalid");

    var negativeDay = Battle(1, "Player");
    negativeDay.Day = -1;
    ThrowsReason(
        () => BuildWith(new[] { negativeDay }),
        "invalid_bundle",
        "manifest_schema_invalid"
    );

    ThrowsReason(
        () =>
            BuildWith(
                Array.Empty<BundleBattleProjectionV5>(),
                new byte[BundleLimitsV5.MaxRunBytes + 1]
            ),
        "invalid_bundle",
        "run_too_large"
    );
    var maximumRun = BuildWith(
        Array.Empty<BundleBattleProjectionV5>(),
        new byte[BundleLimitsV5.MaxRunBytes]
    );
    Equal(
        BundleLimitsV5.MaxRunBytes,
        maximumRun.Manifest.Run.Payload.Length,
        "maximum Run accepted"
    );

    ThrowsReason(
        () =>
            BundleV5Codec.Build(
                BaseInput(
                    screenshot: new BundleScreenshotBuildInputV5
                    {
                        Bytes = new byte[BundleLimitsV5.MaxScreenshotBytes + 1],
                        Width = 1,
                        Height = 1,
                        Quality = 1,
                    }
                )
            ),
        "invalid_bundle",
        "screenshot_too_large"
    );
    var maximumScreenshot = BundleV5Codec.Build(
        BaseInput(
            screenshot: new BundleScreenshotBuildInputV5
            {
                Bytes = new byte[BundleLimitsV5.MaxScreenshotBytes],
                Width = 1,
                Height = 1,
                Quality = 1,
            }
        )
    );
    Equal(
        BundleLimitsV5.MaxScreenshotBytes,
        maximumScreenshot.Manifest.Screenshot!.Length,
        "maximum Screenshot accepted"
    );
}

void ReaderBoundsAndUnknownFields()
{
    Equal(8_388_607, BundleLimitsV5.MaxBundleBytes, "Bundle limit");
    Equal(2_097_152, BundleLimitsV5.MaxManifestBytes, "manifest limit");
    Equal(2_097_151, BundleLimitsV5.MaxRunBytes, "Run limit");
    Equal(1_048_576, BundleLimitsV5.MaxScreenshotBytes, "Screenshot limit");
    Equal(524_288, BundleLimitsV5.MaxProjectionBytes, "projection limit");

    var payload = new byte[] { 1 };
    var payloadDigest = BundleV5Codec.ComputeSha256Hex(payload);
    var minimal = MinimalManifest(payloadDigest);
    var withUnknown = minimal[..^1] + ",\"future\":{\"accepted\":true}}";
    var opened = BundleV5Codec.Open(RawBundle(withUnknown, payload));
    Equal("reader-run", opened.Manifest.Run.RunId, "unknown manifest fields ignored");

    var oversizedProjection = minimal.Replace(
        "\"run\":{},\"battles\":[]",
        "\"run\":{},\"battles\":[],\"future\":\""
            + new string('x', BundleLimitsV5.MaxProjectionBytes)
            + "\"",
        StringComparison.Ordinal
    );
    ThrowsReason(
        () => BundleV5Codec.Open(RawBundle(oversizedProjection, payload)),
        "invalid_bundle",
        "projection_too_large"
    );

    var trailing = RawBundle(minimal, new byte[] { 1, 2 });
    ThrowsReason(() => BundleV5Codec.Open(trailing), "invalid_bundle", "undeclared_trailing_bytes");

    var tooLarge = new byte[BundleLimitsV5.MaxBundleBytes + 1];
    ThrowsReason(() => BundleV5Codec.Open(tooLarge), "bundle_too_large", "bundle_too_large");

    var exactManifest = PadUnknownRootToLength(minimal, BundleLimitsV5.MaxManifestBytes);
    Equal(
        BundleLimitsV5.MaxManifestBytes,
        Encoding.UTF8.GetByteCount(exactManifest),
        "exact maximum manifest construction"
    );
    BundleV5Codec.Open(RawBundle(exactManifest, payload));

    var tooLongManifest = RawBundle(exactManifest + " ", payload);
    ThrowsReason(() => BundleV5Codec.Open(tooLongManifest), "invalid_bundle", "manifest_too_large");
}

void Ulids()
{
    var generator = new UlidV5Generator(() => 0, bytes => Array.Clear(bytes));
    var first = generator.Next();
    var second = generator.Next();
    Equal("00000000000000000000000000", first, "deterministic zero ULID");
    True(UlidV5Generator.IsCanonical(first), "canonical ULID");
    True(string.CompareOrdinal(first, second) < 0, "same-clock ULIDs are monotonic");
    True(!UlidV5Generator.IsCanonical("01j00000000000000000000000"), "lowercase ULID rejected");
}

void RunPayloadRoundTrip()
{
    var payload = GoldenPayload();
    var encoded = RunPayloadV5Codec.Encode(payload);
    var decoded = RunPayloadV5Codec.Decode(encoded);
    Equal(payload.RunId, decoded.RunId, "Run payload Run ID");
    Equal(payload.PlayerAccountId, decoded.PlayerAccountId, "Run payload player ID");
    Equal("战斗", decoded.Events[0].Kind, "Run payload Unicode");
    Equal("battle-000", decoded.ReplayableBattleIds[0], "Run payload replayable IDs");
    var golden = Convert.FromBase64String(
        TestInputs.Scratch(Path.Combine(fixtures, "run-payload-v5.fixture.b64")).Trim()
    );
    // Gzip headers and deflate output vary by OS/runtime. The wire contract is
    // the decompressed MessagePack, including every field and its encoding.
    SequenceEqual(Ungzip(golden), Ungzip(encoded), "stable Run payload MessagePack fixture");
    var goldenDecoded = RunPayloadV5Codec.Decode(golden);
    SequenceEqual(
        Ungzip(golden),
        Ungzip(RunPayloadV5Codec.Encode(goldenDecoded)),
        "golden Run payload decode and re-encode"
    );
}

byte[] Ungzip(byte[] bytes)
{
    using var input = new MemoryStream(bytes);
    using var gzip = new GZipStream(input, CompressionMode.Decompress);
    using var output = new MemoryStream();
    gzip.CopyTo(output);
    return output.ToArray();
}

void RunPayloadFailureBoundaries()
{
    True(
        !RunPayloadV5Codec.TryDecode(new byte[] { 0x1F }, out _, out var shortReason)
            && shortReason == "run_payload_empty",
        "short payload keeps its closed empty reason"
    );

    var versionFour = SamplePayload(Array.Empty<string>());
    versionFour.PayloadFormatVersion = 4;
    var options = MessagePackSerializerOptions
        .Standard.WithResolver(ContractlessStandardResolverAllowPrivate.Instance)
        .WithSecurity(MessagePackSecurity.UntrustedData);
    var versionFourBytes = Gzip(MessagePackSerializer.Serialize(versionFour, options));
    True(
        !RunPayloadV5Codec.TryDecode(versionFourBytes, out _, out var versionReason)
            && versionReason == "unsupported_run_payload_version",
        "wrong Run payload version keeps its closed reason"
    );

    var oversizedBytes = Gzip(new byte[RunPayloadV5Codec.MaxDecompressedBytes + 1]);
    True(
        !RunPayloadV5Codec.TryDecode(oversizedBytes, out _, out var sizeReason)
            && sizeReason == "run_payload_decompressed_too_large",
        "decompressed Run payload limit is enforced before MessagePack decoding"
    );
}

byte[] Gzip(byte[] bytes)
{
    using var output = new MemoryStream();
    using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        gzip.Write(bytes, 0, bytes.Length);
    return output.ToArray();
}

void RunBundleContract()
{
    var battle = ReplayBattle("battle-contract");
    var payload = SamplePayload(new[] { battle.BattleId });
    payload.Battles.Add(battle);
    var built = BundleV5Codec.Build(
        new BundleBuildInputV5
        {
            BundleId = "01J00000000000000000000905",
            CreatedAtMs = 1_785_628_800_000,
            RunId = payload.RunId,
            PlayerAccountId = payload.PlayerAccountId,
            RunPayload = RunPayloadV5Codec.Encode(payload),
        }
    );

    var opened = RunBundleV5Contract.Open(built.Bytes);
    True(opened.Succeeded, "Run Bundle contract opens a self-consistent bundle");
    True(
        opened.Value!.TryGetReplayableBattle(battle.BattleId, out var replayable),
        "Run Bundle contract resolves a replayable battle"
    );
    Equal(battle.BattleId, replayable!.BattleId, "resolved replayable battle ID");

    var identityMismatch = BundleV5Codec.Build(
        new BundleBuildInputV5
        {
            BundleId = "01J00000000000000000000906",
            CreatedAtMs = 1_785_628_800_000,
            RunId = "different-run",
            PlayerAccountId = payload.PlayerAccountId,
            RunPayload = RunPayloadV5Codec.Encode(payload),
        }
    );
    Equal(
        RunBundleOpenFailureKind.RunIdentityMismatch,
        RunBundleV5Contract.Open(identityMismatch.Bytes).FailureKind,
        "manifest/payload identity mismatch classification"
    );

    var missingLabel = ReplayBattle("missing-label");
    missingLabel.Snapshots!.CardSets.RemoveAt(0);
    True(!RunBundleV5Contract.IsReplayable(missingLabel), "missing card-set label rejected");

    var duplicateLabel = ReplayBattle("duplicate-label");
    duplicateLabel.Snapshots!.CardSets[3].Label = "player_hand";
    True(!RunBundleV5Contract.IsReplayable(duplicateLabel), "duplicate card-set label rejected");

    var extraLabel = ReplayBattle("extra-label");
    extraLabel.Snapshots!.CardSets.Add(new BattleCardSetV5 { Label = "future" });
    True(!RunBundleV5Contract.IsReplayable(extraLabel), "extra card set rejected");

    var emptyPhase = ReplayBattle("empty-phase");
    emptyPhase.Replay!.CombatMessageBytes = Array.Empty<byte>();
    True(!RunBundleV5Contract.IsReplayable(emptyPhase), "empty replay phase rejected");

    var nilCardSets = ReplayBattle("nil-card-sets");
    nilCardSets.Snapshots!.CardSets = null!;
    True(!RunBundleV5Contract.IsReplayable(nilCardSets), "nil card sets rejected");

    opened.Value.Payload.ReplayableBattleIds = null!;
    True(
        !opened.Value.TryGetReplayableBattle(battle.BattleId, out _),
        "nil replayable-battle IDs rejected"
    );
    opened.Value.Payload.ReplayableBattleIds = [battle.BattleId];
    opened.Value.Payload.Battles = null!;
    True(!opened.Value.TryGetReplayableBattle(battle.BattleId, out _), "nil battles rejected");
}

RunBattleV5 ReplayBattle(string battleId) =>
    new()
    {
        BattleId = battleId,
        Snapshots = new BattleCardSnapshotsV5
        {
            CardSets =
            [
                new BattleCardSetV5 { Label = "player_hand", Status = "Captured" },
                new BattleCardSetV5 { Label = "player_skills", Status = "Captured" },
                new BattleCardSetV5 { Label = "opponent_hand", Status = "Captured" },
                new BattleCardSetV5 { Label = "opponent_skills", Status = "Captured" },
            ],
        },
        Replay = new BattleReplayV5
        {
            SpawnMessageBytes = new byte[] { 1 },
            CombatMessageBytes = new byte[] { 2 },
            DespawnMessageBytes = new byte[] { 3 },
        },
    };

BundleBuildResultV5 BuildWith(
    IReadOnlyList<BundleBattleProjectionV5> battles,
    byte[]? payload = null
) => BundleV5Codec.Build(BaseInput(battles, payload));

BundleBuildInputV5 BaseInput(
    IReadOnlyList<BundleBattleProjectionV5>? battles = null,
    byte[]? payload = null,
    BundleScreenshotBuildInputV5? screenshot = null
) =>
    new()
    {
        BundleId = "01J00000000000000000000903",
        CreatedAtMs = 1_785_628_800_000,
        RunId = "test-run",
        PlayerAccountId = "golden-account",
        Battles = battles ?? Array.Empty<BundleBattleProjectionV5>(),
        RunPayload = payload ?? new byte[] { 1 },
        Screenshot = screenshot,
    };

BundleBattleProjectionV5 Battle(int index, string displayName) =>
    new()
    {
        BattleId = $"battle-{index:000}",
        RecordedAtMs = 1_785_628_700_000 + index,
        Day = 10,
        Hour = index,
        EncounterId = null,
        CombatKind = "pvp",
        Result = index % 2 == 0 ? "win" : "loss",
        WinnerCombatantId = null,
        LoserCombatantId = null,
        IsFinalBattle = index == 29,
        Player = new BundleCombatantProjectionV5
        {
            AccountId = "golden-account",
            DisplayName = displayName,
            HeroId = null,
            HeroName = "Vanessa",
            Rank = "Gold",
            Rating = 1234,
            Level = 10,
            Prestige = 2,
            Victories = 9,
        },
        Opponent = new BundleCombatantProjectionV5
        {
            AccountId = $"opponent-{index:000}",
            DisplayName = "Opponent",
            HeroId = null,
            HeroName = "Pygmalien",
            Rank = "Gold",
            Rating = 1200,
            Level = 10,
            Prestige = 3,
            Victories = 8,
        },
    };

RunPayloadV5 SamplePayload(IEnumerable<string> battleIds) =>
    new()
    {
        RunId = "payload-run",
        PlayerAccountId = "golden-account",
        Run = new RunFactsV5
        {
            Hero = "Vanessa",
            GameMode = "Ranked",
            StartedAtUtc = "2026-08-03T00:00:00.0000000+00:00",
            EndedAtUtc = "2026-08-03T00:30:00.0000000+00:00",
            Status = "completed",
            ModVersion = "5.0.0",
        },
        Events = new List<RunEventV5>
        {
            new()
            {
                Seq = 1,
                TimestampUtc = "2026-08-03T00:01:00.0000000+00:00",
                Kind = "战斗",
                PayloadJson = "{\"ok\":true}",
            },
        },
        ReplayableBattleIds = battleIds.ToList(),
        Degradation = new PayloadDegradationV5(),
    };

RunPayloadV5 GoldenPayload()
{
    var payload = SamplePayload(new[] { "battle-000" });
    payload.Run.Seed = 123456;
    payload.Run.Day = 10;
    payload.Run.Hour = 2;
    payload.Run.Victories = 1;
    payload.Run.Losses = 0;
    payload.Run.PlayerRank = "Gold";
    payload.Run.PlayerRating = 1200;
    payload.Run.FinalPlayerRank = "Legendary";
    payload.Run.FinalPlayerRating = 1234;
    payload.Run.FinalPlayerRatingDelta = 34;
    payload.Run.MaxHealth = 2500;
    payload.Run.Prestige = 3;
    payload.Run.Level = 11;
    payload.Run.Income = 8;
    payload.Run.Gold = 17;
    payload.Run.BuildChannel = "Online";
    var battle = ReplayBattle("battle-000");
    battle.Facts = new BattleFactsV5
    {
        RecordedAtUtc = "2026-08-03T00:29:00.0000000+00:00",
        Day = 10,
        Hour = 1,
        EncounterId = "encounter-final",
        CombatKind = "PvP",
        Result = "Win",
        WinnerCombatantId = "Player",
        LoserCombatantId = "Opponent",
        IsFinalBattle = true,
    };
    battle.Participants = new BattleParticipantsV5
    {
        Player = new BattleParticipantV5
        {
            AccountId = payload.PlayerAccountId,
            DisplayName = "玩家😀",
            HeroName = "Vanessa",
            Rank = "Gold",
            Rating = 1200,
            Level = 11,
            Prestige = 3,
            Victories = 0,
            Income = 8,
            Gold = 17,
            HandItemCount = 1,
            SkillCount = 2,
        },
        Opponent = new BattleParticipantV5
        {
            AccountId = "golden-opponent",
            DisplayName = "Opponent",
            HeroName = "Pygmalien",
            Rank = "Silver",
            Rating = 1100,
            Level = 12,
            Prestige = 4,
            Victories = 5,
            Income = 9,
            Gold = 18,
            HandItemCount = 2,
            SkillCount = 1,
        },
    };
    for (var index = 0; index < battle.Snapshots!.CardSets.Count; index++)
    {
        var cardSet = battle.Snapshots.CardSets[index];
        cardSet.Source = "capture";
        cardSet.Items.Add(
            new BattleCardV5
            {
                InstanceId = $"instance-{index}",
                TemplateId = $"00000000-0000-0000-0000-{index + 1:000000000000}",
                Type = index % 2 == 0 ? 1 : 2,
                Size = index % 2 == 0 ? 2 : 1,
                Section = index,
                Socket = index + 4,
                Name = $"Golden card {index}",
                Tier = index % 2 == 0 ? "Gold" : "Silver",
                Enchant = index == 0 ? "Burning" : null,
                Tags = new List<string> { "Weapon", $"golden-{index}" },
                Attributes = new Dictionary<string, int>
                {
                    ["damage"] = 100 + index,
                    ["cooldown"] = 2000 + index,
                },
            }
        );
    }
    payload.Battles.Add(battle);
    payload.Degradation.Categories = new List<string> { "events_omitted", "screenshot_omitted" };
    payload.Degradation.EventsOmitted = 2;
    payload.Degradation.ScreenshotOmitted = true;
    return payload;
}

string MinimalManifest(string payloadDigest) =>
    "{\"bundle_id\":\"01J00000000000000000000904\",\"bundle_version\":5,\"created_at_ms\":1785628800000,\"run\":{\"run_id\":\"reader-run\",\"player_account_id\":\"reader-account\",\"run_format_version\":5,\"projection\":{\"run\":{},\"battles\":[]},\"payload\":{\"offset\":0,\"length\":1,\"sha256\":\""
    + payloadDigest
    + "\",\"content_type\":\"application/x-bpp-run-v5\"}}}";

string PadUnknownRootToLength(string manifest, int byteLength)
{
    const string prefix = ",\"padding\":\"";
    const string suffix = "\"}";
    var baseWithoutClose = manifest[..^1];
    var fixedBytes = Encoding.UTF8.GetByteCount(baseWithoutClose + prefix + suffix);
    return baseWithoutClose + prefix + new string('a', byteLength - fixedBytes) + suffix;
}

byte[] RawBundle(string manifest, byte[] payload)
{
    var manifestBytes = Encoding.UTF8.GetBytes(manifest);
    var bytes = new byte[16 + manifestBytes.Length + payload.Length];
    Encoding.ASCII.GetBytes("BPPBNDL5").CopyTo(bytes, 0);
    WriteBigEndian(bytes, 8, 5);
    WriteBigEndian(bytes, 12, manifestBytes.Length);
    manifestBytes.CopyTo(bytes, 16);
    payload.CopyTo(bytes, 16 + manifestBytes.Length);
    return bytes;
}

void WriteBigEndian(byte[] bytes, int offset, int value)
{
    bytes[offset] = (byte)((uint)value >> 24);
    bytes[offset + 1] = (byte)((uint)value >> 16);
    bytes[offset + 2] = (byte)((uint)value >> 8);
    bytes[offset + 3] = (byte)value;
}

byte[] ReadBundle(string name) =>
    Convert.FromBase64String(TestInputs.Scratch(Path.Combine(fixtures, name)).Trim());

void ThrowsReason(Action action, string code, string reason)
{
    try
    {
        action();
    }
    catch (BundleV5Exception ex)
    {
        Equal(code, ex.Code, $"{reason} code");
        Equal(reason, ex.Reason, $"{reason} reason");
        return;
    }
    throw new InvalidOperationException($"Expected BundleV5Exception {code}/{reason}.");
}

void True(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

void Equal<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{message}: expected {expected}, actual {actual}.");
}

void SequenceEqual(byte[] expected, byte[] actual, string message)
{
    if (!CryptographicOperations.FixedTimeEquals(expected, actual))
        throw new InvalidOperationException($"{message}: byte sequences differ.");
}

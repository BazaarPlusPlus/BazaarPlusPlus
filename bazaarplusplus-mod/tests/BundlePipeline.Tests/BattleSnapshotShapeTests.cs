using System.Text;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Game.BundlePipeline;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Game.PvpBattles.Persistence;
using BazaarPlusPlus.Storage.Paths;
using BazaarPlusPlus.Storage.RunLog;
using BazaarPlusPlus.Storage.Sqlite;
using BazaarPlusPlus.TestSupport;
using Microsoft.Data.Sqlite;

// The battle_snapshots JSON the desktop writes, pinned byte for byte by
// fixtures/battle-snapshots.golden.txt. Core's PvpBattleCardSetDocument owns that shape: the
// Android host writes it directly and the Bundle composer reads it, so a desktop capture must read
// back through it unchanged and round-trip through the desktop catalog unchanged.
internal static class BattleSnapshotShapeTests
{
    private const string GoldenRelativePath =
        "tests/BundlePipeline.Tests/fixtures/battle-snapshots.golden.txt";

    private static readonly string[] Columns =
    [
        "player_hand_json",
        "player_skills_json",
        "opponent_hand_json",
        "opponent_skills_json",
    ];

    internal static void Run(string root)
    {
        Directory.CreateDirectory(root);
        var database = PathConstants.RunLogDatabase(root);
        new RunLogStore(new TestPaths(root)).CreateRun(
            new RunLogCreateRequest
            {
                RunId = "run-shape",
                StartedAtUtc = PipelineInputs.Base,
                Hero = "Vanessa",
                GameMode = "Ranked",
                PlayerAccountId = "account-001",
                ModVersion = "6.0.0",
            }
        );
        var catalog = new PvpBattleCatalog(new PvpBattleSqliteStore(database));
        catalog.Save(Manifest("battle-shape", Snapshots()));

        var written = ReadColumns(database, "battle-shape");
        CompareWithGolden(written);

        foreach (var json in written)
            Check.That(
                PvpBattleCardSetDocument.Serialize(PvpBattleCardSetDocument.Deserialize(json))
                    == json,
                "Core's document reads a desktop capture back unchanged."
            );

        var loaded =
            catalog.TryLoad("battle-shape")
            ?? throw new InvalidOperationException("The saved battle should load.");
        catalog.Save(Manifest("battle-shape-again", loaded.Snapshots));
        Check.That(
            ReadColumns(database, "battle-shape-again").SequenceEqual(written),
            "A loaded capture saves back to the same JSON."
        );

        SizelessAndUnnamedCards(root, catalog, database);
    }

    // A card the game gave no size (its unnamed value 0) is stored with the "0" every host writes,
    // reads back without a size, and reaches the Bundle as size 0. A stored type no facet names
    // (a number another host wrote for a member the facets lack) drops the card from the facet
    // view; the Bundle composer, which reads the stored JSON, still carries the game's value.
    private static void SizelessAndUnnamedCards(
        string root,
        PvpBattleCatalog catalog,
        string database
    )
    {
        var store = new RunLogStore(new TestPaths(root));
        store.CompleteRun(
            "run-shape",
            new RunLogCompletion
            {
                EndedAtUtc = PipelineInputs.Base.AddMinutes(5),
                Status = "completed",
            }
        );
        var sizeless = new PvpBattleCardSetCapture
        {
            Status = PvpBattleCaptureStatus.Captured,
            Source = PvpBattleCaptureSource.OpeningMessage,
            Items =
            [
                new PvpBattleCardSnapshot
                {
                    InstanceId = "skl_sizeless",
                    TemplateId = "6a0c3f5e-0000-4000-8000-000000000004",
                    Type = CardType.Skill,
                    Size = null,
                },
            ],
        };
        catalog.Save(
            Manifest(
                "battle-sizeless",
                new PvpBattleSnapshots
                {
                    PlayerHand = Missing(),
                    PlayerSkills = sizeless,
                    OpponentHand = Missing(),
                    OpponentSkills = Missing(),
                }
            )
        );
        var column = ReadColumns(database, "battle-sizeless")[1];
        Check.That(
            column.Contains("\"size\":\"0\"", StringComparison.Ordinal),
            "A card without a size is stored with size \"0\"."
        );
        var loaded = catalog.TryLoad("battle-sizeless")!.Snapshots.PlayerSkills.Items.Single();
        Check.That(loaded.Size == null, "A stored size \"0\" reads back as no size.");

        var composed = new RunPayloadComposer(
            database,
            Path.Combine(root, "replays"),
            DesktopPvpBattleEvidence.Instance,
            SqliteOpenOptions.Default
        ).Compose("run-shape", "account-001");
        var card = composed
            .Payload.Battles.Single(battle => battle.BattleId == "battle-sizeless")
            .Snapshots!.CardSets.Single(set => set.Label == "player_skills")
            .Items.Single();
        Check.That(card.Size == 0, "A card without a size reaches the Bundle as size 0.");

        var unnamed = PvpBattleCardSetDocument.Serialize(
            new PvpBattleCardSetDocument
            {
                Status = PvpBattleCaptureStatus.Captured,
                Items =
                [
                    new PvpBattleCardDocument
                    {
                        InstanceId = "card-unnamed",
                        Type = "99",
                        Size = "Small",
                    },
                    new PvpBattleCardDocument
                    {
                        InstanceId = "card-named",
                        Type = "Item",
                        Size = "Small",
                        Section = "5",
                        Socket = "Socket_42",
                    },
                ],
            }
        );
        var read = PvpBattleSnapshotsCodec.Deserialize(unnamed).Items.Single();
        Check.That(
            read.InstanceId == "card-named" && read.Section == null && read.Socket == null,
            "A type no facet names drops the card; a section or socket no facet names reads as none."
        );
    }

    private static PvpBattleSnapshots Snapshots() =>
        new()
        {
            PlayerHand = new PvpBattleCardSetCapture
            {
                Status = PvpBattleCaptureStatus.Captured,
                Source = PvpBattleCaptureSource.OpeningMessage,
                Items =
                [
                    new PvpBattleCardSnapshot
                    {
                        InstanceId = "itm_hand_0",
                        TemplateId = "6a0c3f5e-0000-4000-8000-000000000001",
                        Type = CardType.Item,
                        Size = CardSize.Medium,
                        Section = CardSection.Hand,
                        Socket = CardSocket.Socket_3,
                        Name = "Pigomorph",
                        Tier = "Silver",
                        Enchant = "Golden",
                        Tags = ["Weapon", "Core"],
                        Attributes = new Dictionary<string, int>
                        {
                            ["DamageAmount"] = 20,
                            ["CooldownMax"] = 5000,
                        },
                    },
                    new PvpBattleCardSnapshot
                    {
                        InstanceId = "itm_hand_1",
                        TemplateId = "6a0c3f5e-0000-4000-8000-000000000002",
                        Type = CardType.SocketEffect,
                        Size = CardSize.Small,
                        Section = CardSection.Hand,
                        Socket = CardSocket.Socket_0,
                        Name = null,
                        Tier = "Bronze",
                        Enchant = string.Empty,
                    },
                ],
            },
            PlayerSkills = new PvpBattleCardSetCapture
            {
                Status = PvpBattleCaptureStatus.Captured,
                Source = PvpBattleCaptureSource.LiveRetry,
                Items =
                [
                    new PvpBattleCardSnapshot
                    {
                        InstanceId = "skl_0",
                        TemplateId = "6a0c3f5e-0000-4000-8000-000000000003",
                        Type = CardType.Skill,
                        Size = CardSize.Medium,
                        Section = null,
                        Socket = null,
                        Name = "Coup de Grâce \"终结\"",
                        Tier = "Gold",
                        Enchant = "None",
                        Tags = ["Skill"],
                        Attributes = new Dictionary<string, int> { ["Custom_0"] = -3 },
                    },
                ],
            },
            OpponentHand = new PvpBattleCardSetCapture
            {
                Status = PvpBattleCaptureStatus.CapturedEmpty,
                Source = PvpBattleCaptureSource.OpeningMessage,
            },
            OpponentSkills = Missing(),
        };

    private static PvpBattleCardSetCapture Missing() =>
        new() { Status = PvpBattleCaptureStatus.Missing, Source = PvpBattleCaptureSource.Unknown };

    private static PvpBattleManifest Manifest(string battleId, PvpBattleSnapshots snapshots) =>
        new()
        {
            BattleId = battleId,
            RunId = "run-shape",
            RecordedAtUtc = PipelineInputs.Base.AddSeconds(30),
            CombatKind = "PVPCombat",
            Day = 2,
            Hour = 5,
            Participants = new PvpBattleParticipants { PlayerAccountId = "account-001" },
            Outcome = new PvpBattleOutcome { Result = "win" },
            Snapshots = snapshots,
        };

    private static string[] ReadColumns(string database, string battleId)
    {
        using var connection = new SqliteConnection($"Data Source={database}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT {string.Join(", ", Columns)} FROM battle_snapshots WHERE battle_id=$id;";
        command.Parameters.AddWithValue("$id", battleId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            throw new InvalidOperationException($"{battleId} has no battle_snapshots row.");
        return Columns.Select((_, index) => reader.GetString(index)).ToArray();
    }

    private static void CompareWithGolden(string[] written)
    {
        var actual = string.Concat(
            Columns.Select((column, index) => $"{column} {written[index]}\n")
        );
        var goldenPath = Path.Combine(TestInputs.RepoRoot, GoldenRelativePath);
        if (Environment.GetEnvironmentVariable("BPP_UPDATE_GOLDENS") == "1")
        {
            File.WriteAllText(goldenPath, actual, new UTF8Encoding(false));
            Console.WriteLine($"Rewrote {GoldenRelativePath}; review it with git diff.");
            return;
        }
        var expected = TestInputs.Fixture(GoldenRelativePath).Replace("\r\n", "\n");
        if (string.Equals(expected, actual, StringComparison.Ordinal))
            return;
        throw new InvalidOperationException(
            $"battle_snapshots JSON differs from {GoldenRelativePath}. Regenerate with "
                + "BPP_UPDATE_GOLDENS=1 only if the change is intended.\n"
                + UnifiedDiff.Render(expected, actual, GoldenRelativePath)
        );
    }
}

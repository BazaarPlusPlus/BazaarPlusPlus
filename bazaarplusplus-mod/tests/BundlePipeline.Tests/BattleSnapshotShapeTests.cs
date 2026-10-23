using System.Text;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Game.PvpBattles.Persistence;
using BazaarPlusPlus.Storage.Paths;
using BazaarPlusPlus.Storage.RunLog;
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
        var catalog = new PvpBattleCatalog(database);
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

        UnnamedEnumValuesRoundTrip(catalog, database);
    }

    // A value the game has no member name for is stored as its number and read back as that value.
    private static void UnnamedEnumValuesRoundTrip(PvpBattleCatalog catalog, string database)
    {
        var unnamed = new PvpBattleCardSetCapture
        {
            Status = PvpBattleCaptureStatus.Captured,
            Source = PvpBattleCaptureSource.OpeningMessage,
            Items =
            [
                new PvpBattleCardSnapshot
                {
                    InstanceId = "card-unnamed",
                    TemplateId = "template-unnamed",
                    Type = (ECardType)99,
                    Size = (ECardSize)7,
                    Section = (EInventorySection)5,
                    Socket = (EContainerSocketId)42,
                },
            ],
        };
        catalog.Save(
            Manifest(
                "battle-unnamed",
                new PvpBattleSnapshots
                {
                    PlayerHand = unnamed,
                    PlayerSkills = Missing(),
                    OpponentHand = Missing(),
                    OpponentSkills = Missing(),
                }
            )
        );
        var card = PvpBattleCardSetDocument
            .Deserialize(ReadColumns(database, "battle-unnamed")[0])
            .Items.Single();
        Check.That(
            card.Type == "99" && card.Size == "7" && card.Section == "5" && card.Socket == "42",
            "An unnamed enum value is stored as its number."
        );
        var loaded = catalog.TryLoad("battle-unnamed")!.Snapshots.PlayerHand.Items.Single();
        Check.That(
            loaded.Type == (ECardType)99
                && loaded.Size == (ECardSize)7
                && loaded.Section == (EInventorySection)5
                && loaded.Socket == (EContainerSocketId)42,
            "An unnamed enum value reads back as the same value."
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
                        Type = ECardType.Item,
                        Size = ECardSize.Medium,
                        Section = EInventorySection.Hand,
                        Socket = EContainerSocketId.Socket_3,
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
                        Type = ECardType.SocketEffect,
                        Size = ECardSize.Small,
                        Section = EInventorySection.Hand,
                        Socket = EContainerSocketId.Socket_0,
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
                        Type = ECardType.Skill,
                        Size = ECardSize.Medium,
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

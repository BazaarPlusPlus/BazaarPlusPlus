#nullable enable
using System.IO.Compression;
using System.Net.Http.Headers;
using Xunit;
using static Architecture.Tests.ArchitectureRules;

namespace Architecture.Tests;

public sealed class V5DataPipelineArchitectureTests
{
    private const string ModApi = CompiledArtifacts.ModApiAssembly;
    private const string Clients = "BazaarPlusPlus.ModApi.Clients";

    [Fact]
    public void ModApi_has_only_v5_bundle_routes_and_no_multipart_wire()
    {
        Holds(
            "The mod API speaks only the v5 bundle routes over MessagePack, never multipart.",
            build =>
            {
                var modApi = build.Assembly(ModApi).Types;
                return Literals(
                        modApi,
                        StringComparison.OrdinalIgnoreCase,
                        "mod-api-v4",
                        "/run-bundles",
                        "multipart",
                        "bazaardb/snapshots"
                    )
                    .Concat(References(modApi, Is(typeof(MultipartFormDataContent).FullName!)));
            }
        );
    }

    [Fact]
    public void Run_bundle_writer_and_ghost_reader_share_one_replayability_contract()
    {
        Holds(
            "Ghost sync opens bundles through RunBundleV5Contract, not the raw payload codec.",
            build =>
                References(
                    build.Type("BazaarPlusPlus.Game.HistoryPanel.Ghost.GhostBattleSyncService"),
                    Is(build.Type("BazaarPlusPlus.ModApi.Bundle.RunPayloadV5Codec").FullName)
                )
        );
    }

    [Fact]
    public void ModApi_has_one_owner_for_messagepack_gzip_framing()
    {
        Holds(
            "MessagePackGzipFraming is the only gzip owner in the mod API.",
            build =>
            {
                var owner = build.Type("BazaarPlusPlus.ModApi.MessagePackGzipFraming");
                return References(
                    build.Assembly(ModApi).Types.Where(type => type != owner),
                    Is(typeof(GZipStream).FullName!)
                );
            }
        );
    }

    [Fact]
    public void ModApi_clients_delegate_shared_response_metadata_and_error_envelopes()
    {
        var retryAfter = (
            typeof(HttpResponseHeaders).FullName!,
            "get_" + nameof(HttpResponseHeaders.RetryAfter)
        );
        Holds(
            "Clients read Retry-After through ModApiResponse, not response headers.",
            build => Accesses(build.TypesIn(Clients), retryAfter)
        );
    }

    [Fact]
    public void ModApi_session_is_the_only_public_mod_backend_client_chain()
    {
        Holds(
            "Only ModApiSession constructs backend clients, and it never exposes its transport.",
            build =>
            {
                var session = build.Type(Clients + ".ModApiSession");
                var clients = new[]
                {
                    "BundleUploadClient",
                    "GhostBattleClient",
                    "ModApiHealthClient",
                }
                    .Select(name => build.Type(Clients + "." + name).FullName)
                    .ToArray();
                var transport = new[]
                {
                    typeof(HttpClient).FullName!,
                    build.Type("BazaarPlusPlus.ModApi.ModApiRoutes").FullName,
                };
                var constructions = build
                    .Types.Where(type => type != session)
                    .SelectMany(type => type.Methods)
                    .SelectMany(method =>
                        method
                            .Instructions.Where(op =>
                                op.OpCode == "newobj"
                                && clients.Contains(op.TargetType?.FullName, StringComparer.Ordinal)
                            )
                            .Select(op => $"{method} {op}")
                    );
                var exposed = session
                    .PublicSurface.Where(member =>
                        member.Type is { } type && transport.Contains(type.FullName)
                    )
                    .Select(member => $"{session} exposes {member.Member} as {member.Type}");
                return constructions.Concat(exposed);
            }
        );
    }

    [Fact]
    public void Bundle_queue_store_is_the_only_runtime_owner_of_queue_sql()
    {
        var sqliteConnection = CompiledArtifacts.Universe.RequireType(
            "Microsoft.Data.Sqlite.SqliteConnection"
        );
        Holds(
            "Queue SQL lives in BundleQueueStore and the schema; the pipeline never opens SQLite.",
            build =>
            {
                // PvpBattleSqliteStore's replay-maintenance query only reads both tables to keep
                // payloads of runs with pending seal or upload work (Seal eligibility consumer).
                var owners = new[]
                {
                    build.Type("BazaarPlusPlus.Storage.BundleQueue.BundleQueueStore"),
                    build.Type("BazaarPlusPlus.Storage.RunLog.RunLogSchema"),
                    build.Type("BazaarPlusPlus.Game.PvpBattles.Persistence.PvpBattleSqliteStore"),
                };
                var pipeline = new[]
                {
                    build.Type("BazaarPlusPlus.Game.BundlePipeline.BundleSealCoordinator"),
                    build.Type("BazaarPlusPlus.Game.BundlePipeline.BundleUploadFeed"),
                };
                return Literals(
                        build.Types.Where(type => !owners.Contains(type)),
                        StringComparison.Ordinal,
                        "bundle_seal_jobs",
                        "bundle_outbox"
                    )
                    .Concat(References(pipeline, Is(sqliteConnection)));
            }
        );
    }
}

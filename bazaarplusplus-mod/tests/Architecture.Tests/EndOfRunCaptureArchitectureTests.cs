#nullable enable
using Xunit;
using static Architecture.Tests.ArchitectureRules;

namespace Architecture.Tests;

public sealed class EndOfRunCaptureArchitectureTests
{
    private const string Screenshots = "BazaarPlusPlus.Game.Screenshots";

    [Fact]
    public void Patches_express_only_the_two_workflow_intents()
    {
        Holds(
            "End-of-run patches talk to the workflow only, never the driver, service, or core.",
            build =>
            {
                var internals = new[]
                {
                    build.Type(Screenshots + ".EndOfRunCaptureDriver").FullName,
                    build.Type(Screenshots + ".ScreenshotService").FullName,
                    build.Type(Screenshots + ".EndOfRunCaptureWorkflowCore`1").FullName,
                };
                return References(build.TypesIn("BazaarPlusPlus.Patches.EndOfRun"), Is(internals));
            }
        );
    }

    [Fact]
    public void Composition_constructs_workflow_before_publishing_patch_features_and_mounting_driver()
    {
        Holds(
            "BppComposition builds the workflow, then publishes patch features, then mounts the driver.",
            build =>
            {
                var workflow = build.Type(Screenshots + ".EndOfRunCaptureWorkflow").FullName;
                var features = build.Type("BazaarPlusPlus.Patches.BppPatchFeatures").FullName;
                var mount = build.Type("BazaarPlusPlus.Core.Runtime.ComponentMount`1").FullName;
                var driver = build.Type(Screenshots + ".EndOfRunCaptureDriver").FullName;
                var composition = build.Type("BazaarPlusPlus.BppComposition");
                var constructor = composition
                    .MethodClosure(".ctor")
                    .Single(method =>
                        method.Name == ".ctor" && method.DeclaringType == composition.FullName
                    );
                var order = new[]
                {
                    FirstConstruction(constructor, op => op.TargetType?.FullName == workflow),
                    FirstConstruction(constructor, op => op.TargetType?.FullName == features),
                    FirstConstruction(
                        constructor,
                        op =>
                            op.TargetType?.FullName == mount
                            && op.MentionedTypes.Any(type => type.FullName == driver)
                    ),
                };
                return order.Any(index => index < 0) || !order.SequenceEqual(order.Order())
                    ? new[] { $"{constructor} construction order is {string.Join(", ", order)}" }
                    : Array.Empty<string>();
            }
        );
    }

    [Fact]
    public void Screenshot_persistence_cannot_resample_live_game_state()
    {
        Holds(
            "Persistence maps frozen capture metadata; it cannot sample live run state.",
            build =>
            {
                var snapshots = Is(
                    build.Type("BazaarPlusPlus.Core.GameState.RunBasicsSnapshot").FullName,
                    build.Type("BazaarPlusPlus.Core.GameState.RankSnapshot").FullName
                );
                var services = Is(build.Type("BazaarPlusPlus.Core.Runtime.IBppServices").FullName);
                var persistence = build.Type(Screenshots + ".EndOfRunArtifactPersistence");
                var mapper = build.Type(Screenshots + ".RunScreenshotRecordMapper");
                // The constructor may read static facts (build channel, data root) from the
                // services; nothing may keep them to sample live state at persist time.
                var retained = persistence
                    .Fields.Where(field => field.Type is { } type && services(type))
                    .Select(field => $"{field.DeclaringType}::{field.Name} retains {field.Type}");
                var sampled = persistence
                    .Methods.Where(method =>
                        !(method.Name == ".ctor" && method.DeclaringType == persistence.FullName)
                        && method.References.Any(services)
                    )
                    .Select(method => $"{method} reads IBppServices");
                return References(new[] { persistence, mapper }, snapshots)
                    .Concat(References(mapper, services))
                    .Concat(retained)
                    .Concat(sampled);
            }
        );
        Holds(
            "Metadata must be frozen before frame acquisition can release Continue.",
            build =>
            {
                var service = build.Type(Screenshots + ".ScreenshotService");
                var metadata = build.Type(Screenshots + ".ScreenshotCaptureMetadata").FullName;
                var begin = service
                    .MethodClosure("BeginCaptureCurrentFrame")
                    .First(method => method.Name == "BeginCaptureCurrentFrame");
                var capture = FirstCall(begin, metadata, "Capture");
                var acquire = FirstCall(
                    begin,
                    service.FullName,
                    "CaptureAndWriteCurrentFrameAsync"
                );
                return capture >= 0 && acquire > capture
                    ? Array.Empty<string>()
                    : new[] { $"{begin} captures metadata at {capture}, acquires at {acquire}" };
            }
        );
    }

    private static int FirstConstruction(CompiledMethod method, Func<IlInstruction, bool> match) =>
        IndexOf(method, op => op.OpCode == "newobj" && match(op));

    private static int FirstCall(CompiledMethod method, string type, string member) =>
        IndexOf(method, op => op.IsMemberAccess && op.Targets(type, member));

    private static int IndexOf(CompiledMethod method, Func<IlInstruction, bool> match)
    {
        for (var index = 0; index < method.Instructions.Count; index++)
            if (match(method.Instructions[index]))
                return index;
        return -1;
    }
}

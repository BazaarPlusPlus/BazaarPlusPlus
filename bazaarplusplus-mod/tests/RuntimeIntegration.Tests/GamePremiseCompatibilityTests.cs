#nullable enable
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using BazaarPlusPlus.GameInterop.AssetLoading;
using BazaarPlusPlus.Patches;
using BazaarPlusPlus.TestSupport;
using Xunit;

namespace RuntimeIntegration.Tests;

// Checks the mod's premises about the game against the real assemblies under ManagedPath:
// signatures by reflection, method-body calls by scanning IL. The channel comes from the
// GameBuildInfoResolver probe (TheBazaar.Config declares ServerOption only on PTR); premises
// shared by online and PTR run on both.
//
// Every run writes artifacts/compatibility/<channel>-<buildid>.json, also when a premise
// fails, with no timestamps, so two runs against one ManagedPath are byte-identical and a
// diff between two builds' reports names the premise that drifted.
//
// Failure modes: a premise reports resolved=false when the game renamed, removed, retyped,
// or re-scoped the member, or when a method body stopped calling the target. A snapshot
// directory whose name declares a channel the probe disagrees with fails the
// snapshot-channel premise. The buildid is "unknown" when ManagedPath is neither a
// game-libs snapshot nor under a Steam library holding appmanifest_1617400.acf.
public sealed class GamePremiseCompatibilityTests
{
    private const BindingFlags Declared =
        BindingFlags.Public
        | BindingFlags.NonPublic
        | BindingFlags.Instance
        | BindingFlags.Static
        | BindingFlags.DeclaredOnly;

    private static readonly Regex SnapshotPath = new(
        @"(?:^|/)game-libs/(online|ptr)-([^/]+)/Managed$",
        RegexOptions.CultureInvariant
    );

    private static readonly Regex BuildIdLine = new(
        "^\"buildid\"\\s+\"([^\"]+)\"",
        RegexOptions.CultureInvariant
    );

    [Fact]
    public void Game_premises_hold_for_the_managed_assemblies_and_are_reported()
    {
        var managedPath = typeof(GamePremiseCompatibilityTests)
            .Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "BppManagedPath")
            .Value!;
        // The build copies only the game assemblies the mod compiles against; member
        // signatures also reach ones it does not (DOTween), which load from ManagedPath.
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            var path = Path.Combine(managedPath, name.Name + ".dll");
            return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
        };
        var runtime = Assembly.Load("TheBazaarRuntime");
        var hasServerOption =
            runtime
                .GetType("TheBazaar.Config", throwOnError: true)!
                .GetNestedType("ServerOption", BindingFlags.Public | BindingFlags.NonPublic)
            != null;
        var channel = hasServerOption ? "ptr" : "online";
        var (snapshotChannel, buildId) = IdentifyBuild(managedPath);

        var premises = new Premises(runtime);
        var overloads = new SortedSet<string>(StringComparer.Ordinal);
        try
        {
            if (snapshotChannel != null)
                premises.Snapshot(snapshotChannel, channel, buildId);
            if (channel == "ptr")
                CheckPtrDispatch(premises);
            else
                CheckOnlineDispatch(premises);
            CheckTooltipGates(premises);
            CheckCardPreview(premises);
            CheckSkinPortraits(premises);
            CheckAssetLoaderOverloads(premises, overloads);
        }
        finally
        {
            premises.Dispose();
            WriteReport(managedPath, channel, buildId, premises.Results, overloads);
        }

        var failed = premises
            .Results.Where(result => !result.Resolved)
            .Select(result => $"{result.Premise} [{result.Target}]")
            .ToArray();
        Assert.True(
            failed.Length == 0,
            $"{failed.Length} game premise(s) drifted on {channel}-{buildId}:\n  "
                + string.Join("\n  ", failed)
        );
    }

    private static void CheckOnlineDispatch(Premises p)
    {
        var receiveOrQueue = p.Method(
            "TheBazaar.NetMessageProcessor",
            "ReceiveOrQueue",
            "BazaarGameShared.Infra.Messages.INetMessage"
        );
        p.Signature(
            "online dispatch entry point is the public ReceiveOrQueue",
            "TheBazaar.NetMessageProcessor.ReceiveOrQueue(INetMessage)",
            receiveOrQueue,
            member =>
                member is MethodInfo { IsPublic: true, IsStatic: false } method
                && method.ReturnType == typeof(void)
        );
        p.Absent(
            "online has no PTR private Receive funnel (NetMessageDispatchSeam shape key)",
            "TheBazaar.NetMessageProcessor.Receive(INetMessage, Boolean)",
            p.Method(
                "TheBazaar.NetMessageProcessor",
                "Receive",
                "BazaarGameShared.Infra.Messages.INetMessage",
                "System.Boolean"
            )
        );
        p.Calls(
            "online aggregate children recurse through the patched ReceiveOrQueue",
            receiveOrQueue,
            "TheBazaar.NetMessageProcessor::ReceiveOrQueue/1"
        );
        p.Signature(
            "NetMessageDispatchSeam targets ReceiveOrQueue",
            "NetMessageDispatchSeam.ResolveTarget()",
            NetMessageDispatchSeam.ResolveTarget(),
            member => receiveOrQueue != null && member.Equals(receiveOrQueue)
        );
        p.Absent(
            "ServerOption is PTR-only (GameBuildInfoResolver probe)",
            "TheBazaar.Config+ServerOption",
            p.Type("TheBazaar.Config+ServerOption")
        );
    }

    private static void CheckPtrDispatch(Premises p)
    {
        var receiveOrQueue = p.Method(
            "TheBazaar.NetMessageProcessor",
            "ReceiveOrQueue",
            "BazaarGameShared.Infra.Messages.INetMessage"
        );
        var receive = p.Method(
            "TheBazaar.NetMessageProcessor",
            "Receive",
            "BazaarGameShared.Infra.Messages.INetMessage",
            "System.Boolean"
        );
        p.Signature(
            "PTR keeps the public one-arg ReceiveOrQueue (seam fallback target)",
            "TheBazaar.NetMessageProcessor.ReceiveOrQueue(INetMessage)",
            receiveOrQueue,
            member =>
                member is MethodInfo { IsPublic: true, IsStatic: false } method
                && method.ReturnType == typeof(void)
        );
        p.Signature(
            "PTR private Receive funnel (NetMessageDispatchSeam primary target)",
            "TheBazaar.NetMessageProcessor.Receive(INetMessage, Boolean)",
            receive,
            member =>
                member is MethodInfo { IsPrivate: true, IsStatic: false } method
                && method.ReturnType == typeof(void)
        );
        p.Calls(
            "PTR aggregate children recurse through the patched private Receive",
            receive,
            "TheBazaar.NetMessageProcessor::Receive/2"
        );
        p.Signature(
            "NetMessageDispatchSeam targets the private Receive",
            "NetMessageDispatchSeam.ResolveTarget()",
            NetMessageDispatchSeam.ResolveTarget(),
            member => receive != null && member.Equals(receive)
        );
        p.Signature(
            "PTR Config declares ServerOption (GameBuildInfoResolver probe)",
            "TheBazaar.Config+ServerOption",
            p.Type("TheBazaar.Config+ServerOption"),
            _ => true
        );
    }

    private static void CheckTooltipGates(Premises p)
    {
        const string parent = "TheBazaar.UI.Tooltips.TooltipParentComponent";
        const string card = "TheBazaar.UI.Tooltips.CardTooltipController";
        const string auxiliary = "TheBazaar.UI.Tooltips.AuxiliaryTooltipController";
        const string canvasHider = "TheBazaar.SequenceFramework.CanvasHiderComponent";

        var showCard = p.PublicMethod(parent, "ShowCardTooltipController", typeof(void));
        var showSecondary = p.PublicMethod(
            parent,
            "ShowSecondaryCardTooltipController",
            typeof(Task)
        );
        var showAuxiliary = p.PublicMethod(parent, "ShowAuxiliaryTooltipController", typeof(void));
        p.Signature(
            "native primary tooltip high-level gate",
            parent + ".ShowCardTooltipController",
            showCard
        );
        p.Signature(
            "native secondary tooltip high-level gate returns Task",
            parent + ".ShowSecondaryCardTooltipController",
            showSecondary
        );
        p.Signature(
            "native auxiliary tooltip high-level gate",
            parent + ".ShowAuxiliaryTooltipController",
            showAuxiliary
        );
        p.Calls(
            "primary high-level show reaches the low-level late-show seam",
            showCard,
            card + "::ShowTooltipController"
        );
        p.Calls(
            "secondary high-level show reaches the low-level late-show seam",
            showSecondary,
            card + "::ShowTooltipController"
        );
        p.Calls(
            "auxiliary high-level show reaches the low-level late-show seam",
            showAuxiliary,
            auxiliary + "::ShowAuxiliaryTooltipController"
        );
        p.Signature(
            "native card tooltip low-level late-show gate",
            card + ".ShowTooltipController",
            p.PublicMethod(card, "ShowTooltipController", typeof(void))
        );
        p.Signature(
            "authoritative card-tooltip concealment seam",
            card + ".CanvasHiderComponent",
            p.Type(card)?.GetProperty("CanvasHiderComponent", Declared),
            member =>
                member is PropertyInfo property
                && property.GetMethod?.IsPublic == true
                && property.PropertyType.FullName == canvasHider
        );
        p.Signature(
            "complete auxiliary-tooltip visual gate",
            auxiliary + ".auxParent",
            p.Type(auxiliary)?.GetField("auxParent", Declared),
            member =>
                member is FieldInfo { IsPrivate: true } field
                && field.FieldType.FullName == "UnityEngine.GameObject"
        );
        p.Signature(
            "native auxiliary low-level late-show gate",
            auxiliary + ".ShowAuxiliaryTooltipController",
            p.PublicMethod(auxiliary, "ShowAuxiliaryTooltipController", typeof(void))
        );
        p.Signature(
            "card conceal command",
            canvasHider + ".SetVisibility(Boolean)",
            p.Method(canvasHider, "SetVisibility", "System.Boolean"),
            member =>
                member is MethodInfo { IsPublic: true } method && method.ReturnType == typeof(void)
        );
        p.Signature(
            "card visibility audit",
            canvasHider + ".IsVisible()",
            p.Method(canvasHider, "IsVisible"),
            member =>
                member is MethodInfo { IsPublic: true } method && method.ReturnType == typeof(bool)
        );
    }

    private static void CheckCardPreview(Premises p)
    {
        const string preview = "TheBazaar.UI.CardPreviewBase";
        p.Signature(
            "native preview instance setup",
            preview + ".SetUp(TCardBase, Boolean, TCardInstance, CancellationToken)",
            p.Method(
                preview,
                "SetUp",
                "BazaarGameShared.Domain.Cards.TCardBase",
                "System.Boolean",
                "BazaarGameShared.Domain.Cards.TCardInstance",
                "System.Threading.CancellationToken"
            ),
            member =>
                member is MethodInfo { IsPublic: true } method && method.ReturnType == typeof(Task)
        );
        p.Signature(
            "native preview show toggle",
            preview + ".Show(Boolean)",
            p.Method(preview, "Show", "System.Boolean"),
            member =>
                member is MethodInfo { IsPublic: true, IsStatic: false } method
                && method.ReturnType == typeof(void)
        );
        p.Signature(
            "native preview resize is abstract",
            preview + ".Resize()",
            p.Method(preview, "Resize"),
            member => member is MethodInfo { IsPublic: true, IsAbstract: true }
        );
        p.Signature(
            "native preview hover",
            preview + ".OnHover()",
            p.Method(preview, "OnHover"),
            member => member is MethodInfo { IsPublic: true }
        );
        p.Signature(
            "native preview hover out",
            preview + ".OnHoverOut()",
            p.Method(preview, "OnHoverOut"),
            member => member is MethodInfo { IsPublic: true }
        );
        p.Signature(
            "native preview client card",
            preview + "._clientCard",
            p.Type(preview)?.GetField("_clientCard", Declared),
            member => member is FieldInfo { IsFamily: true } field && field.FieldType.Name == "Card"
        );
        p.Signature(
            "native preview tooltip data",
            preview + "._tooltipData",
            p.Type(preview)?.GetField("_tooltipData", Declared),
            member =>
                member is FieldInfo { IsPrivate: true } field
                && field.FieldType.Name == "CardTooltipData"
        );

        // The mod rebuilds the game's UI card itself, so the game's own construction must
        // still pick the per-size asset reference, instantiate it, parent it, resize, and
        // set it up. Private helper names drift between builds; the public entry does not.
        var instantiateUiCard = p.Method(
            "AssetLoader",
            "InstantiateUICardAsync",
            "BazaarGameShared.Domain.Cards.TCardInstance",
            "UnityEngine.Transform",
            "System.Threading.CancellationToken"
        );
        foreach (
            var callee in new[]
            {
                "AssetLoader::SmallCardUIAssetRef",
                "AssetLoader::MediumCardUIAssetRef",
                "AssetLoader::LargeCardUIAssetRef",
                "AssetLoader::SkillUIAssetRef",
                "AssetLoader::InstantiateAssetAsyncByReference",
                "UnityEngine.Transform::SetParent/2",
                preview + "::Resize/0",
                preview + "::SetUp/4",
            }
        )
        {
            p.Calls(
                "game UI card construction uses " + callee,
                instantiateUiCard,
                callee,
                followPrivateHelpers: true
            );
        }
    }

    private static void CheckSkinPortraits(Premises p)
    {
        const string skin = "TheBazaar.Assets.Scripts.ScriptableObjectsScripts.SkinAssetDataSO";
        p.Signature(
            "hero portrait sprite reference",
            skin + ".portraitTextureReference",
            p.Type(skin)?.GetField("portraitTextureReference", Declared),
            member =>
                member is FieldInfo { IsPublic: true } field
                && field.FieldType.FullName == "UnityEngine.AddressableAssets.AssetReferenceSprite"
        );
        p.Signature(
            "hero store portrait texture reference",
            skin + ".storePortraitTextureReference",
            p.Type(skin)?.GetField("storePortraitTextureReference", Declared),
            member =>
                member is FieldInfo { IsPublic: true } field
                && field.FieldType.FullName == "UnityEngine.AddressableAssets.AssetReferenceTexture"
        );
        var loadPortraitSprite = p.PublicMethod(skin, "LoadPortraitSpriteAsync", null);
        p.Calls(
            "portrait sprite load prefers a valid animated portrait",
            loadPortraitSprite,
            skin + "::animatedPortraitPrefabReference"
        );
        p.Calls(
            "portrait sprite load validates the runtime key",
            loadPortraitSprite,
            "UnityEngine.AddressableAssets.AssetReference::RuntimeKeyIsValid/0"
        );
        var loadCollectionList = p.PublicMethod(skin, "LoadCollectionListAssetAsync", null);
        p.Calls(
            "collection list art loads the store portrait texture",
            loadCollectionList,
            skin + "::storePortraitTextureReference"
        );
        p.Calls(
            "collection list art loads through LoadTexture",
            loadCollectionList,
            skin + "::LoadTexture/1"
        );
    }

    // NativeCardPrefabLoader resolves InstantiateAssetAsyncByReference by name alone, so a
    // second overload would make that lookup ambiguous. The two Load methods are filtered
    // through SupportsSignature by NativeGlobalAssetLoader.
    private static void CheckAssetLoaderOverloads(Premises p, SortedSet<string> supported)
    {
        var loader = p.Type("AssetLoader");
        var assetReference = Type.GetType(
            "UnityEngine.AddressableAssets.AssetReference, Unity.Addressables",
            throwOnError: true
        )!;
        var instantiate =
            loader
                ?.GetMethods(Declared)
                .Where(method => method.Name == "InstantiateAssetAsyncByReference")
                .ToArray()
            ?? [];
        p.Signature(
            "InstantiateAssetAsyncByReference has one overload (resolved by name)",
            "AssetLoader.InstantiateAssetAsyncByReference",
            instantiate.Length == 1 ? instantiate[0] : null
        );

        foreach (
            var (name, generic, firstArgument, intent) in new (
                string,
                bool,
                object,
                NativeAssetScopeIntent
            )[]
            {
                (
                    "InstantiateAssetAsyncByReference",
                    false,
                    RuntimeHelpers.GetUninitializedObject(assetReference),
                    NativeAssetScopeIntent.Current
                ),
                ("LoadAssetAsyncByAddress", true, "address", NativeAssetScopeIntent.Global),
                (
                    "LoadAssetAsyncByReference",
                    true,
                    RuntimeHelpers.GetUninitializedObject(assetReference),
                    NativeAssetScopeIntent.Global
                ),
            }
        )
        {
            var usable =
                loader
                    ?.GetMethods(Declared)
                    .Where(method =>
                        method.Name == name
                        && method.IsGenericMethodDefinition == generic
                        && NativeAssetLoaderInvocation.SupportsSignature(
                            method,
                            firstArgument.GetType()
                        )
                    )
                    .ToArray()
                ?? [];
            foreach (var method in usable)
                supported.Add(Premises.Describe(method));
            p.Signature(
                $"AssetLoader.{name} has an overload the mod invokes with {intent} scope",
                "AssetLoader." + name,
                usable.FirstOrDefault(method =>
                    BuildsMatchingArguments(method, firstArgument, intent)
                )
            );
        }
    }

    private static bool BuildsMatchingArguments(
        MethodInfo method,
        object firstArgument,
        NativeAssetScopeIntent intent
    )
    {
        if (
            !NativeAssetLoaderInvocation.TryBuildArguments(
                method,
                firstArgument,
                intent,
                out var arguments
            )
        )
            return false;
        var parameters = method.GetParameters();
        if (arguments.Length != parameters.Length || !ReferenceEquals(arguments[0], firstArgument))
            return false;
        if (parameters.Length == 1)
            return true;
        var second = parameters[1].ParameterType;
        if (second == typeof(bool))
            return Equals(arguments[1], false);
        var scope = Nullable.GetUnderlyingType(second);
        return intent == NativeAssetScopeIntent.Current
            ? arguments[1] == null
            : arguments[1]?.GetType() == scope && arguments[1]!.ToString() == "Global";
    }

    private static (string? SnapshotChannel, string BuildId) IdentifyBuild(string managedPath)
    {
        var normalized = managedPath.Replace('\\', '/').TrimEnd('/');
        var snapshot = SnapshotPath.Match(normalized);
        if (snapshot.Success)
            return (snapshot.Groups[1].Value, snapshot.Groups[2].Value);

        var directory = new DirectoryInfo(managedPath);
        for (var level = 0; level < 10 && directory.Parent != null; level++)
        {
            directory = directory.Parent;
            var manifest = Path.Combine(directory.FullName, "appmanifest_1617400.acf");
            if (!File.Exists(manifest))
                continue;
            using var reader = new StreamReader(File.OpenRead(manifest));
            while (reader.ReadLine() is { } line)
            {
                var match = BuildIdLine.Match(line.Trim());
                if (match.Success)
                    return (null, match.Groups[1].Value);
            }
            break;
        }

        return (null, "unknown");
    }

    private static void WriteReport(
        string managedPath,
        string channel,
        string buildId,
        IReadOnlyList<PremiseResult> premises,
        SortedSet<string> overloads
    )
    {
        using var buffer = new MemoryStream();
        using (
            var json = new Utf8JsonWriter(
                buffer,
                new JsonWriterOptions
                {
                    Indented = true,
                    NewLine = "\n",
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                }
            )
        )
        {
            json.WriteStartObject();
            json.WriteString("channel", channel);
            json.WriteString("buildid", buildId);
            json.WriteStartObject("sha256");
            foreach (var assembly in new[] { "Assembly-CSharp.dll", "TheBazaarRuntime.dll" })
                json.WriteString(assembly, Sha256(Path.Combine(managedPath, assembly)));
            json.WriteEndObject();
            json.WriteStartArray("premises");
            foreach (var premise in premises)
            {
                json.WriteStartObject();
                json.WriteString("premise", premise.Premise);
                json.WriteString("kind", premise.Kind);
                json.WriteString("target", premise.Target);
                json.WriteBoolean("resolved", premise.Resolved);
                json.WriteString("member", premise.Member);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteStartArray("assetLoaderOverloads");
            foreach (var overload in overloads)
                json.WriteStringValue(overload);
            json.WriteEndArray();
            json.WriteEndObject();
        }
        buffer.WriteByte((byte)'\n');

        var directory = Path.Combine(TestInputs.RepoRoot, "artifacts", "compatibility");
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, $"{channel}-{buildId}.json"), buffer.ToArray());
    }

    private static string Sha256(string path)
    {
        if (!File.Exists(path))
            return "missing";
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private sealed record PremiseResult(
        string Premise,
        string Kind,
        string Target,
        bool Resolved,
        string? Member
    );

    private sealed class Premises(Assembly runtime) : IDisposable
    {
        private readonly IlCallScanner _scanner = new(runtime);

        internal List<PremiseResult> Results { get; } = [];

        public void Dispose() => _scanner.Dispose();

        internal Type? Type(string fullName) => runtime.GetType(fullName, throwOnError: false);

        internal MethodInfo? Method(string type, string name, params string[] parameterTypes) =>
            Type(type)
                ?.GetMethods(Declared)
                .SingleOrDefault(method =>
                    method.Name == name
                    && method
                        .GetParameters()
                        .Select(parameter => parameter.ParameterType.FullName)
                        .SequenceEqual(parameterTypes)
                );

        // The overload matters less than its visibility and return type for these gates.
        internal MethodInfo? PublicMethod(string type, string name, System.Type? returnType) =>
            Type(type)
                ?.GetMethods(Declared)
                .Where(method =>
                    method.Name == name
                    && method.IsPublic
                    && (returnType == null || method.ReturnType == returnType)
                )
                .OrderBy(Describe, StringComparer.Ordinal)
                .FirstOrDefault();

        internal void Snapshot(string declared, string probed, string buildId) =>
            Results.Add(
                new PremiseResult(
                    "snapshot directory channel matches the ServerOption probe",
                    "signature",
                    $"game-libs/{declared}-{buildId}",
                    declared == probed,
                    probed
                )
            );

        internal void Signature(
            string premise,
            string target,
            MemberInfo? member,
            Func<MemberInfo, bool>? shape = null
        )
        {
            var resolved = member != null && (shape == null || shape(member));
            Results.Add(
                new PremiseResult(
                    premise,
                    "signature",
                    target,
                    resolved,
                    member == null ? null : Describe(member)
                )
            );
        }

        internal void Absent(string premise, string target, MemberInfo? member) =>
            Results.Add(
                new PremiseResult(
                    premise,
                    "signature",
                    target,
                    member == null,
                    member == null ? null : Describe(member)
                )
            );

        internal void Calls(
            string premise,
            MethodBase? caller,
            string callee,
            bool followPrivateHelpers = false
        )
        {
            var resolved =
                caller != null
                && _scanner
                    .References(caller, followPrivateHelpers)
                    .Any(reference =>
                        reference == callee
                        || reference.StartsWith(callee + "/", StringComparison.Ordinal)
                    );
            Results.Add(
                new PremiseResult(
                    premise,
                    "call",
                    callee,
                    resolved,
                    caller == null ? null : Describe(caller)
                )
            );
        }

        internal static string Describe(MemberInfo member) =>
            member switch
            {
                MethodInfo method =>
                    $"{Access(method)}{Modifiers(method)} {TypeName(method.ReturnType)} {TypeName(method.DeclaringType!)}.{method.Name}{GenericArity(method)}({string.Join(", ", method.GetParameters().Select(Parameter))})",
                FieldInfo field =>
                    $"{FieldAccess(field)}{(field.IsStatic ? " static" : "")} {TypeName(field.FieldType)} {TypeName(field.DeclaringType!)}.{field.Name}",
                PropertyInfo property =>
                    $"{(property.GetMethod == null ? "set-only" : Access(property.GetMethod))} {TypeName(property.PropertyType)} {TypeName(property.DeclaringType!)}.{property.Name}",
                System.Type type => TypeName(type),
                _ => member.ToString() ?? member.Name,
            };

        private static string Access(MethodBase method) =>
            method.IsPublic ? "public"
            : method.IsFamily ? "protected"
            : method.IsPrivate ? "private"
            : method.IsAssembly ? "internal"
            : method.IsFamilyOrAssembly ? "protected internal"
            : "private protected";

        private static string FieldAccess(FieldInfo field) =>
            field.IsPublic ? "public"
            : field.IsFamily ? "protected"
            : field.IsPrivate ? "private"
            : field.IsAssembly ? "internal"
            : field.IsFamilyOrAssembly ? "protected internal"
            : "private protected";

        private static string Modifiers(MethodInfo method) =>
            method.IsStatic ? " static"
            : method.IsAbstract ? " abstract"
            : method.IsVirtual && !method.IsFinal ? " virtual"
            : "";

        private static string GenericArity(MethodInfo method) =>
            method.IsGenericMethodDefinition
                ? "<" + string.Join(", ", method.GetGenericArguments().Select(TypeName)) + ">"
                : "";

        private static string Parameter(ParameterInfo parameter) =>
            $"{TypeName(parameter.ParameterType)} {parameter.Name}{(parameter.IsOptional ? " [optional]" : "")}";

        private static string TypeName(System.Type type)
        {
            if (type.IsByRef)
                return TypeName(type.GetElementType()!) + "&";
            if (type.IsArray)
                return TypeName(type.GetElementType()!) + "[]";
            if (type.IsGenericParameter)
                return type.Name;
            var name = type.Name;
            var tick = name.IndexOf('`');
            if (tick >= 0)
                name = name[..tick];
            var qualified =
                type.IsNested ? TypeName(type.DeclaringType!) + "+" + name
                : string.IsNullOrEmpty(type.Namespace) ? name
                : type.Namespace + "." + name;
            return type.IsGenericType && !type.IsNested
                ? qualified
                    + "<"
                    + string.Join(", ", type.GetGenericArguments().Select(TypeName))
                    + ">"
                : qualified;
        }
    }

    // Lists what a method body references: "Ns.Type::Method/arity" for call, callvirt,
    // newobj, and ldftn targets, "Ns.Type::field" for field operands. The scan follows the
    // compiler-generated code a C# method body compiles into (async state machines, local
    // functions, lambdas) and, when asked, the caller type's own private helpers.
    private sealed class IlCallScanner : IDisposable
    {
        private static readonly Dictionary<ushort, OperandType> Operands = BuildOperandTable();

        private readonly PEReader _pe;
        private readonly MetadataReader _metadata;

        internal IlCallScanner(Assembly assembly)
        {
            _pe = new PEReader(File.OpenRead(assembly.Location));
            _metadata = _pe.GetMetadataReader();
        }

        public void Dispose() => _pe.Dispose();

        internal SortedSet<string> References(MethodBase root, bool followPrivateHelpers)
        {
            var rootHandle = (MethodDefinitionHandle)
                MetadataTokens.EntityHandle(root.MetadataToken);
            var owner = Outermost(_metadata.GetMethodDefinition(rootHandle).GetDeclaringType());
            var references = new SortedSet<string>(StringComparer.Ordinal);
            var seen = new HashSet<MethodDefinitionHandle>();
            var pending = new Queue<MethodDefinitionHandle>();
            pending.Enqueue(rootHandle);
            while (pending.Count > 0)
            {
                var handle = pending.Dequeue();
                if (!seen.Add(handle))
                    continue;
                var definition = _metadata.GetMethodDefinition(handle);
                if (definition.RelativeVirtualAddress == 0)
                    continue;
                var il = _pe.GetMethodBody(definition.RelativeVirtualAddress).GetILReader();
                while (il.RemainingBytes > 0)
                {
                    int opcode = il.ReadByte();
                    if (opcode == 0xFE)
                        opcode = 0xFE00 | il.ReadByte();
                    switch (Operands[(ushort)opcode])
                    {
                        case OperandType.InlineMethod:
                        {
                            var (name, target) = MethodReference(
                                MetadataTokens.EntityHandle(il.ReadInt32())
                            );
                            references.Add(name);
                            if (target is { } next && Follows(next, owner, followPrivateHelpers))
                                pending.Enqueue(next);
                            break;
                        }
                        case OperandType.InlineField:
                        {
                            var (name, declaringType) = FieldReference(
                                MetadataTokens.EntityHandle(il.ReadInt32())
                            );
                            references.Add(name);
                            if (
                                declaringType is { } type
                                && StateMachineMoveNext(type) is { } moveNext
                            )
                                pending.Enqueue(moveNext);
                            break;
                        }
                        case OperandType.InlineSwitch:
                            il.Offset += il.ReadInt32() * 4;
                            break;
                        case OperandType.InlineNone:
                            break;
                        case OperandType.ShortInlineBrTarget
                        or OperandType.ShortInlineI
                        or OperandType.ShortInlineVar:
                            il.Offset += 1;
                            break;
                        case OperandType.InlineVar:
                            il.Offset += 2;
                            break;
                        case OperandType.InlineI8 or OperandType.InlineR:
                            il.Offset += 8;
                            break;
                        default:
                            il.Offset += 4;
                            break;
                    }
                }
            }
            return references;
        }

        private bool Follows(
            MethodDefinitionHandle handle,
            TypeDefinitionHandle owner,
            bool privateHelpers
        )
        {
            var definition = _metadata.GetMethodDefinition(handle);
            var type = definition.GetDeclaringType();
            if (
                _metadata.GetString(definition.Name).StartsWith('<')
                || _metadata.GetString(_metadata.GetTypeDefinition(type).Name).StartsWith('<')
            )
                return true;
            return privateHelpers
                && Outermost(type) == owner
                && (definition.Attributes & MethodAttributes.MemberAccessMask)
                    == MethodAttributes.Private;
        }

        private MethodDefinitionHandle? StateMachineMoveNext(TypeDefinitionHandle handle)
        {
            var type = _metadata.GetTypeDefinition(handle);
            if (!_metadata.GetString(type.Name).StartsWith('<'))
                return null;
            foreach (var method in type.GetMethods())
            {
                if (_metadata.GetString(_metadata.GetMethodDefinition(method).Name) == "MoveNext")
                    return method;
            }
            return null;
        }

        private (string Name, MethodDefinitionHandle? Definition) MethodReference(
            EntityHandle handle
        )
        {
            switch (handle.Kind)
            {
                case HandleKind.MethodDefinition:
                {
                    var definitionHandle = (MethodDefinitionHandle)handle;
                    var definition = _metadata.GetMethodDefinition(definitionHandle);
                    return (
                        $"{TypeName(definition.GetDeclaringType())}::{_metadata.GetString(definition.Name)}/{Arity(definition.Signature)}",
                        definitionHandle
                    );
                }
                case HandleKind.MemberReference:
                {
                    var reference = _metadata.GetMemberReference((MemberReferenceHandle)handle);
                    return (
                        $"{TypeName(reference.Parent)}::{_metadata.GetString(reference.Name)}/{Arity(reference.Signature)}",
                        null
                    );
                }
                case HandleKind.MethodSpecification:
                    return MethodReference(
                        _metadata.GetMethodSpecification((MethodSpecificationHandle)handle).Method
                    );
                default:
                    return ("?", null);
            }
        }

        private (string Name, TypeDefinitionHandle? DeclaringType) FieldReference(
            EntityHandle handle
        )
        {
            if (handle.Kind == HandleKind.FieldDefinition)
            {
                var field = _metadata.GetFieldDefinition((FieldDefinitionHandle)handle);
                var type = field.GetDeclaringType();
                return ($"{TypeName(type)}::{_metadata.GetString(field.Name)}", type);
            }
            if (handle.Kind == HandleKind.MemberReference)
            {
                var reference = _metadata.GetMemberReference((MemberReferenceHandle)handle);
                return (
                    $"{TypeName(reference.Parent)}::{_metadata.GetString(reference.Name)}",
                    GenericDefinition(reference.Parent)
                );
            }
            return ("?", null);
        }

        private TypeDefinitionHandle? GenericDefinition(EntityHandle handle)
        {
            if (handle.Kind == HandleKind.TypeDefinition)
                return (TypeDefinitionHandle)handle;
            if (handle.Kind != HandleKind.TypeSpecification)
                return null;
            var blob = _metadata.GetBlobReader(
                _metadata.GetTypeSpecification((TypeSpecificationHandle)handle).Signature
            );
            if (blob.ReadSignatureTypeCode() != SignatureTypeCode.GenericTypeInstance)
                return null;
            blob.ReadSignatureTypeCode();
            var generic = blob.ReadTypeHandle();
            return generic.Kind == HandleKind.TypeDefinition ? (TypeDefinitionHandle)generic : null;
        }

        private int Arity(BlobHandle signature)
        {
            var blob = _metadata.GetBlobReader(signature);
            var header = blob.ReadSignatureHeader();
            if (header.IsGeneric)
                blob.ReadCompressedInteger();
            return blob.ReadCompressedInteger();
        }

        private TypeDefinitionHandle Outermost(TypeDefinitionHandle handle)
        {
            while (true)
            {
                var declaring = _metadata.GetTypeDefinition(handle).GetDeclaringType();
                if (declaring.IsNil)
                    return handle;
                handle = declaring;
            }
        }

        private string TypeName(EntityHandle handle)
        {
            switch (handle.Kind)
            {
                case HandleKind.TypeDefinition:
                {
                    var type = _metadata.GetTypeDefinition((TypeDefinitionHandle)handle);
                    var declaring = type.GetDeclaringType();
                    return declaring.IsNil
                        ? Qualified(type.Namespace, type.Name)
                        : TypeName(declaring) + "+" + _metadata.GetString(type.Name);
                }
                case HandleKind.TypeReference:
                {
                    var type = _metadata.GetTypeReference((TypeReferenceHandle)handle);
                    return type.ResolutionScope.Kind == HandleKind.TypeReference
                        ? TypeName(type.ResolutionScope) + "+" + _metadata.GetString(type.Name)
                        : Qualified(type.Namespace, type.Name);
                }
                case HandleKind.TypeSpecification:
                {
                    var blob = _metadata.GetBlobReader(
                        _metadata.GetTypeSpecification((TypeSpecificationHandle)handle).Signature
                    );
                    if (blob.ReadSignatureTypeCode() != SignatureTypeCode.GenericTypeInstance)
                        return "?";
                    blob.ReadSignatureTypeCode();
                    var name = TypeName(blob.ReadTypeHandle());
                    var tick = name.LastIndexOf('`');
                    return tick >= 0 ? name[..tick] : name;
                }
                default:
                    return "?";
            }
        }

        private string Qualified(StringHandle ns, StringHandle name)
        {
            var space = _metadata.GetString(ns);
            var simple = _metadata.GetString(name);
            return space.Length == 0 ? simple : space + "." + simple;
        }

        private static Dictionary<ushort, OperandType> BuildOperandTable()
        {
            var table = new Dictionary<ushort, OperandType>();
            foreach (
                var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            )
            {
                var opcode = (OpCode)field.GetValue(null)!;
                table.TryAdd(unchecked((ushort)opcode.Value), opcode.OperandType);
            }
            return table;
        }
    }
}

// The game builds this repo can prove (game-libs/ snapshots, the installed client) take
// (T, AssetScope? scope = null); GamePremiseCompatibilityTests checks those against the real
// AssetLoader. These synthetic shapes have no snapshot on record, so they stay synthetic:
// the two legacy shapes guard a build reverting to them (the mod would silently stop
// loading native art), the unknown one guards a future second parameter the mod would
// otherwise fill with a wrong value.
public sealed class NativeAssetLoaderUnprovenShapeTests
{
    [Fact]
    public void Legacy_single_parameter_signature_passes_only_the_first_argument()
    {
        var supported = NativeAssetLoaderInvocation.TryBuildArguments(
            Shape(nameof(InstantiateLegacy)),
            new FakeAssetReference(),
            NativeAssetScopeIntent.Current,
            out var arguments
        );

        Assert.True(supported);
        Assert.Single(arguments);
    }

    [Fact]
    public void Legacy_report_success_flag_is_disabled()
    {
        var supported = NativeAssetLoaderInvocation.TryBuildArguments(
            Shape(nameof(LoadLegacy)),
            "address",
            NativeAssetScopeIntent.Global,
            out var arguments
        );

        Assert.True(supported);
        Assert.Equal(false, arguments[1]);
    }

    [Fact]
    public void Unknown_second_parameter_is_rejected_before_selection_and_invocation()
    {
        var method = Shape(nameof(InstantiateUnknown));

        Assert.False(
            NativeAssetLoaderInvocation.SupportsSignature(method, typeof(FakeAssetReference))
        );
        Assert.False(
            NativeAssetLoaderInvocation.TryBuildArguments(
                method,
                new FakeAssetReference(),
                NativeAssetScopeIntent.Current,
                out var arguments
            )
        );
        Assert.Empty(arguments);
    }

    private static MethodInfo Shape(string name) =>
        typeof(NativeAssetLoaderUnprovenShapeTests).GetMethod(
            name,
            BindingFlags.NonPublic | BindingFlags.Static
        ) ?? throw new InvalidOperationException($"Missing shape {name}.");

    private static Task<object> InstantiateLegacy(FakeAssetReference assetReference) =>
        Task.FromResult<object>(assetReference);

    private static Task<object> LoadLegacy(string address, bool reportSuccess = false) =>
        Task.FromResult<object>(address);

    private static Task<object> InstantiateUnknown(
        FakeAssetReference assetReference,
        string? unexpected = null
    ) => Task.FromResult<object>(assetReference);

    private sealed class FakeAssetReference;
}

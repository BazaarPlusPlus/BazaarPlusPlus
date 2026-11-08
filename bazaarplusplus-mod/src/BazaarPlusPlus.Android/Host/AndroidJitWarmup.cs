using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using BazaarPlusPlus.Android.CombatReplay;
using BazaarPlusPlus.Android.Game;
using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Android.Patches;
using BazaarPlusPlus.Core.Ui;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.CombatStatusBar;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Game.PvpBattles.Persistence;
using BazaarPlusPlus.Game.RunLogging;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Mobile.Hud;
using BazaarPlusPlus.Mobile.Ui;
using Il2CppBazaarGameShared.Infra.Messages;
using Il2CppBazaarGameShared.Infra.Messages.GameSimEvents;

namespace BazaarPlusPlus.Android.Host;

/// <summary>
/// Compiles the Mod's code on the paths that otherwise run for the first time in one frame, a
/// little each frame, before they are needed. The host runs on a JIT: the first PvP battle a
/// process captures paid about 20 ms of compilation inside the net-message hook, on top of the
/// reads, and the combat clock's first build about 12 ms. Each <see cref="Tick"/> prepares methods
/// from <see cref="Paths"/>, nested types included, until <see cref="FrameBudget"/> is spent.
/// The game's message types among them are interop wrappers: their static constructors, which
/// look up the IL2CPP class and its fields, are run here too. Main thread only: compiling can run
/// a type's static constructor, and these call into IL2CPP. Generic instantiations, such as the
/// collections over the game's enums, are not prepared; they still compile on first use.
/// </summary>
internal sealed class AndroidJitWarmup
{
    public static readonly TimeSpan FrameBudget = TimeSpan.FromMilliseconds(1);

    /// <summary>
    /// The types whose first use measured as the largest single frames: the PvP Battle Capture
    /// Window with the host's reads and persistence, and the combat clock's build and layout.
    /// </summary>
    public static IReadOnlyList<Type> Paths { get; } =
    [
        typeof(NetMessageReceivePatch),
        typeof(AndroidReplayGuard),
        typeof(PvpBattleCaptureWindow<INetMessage>),
        typeof(AndroidPvpBattleReads),
        typeof(AndroidGameIds),
        typeof(AndroidCardFacets),
        typeof(AndroidReplayMessages),
        typeof(AndroidClientCache),
        typeof(NetMessageGameSim),
        typeof(NetMessageCombatSim),
        typeof(GameSim),
        typeof(GameSimEventCardSpawned),
        typeof(GameSimEventPlayerSkillEquipped),
        typeof(SimPvpOpponent),
        typeof(SimUpdateCard),
        typeof(SimUpdateRun),
        typeof(CardDeltaPlacement),
        typeof(AttributeDelta),
        typeof(PvpBattleSignal),
        typeof(PvpReplayPart),
        typeof(PvpBattleCardSnapshot),
        typeof(PvpBattleManifest),
        typeof(PvpBattleSnapshots),
        typeof(PvpBattleParticipants),
        typeof(PvpBattleOutcome),
        typeof(PvpBattleCapture),
        typeof(PvpBattleCardSetCapture),
        typeof(PvpReplayPayload),
        typeof(ReplayPersistenceStateTracker),
        typeof(AndroidPvpBattleCapture),
        typeof(AndroidBattlePersistenceQueue),
        typeof(PvpBattleCatalog),
        typeof(RunLoggingModule),
        typeof(TouchCombatClock),
        typeof(CombatStatusBarState),
        typeof(HudVisibility),
        typeof(TouchHudLayout),
        typeof(TouchChrome),
        typeof(TouchRelayoutDebounce),
        typeof(TouchPlaybackIcons),
        typeof(NativeSkin),
        typeof(AndroidNativeArt),
        typeof(AndroidVaultBounds),
        typeof(TouchScreen),
        typeof(UiKit),
    ];

    private const BindingFlags Declared =
        BindingFlags.DeclaredOnly
        | BindingFlags.Instance
        | BindingFlags.Static
        | BindingFlags.Public
        | BindingFlags.NonPublic;

    private readonly Queue<Type> _types;
    private readonly Queue<MethodBase> _methods = new();
    private readonly Action<MethodBase> _prepare;
    private readonly Func<long> _timestamp;
    private readonly long _budget;
    private readonly Stopwatch _elapsed = new();
    private int _frames;

    public AndroidJitWarmup(IEnumerable<Type> paths)
        : this(paths, Prepare, Stopwatch.GetTimestamp, FrameBudget) { }

    internal AndroidJitWarmup(
        IEnumerable<Type> paths,
        Action<MethodBase> prepare,
        Func<long> timestamp,
        TimeSpan budget
    )
    {
        _types = new Queue<Type>(paths);
        _prepare = prepare;
        _timestamp = timestamp;
        _budget = (long)(budget.TotalSeconds * Stopwatch.Frequency);
    }

    public bool IsDone => _types.Count == 0 && _methods.Count == 0;

    public int Prepared { get; private set; }

    public int Failed { get; private set; }

    /// <summary>Every frame on the main thread; a no-op once every method is prepared.</summary>
    public void Tick()
    {
        if (IsDone)
            return;
        _frames++;
        _elapsed.Start();
        var deadline = _timestamp() + _budget;
        try
        {
            do
            {
                if (_methods.TryDequeue(out var method))
                    PrepareOne(method);
                else
                    Expand(_types.Dequeue());
            } while (!IsDone && _timestamp() < deadline);
        }
        finally
        {
            _elapsed.Stop();
        }
        if (IsDone)
            BppLog.InfoEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.jit_warmup.completed"),
                ("methods", Prepared),
                ("failed", Failed),
                ("frames", _frames),
                ("elapsed_ms", _elapsed.ElapsedMilliseconds)
            );
    }

    private void PrepareOne(MethodBase method)
    {
        try
        {
            _prepare(method);
            Prepared++;
        }
        catch (Exception)
        {
            // A method the runtime will not compile ahead is compiled on first use, as before.
            Failed++;
        }
    }

    private void Expand(Type type)
    {
        if (type.ContainsGenericParameters)
            return;
        if (IsInteropWrapper(type))
            Initialize(type);
        foreach (var method in type.GetMethods(Declared))
            if (CanPrepare(method))
                _methods.Enqueue(method);
        foreach (var constructor in type.GetConstructors(Declared))
            if (CanPrepare(constructor))
                _methods.Enqueue(constructor);
        foreach (var nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            if (Close(nested, type) is { } closed)
                _types.Enqueue(closed);
    }

    // A type nested in a constructed generic type is declared over the outer type's parameters.
    private static Type? Close(Type nested, Type outer)
    {
        if (!nested.IsGenericTypeDefinition)
            return nested;
        var arguments = outer.IsConstructedGenericType ? outer.GetGenericArguments() : [];
        return nested.GetGenericArguments().Length == arguments.Length
            ? nested.MakeGenericType(arguments)
            : null;
    }

    private static bool IsInteropWrapper(Type type) =>
        type.Assembly.GetName().Name?.StartsWith("Il2Cpp", StringComparison.Ordinal) == true;

    private void Initialize(Type type)
    {
        try
        {
            RuntimeHelpers.RunClassConstructor(type.TypeHandle);
        }
        catch (Exception)
        {
            // The wrapper initializes on first use instead, as before.
            Failed++;
        }
    }

    private static bool CanPrepare(MethodBase method) =>
        !method.IsAbstract
        && !method.ContainsGenericParameters
        && (method.MethodImplementationFlags & MethodImplAttributes.Runtime) == 0
        && (method.Attributes & MethodAttributes.PinvokeImpl) == 0;

    private static void Prepare(MethodBase method)
    {
        var declaring = method.DeclaringType;
        if (declaring is { IsConstructedGenericType: true })
            RuntimeHelpers.PrepareMethod(
                method.MethodHandle,
                declaring.GetGenericArguments().Select(type => type.TypeHandle).ToArray()
            );
        else
            RuntimeHelpers.PrepareMethod(method.MethodHandle);
    }
}

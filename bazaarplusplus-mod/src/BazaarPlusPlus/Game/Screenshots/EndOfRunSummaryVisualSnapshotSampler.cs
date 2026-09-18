#nullable enable
using System.Collections;
using System.Reflection;
using TheBazaar.UI.EndOfRun;
using UnityEngine;

namespace BazaarPlusPlus.Game.Screenshots;

internal readonly record struct EndOfRunSummaryVisualSnapshot(
    int LoadedCardCount,
    int TransformCount,
    ulong CardSetFingerprint,
    ulong PoseFingerprint
);

internal sealed class EndOfRunSummaryVisualSnapshotSampler
{
    private const string LoadedCardsFieldName = "loadedCards";
    private const string AnimatorMemberName = "Animator";
    private const ulong HashOffset = 14695981039346656037UL;
    private const ulong HashPrime = 1099511628211UL;
    private static readonly FieldInfo? LoadedCardsField = FindField(
        typeof(EndOfRunSummaryController),
        LoadedCardsFieldName
    );
    private static readonly object AnimatorAccessorGate = new();
    private static readonly Dictionary<Type, AnimatorAccessor?> AnimatorAccessors = new();

    private readonly EndOfRunReusablePlanCache<int, CardSamplingPlan> _plans = new();
    private readonly HashSet<int> _activePlanKeys = new();
    private object? _loadedCardsIdentity;
    private int _loadedCardsGeneration;

    public bool TryCapture(
        EndOfRunSummaryController? summaryController,
        out EndOfRunSummaryVisualSnapshot snapshot
    )
    {
        var observation = CaptureCleanFrameVisual(summaryController, out snapshot);
        return observation.State == EndOfRunCleanFrameVisualState.Sampled;
    }

    internal EndOfRunCleanFrameVisualObservation CaptureCleanFrameVisual(
        EndOfRunSummaryController? summaryController
    )
    {
        var observation = CaptureCleanFrameVisual(summaryController, out var snapshot);
        return observation.State == EndOfRunCleanFrameVisualState.Sampled
            ? EndOfRunCleanFrameVisualObservation.Sampled(
                snapshot.LoadedCardCount,
                snapshot.TransformCount,
                snapshot.CardSetFingerprint,
                snapshot.PoseFingerprint
            )
            : observation;
    }

    internal void Reset()
    {
        _plans.Clear();
        _activePlanKeys.Clear();
        _loadedCardsIdentity = null;
        _loadedCardsGeneration++;
    }

    private EndOfRunCleanFrameVisualObservation CaptureCleanFrameVisual(
        EndOfRunSummaryController? summaryController,
        out EndOfRunSummaryVisualSnapshot snapshot
    )
    {
        snapshot = default;
        if (summaryController == null || LoadedCardsField == null)
            return EndOfRunCleanFrameVisualObservation.Unavailable;

        object? loadedCardsValue;
        try
        {
            loadedCardsValue = LoadedCardsField.GetValue(summaryController);
        }
        catch
        {
            return EndOfRunCleanFrameVisualObservation.Unavailable;
        }
        if (loadedCardsValue is not IEnumerable loadedCards)
            return EndOfRunCleanFrameVisualObservation.Unavailable;

        if (!ReferenceEquals(_loadedCardsIdentity, loadedCardsValue))
        {
            _loadedCardsIdentity = loadedCardsValue;
            _loadedCardsGeneration++;
        }

        _activePlanKeys.Clear();
        var loadedCardCount = 0;
        var transformCount = 0;
        var cardSetFingerprint = HashOffset;
        var poseFingerprint = HashOffset;
        try
        {
            foreach (var loadedCard in loadedCards)
            {
                if (loadedCard == null)
                    continue;
                if (loadedCard is UnityEngine.Object unityCard && unityCard == null)
                    return UnavailableAfterPrune();

                var accessor = GetAnimatorAccessor(loadedCard.GetType());
                if (accessor == null || accessor.GetValue(loadedCard) is not Animator animator)
                    return UnavailableAfterPrune();
                if (animator == null || animator.transform is not { } root || root == null)
                    return UnavailableAfterPrune();

                var planKey = animator.GetInstanceID();
                _activePlanKeys.Add(planKey);
                if (
                    !_plans.TryGet(planKey, _loadedCardsGeneration, out var plan)
                    || !plan.IsReusable(animator, root)
                )
                {
                    plan = CardSamplingPlan.Create(animator, root);
                    _plans.Set(planKey, _loadedCardsGeneration, plan);
                }

                loadedCardCount++;
                AddInt(ref cardSetFingerprint, plan.AnimatorInstanceId);
                foreach (var node in plan.Nodes)
                {
                    AddInt(ref cardSetFingerprint, node.InstanceId);
                    node.AddPose(ref poseFingerprint);
                    transformCount++;
                }
            }
        }
        catch
        {
            return UnavailableAfterPrune();
        }

        _plans.Prune(_loadedCardsGeneration, _activePlanKeys, static plan => plan.IsAlive);
        if (loadedCardCount == 0)
            return EndOfRunCleanFrameVisualObservation.Empty;

        AddInt(ref cardSetFingerprint, loadedCardCount);
        snapshot = new EndOfRunSummaryVisualSnapshot(
            loadedCardCount,
            transformCount,
            cardSetFingerprint,
            poseFingerprint
        );
        return EndOfRunCleanFrameVisualObservation.Sampled(
            loadedCardCount,
            transformCount,
            cardSetFingerprint,
            poseFingerprint
        );

        EndOfRunCleanFrameVisualObservation UnavailableAfterPrune()
        {
            _plans.Prune(_loadedCardsGeneration, _activePlanKeys, static plan => plan.IsAlive);
            return EndOfRunCleanFrameVisualObservation.Unavailable;
        }
    }

    private static AnimatorAccessor? GetAnimatorAccessor(Type type)
    {
        lock (AnimatorAccessorGate)
        {
            if (AnimatorAccessors.TryGetValue(type, out var cached))
                return cached;

            var property = type.GetProperty(
                AnimatorMemberName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );
            AnimatorAccessor? accessor = property == null ? null : new AnimatorAccessor(property);
            if (accessor == null)
            {
                var field = type.GetField(
                    AnimatorMemberName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                );
                if (field != null)
                    accessor = new AnimatorAccessor(field);
            }
            AnimatorAccessors[type] = accessor;
            return accessor;
        }
    }

    private static FieldInfo? FindField(Type type, string fieldName)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            var field = current.GetField(
                fieldName,
                BindingFlags.Instance
                    | BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.DeclaredOnly
            );
            if (field != null)
                return field;
        }
        return null;
    }

    private static bool IsNonStructuralVisualSubtree(Transform transform)
    {
        var name = transform.name;
        return transform.GetComponent<ParticleSystem>() != null
            || name.IndexOf("vfx", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("particle", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void AddVector2(ref ulong hash, Vector2 value)
    {
        AddFloat(ref hash, value.x);
        AddFloat(ref hash, value.y);
    }

    private static void AddVector3(ref ulong hash, Vector3 value)
    {
        AddFloat(ref hash, value.x);
        AddFloat(ref hash, value.y);
        AddFloat(ref hash, value.z);
    }

    private static void AddQuaternion(ref ulong hash, Quaternion value)
    {
        AddFloat(ref hash, value.x);
        AddFloat(ref hash, value.y);
        AddFloat(ref hash, value.z);
        AddFloat(ref hash, value.w);
    }

    private static void AddFloat(ref ulong hash, float value) =>
        AddInt(ref hash, value.GetHashCode());

    private static void AddInt(ref ulong hash, int value)
    {
        hash ^= unchecked((uint)value);
        hash *= HashPrime;
    }

    private sealed class AnimatorAccessor
    {
        private readonly PropertyInfo? _property;
        private readonly FieldInfo? _field;

        internal AnimatorAccessor(PropertyInfo property) => _property = property;

        internal AnimatorAccessor(FieldInfo field) => _field = field;

        internal object? GetValue(object instance) =>
            _property != null ? _property.GetValue(instance) : _field?.GetValue(instance);
    }

    private sealed class CardSamplingPlan
    {
        private readonly Animator _animator;
        private readonly Transform _root;
        private readonly ExcludedTopologySentinel[] _excludedTopologySentinels;

        private CardSamplingPlan(
            Animator animator,
            Transform root,
            TransformSamplingNode[] nodes,
            ExcludedTopologySentinel[] excludedTopologySentinels
        )
        {
            _animator = animator;
            _root = root;
            Nodes = nodes;
            _excludedTopologySentinels = excludedTopologySentinels;
        }

        internal int AnimatorInstanceId => _animator.GetInstanceID();

        internal bool IsAlive
        {
            get
            {
                if (_animator == null || _root == null)
                    return false;
                foreach (var node in Nodes)
                {
                    if (!node.IsAlive)
                        return false;
                }
                foreach (var sentinel in _excludedTopologySentinels)
                {
                    if (!sentinel.IsAlive)
                        return false;
                }
                return true;
            }
        }

        internal TransformSamplingNode[] Nodes { get; }

        internal static CardSamplingPlan Create(Animator animator, Transform root)
        {
            var nodes = new List<TransformSamplingNode>();
            var excludedTopologySentinels = new List<ExcludedTopologySentinel>();
            AddHierarchy(root, nodes, excludedTopologySentinels);
            return new CardSamplingPlan(
                animator,
                root,
                nodes.ToArray(),
                excludedTopologySentinels.ToArray()
            );
        }

        internal bool IsReusable(Animator animator, Transform root)
        {
            if (!ReferenceEquals(_animator, animator) || !ReferenceEquals(_root, root))
                return false;
            foreach (var node in Nodes)
            {
                if (!node.HasSameHierarchyGeneration)
                    return false;
            }
            foreach (var sentinel in _excludedTopologySentinels)
            {
                if (!sentinel.HasSameHierarchyGeneration)
                    return false;
            }
            return true;
        }

        private static void AddHierarchy(
            Transform transform,
            ICollection<TransformSamplingNode> nodes,
            ICollection<ExcludedTopologySentinel> excludedTopologySentinels
        )
        {
            nodes.Add(new TransformSamplingNode(transform));
            for (var index = 0; index < transform.childCount; index++)
            {
                var child = transform.GetChild(index);
                if (child == null)
                    continue;
                if (IsNonStructuralVisualSubtree(child))
                {
                    excludedTopologySentinels.Add(new ExcludedTopologySentinel(child));
                    continue;
                }
                AddHierarchy(child, nodes, excludedTopologySentinels);
            }
        }
    }

    private sealed class ExcludedTopologySentinel
    {
        private readonly Transform _transform;
        private readonly EndOfRunHierarchySentinelState _expected;

        internal ExcludedTopologySentinel(Transform transform)
        {
            _transform = transform;
            _expected = CaptureState(transform, isExcluded: true);
        }

        internal bool IsAlive => _transform != null;

        internal bool HasSameHierarchyGeneration =>
            _transform != null
            && EndOfRunHierarchySentinelCore.Matches(
                _expected,
                CaptureState(_transform, IsNonStructuralVisualSubtree(_transform))
            );

        private static EndOfRunHierarchySentinelState CaptureState(
            Transform transform,
            bool isExcluded
        ) =>
            new(
                transform.GetInstanceID(),
                transform.parent == null ? 0 : transform.parent.GetInstanceID(),
                transform.GetSiblingIndex(),
                transform.childCount,
                isExcluded
            );
    }

    private sealed class TransformSamplingNode
    {
        private readonly Transform _transform;
        private readonly RectTransform? _rectTransform;
        private readonly int _childCount;
        private readonly int _parentInstanceId;
        private readonly int _siblingIndex;

        internal TransformSamplingNode(Transform transform)
        {
            _transform = transform;
            _rectTransform = transform as RectTransform;
            _childCount = transform.childCount;
            _parentInstanceId = transform.parent == null ? 0 : transform.parent.GetInstanceID();
            _siblingIndex = transform.GetSiblingIndex();
            InstanceId = transform.GetInstanceID();
        }

        internal int InstanceId { get; }

        internal bool IsAlive => _transform != null;

        internal bool HasSameHierarchyGeneration =>
            _transform != null
            && _transform.childCount == _childCount
            && (_transform.parent == null ? 0 : _transform.parent.GetInstanceID())
                == _parentInstanceId
            && _transform.GetSiblingIndex() == _siblingIndex;

        internal void AddPose(ref ulong poseFingerprint)
        {
            AddInt(ref poseFingerprint, _transform.gameObject.activeSelf ? 1 : 0);
            AddVector3(ref poseFingerprint, _transform.localPosition);
            AddQuaternion(ref poseFingerprint, _transform.localRotation);
            AddVector3(ref poseFingerprint, _transform.localScale);
            if (_rectTransform == null)
                return;
            AddVector2(ref poseFingerprint, _rectTransform.anchorMin);
            AddVector2(ref poseFingerprint, _rectTransform.anchorMax);
            AddVector2(ref poseFingerprint, _rectTransform.anchoredPosition);
            AddVector2(ref poseFingerprint, _rectTransform.sizeDelta);
            AddVector2(ref poseFingerprint, _rectTransform.pivot);
        }
    }
}

#nullable enable
using System;
using System.Collections;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BazaarPlusPlus;
using BazaarPlusPlus.Game.MonsterPreview;
using BazaarPlusPlus.Game.PreviewSurface;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed class HistoryPanelPreviewRenderer
{
    // Temporary manual tuning entrypoint for HistoryPanel preview.
    // Change these defaults directly, rebuild, then verify in game.
    private const int PreviewLayer = 30;
    private const int TextureWidth = 2688;
    private const int TextureHeight = 640;
    private const int PreviewSettleFrames = 6;
    private const float BackdropHeight = 0.02f;
    private const float BackdropPaddingX = 0.45f;
    private const float BackdropPaddingZ = 2.4f;
    private const float BackdropDepthOffset = 0.08f;
    private const float DefaultBoardHorizontalOffset = 0.5f;
    private const float DefaultBoardDepth = 6f;
    private const float DefaultBoardVerticalOffset = -0.10f;
    private const float DefaultCameraDepth = 9.95f;
    private const float DefaultCameraVerticalCenter = 0.08f;
    private const float DefaultCameraFieldOfView = 54f;
    private const float DefaultCardWidthScale = 0.92f;
    private const float DefaultCardHeightScale = 0.96f;
    private const float DefaultCardSpacingX = 0.92f;

    private GameObject? _rootObject;
    private Camera? _camera;
    private Light? _keyLight;
    private Light? _fillLight;
    private RenderTexture? _texture;
    private GameObject? _backdropPlate;
    private PreviewBoardSurface? _playerBoardSurface;
    private PreviewBoardSurface? _opponentBoardSurface;
    private PreviewBoardRenderTarget? _playerBoardRenderTarget;
    private PreviewBoardRenderTarget? _opponentBoardRenderTarget;
    private string? _renderedBattleId;
    private int _generation;
    private float _boardHorizontalOffset = DefaultBoardHorizontalOffset;
    private float _boardDepth = DefaultBoardDepth;
    private float _boardVerticalOffset = DefaultBoardVerticalOffset;
    private float _cameraDepth = DefaultCameraDepth;
    private float _cameraVerticalCenter = DefaultCameraVerticalCenter;
    private float _cameraFieldOfView = DefaultCameraFieldOfView;
    private float _cardWidthScale = DefaultCardWidthScale;
    private float _cardHeightScale = DefaultCardHeightScale;
    private float _cardSpacingX = DefaultCardSpacingX;

    public Texture? CurrentTexture => _texture;

    public void CancelPending()
    {
        _generation++;
    }

    public void Invalidate()
    {
        CancelPending();
        _renderedBattleId = null;
    }

    public bool NudgeBoardHorizontalOffset(float delta)
    {
        _boardHorizontalOffset = Mathf.Max(0.5f, _boardHorizontalOffset + delta);
        return ReportTuning();
    }

    public bool NudgeCameraDepth(float delta)
    {
        _cameraDepth = Mathf.Clamp(_cameraDepth + delta, _boardDepth + 1.5f, 40f);
        return ReportTuning();
    }

    public bool NudgeCameraVerticalCenter(float delta)
    {
        _cameraVerticalCenter = Mathf.Clamp(_cameraVerticalCenter + delta, -10f, 10f);
        return ReportTuning();
    }

    public bool NudgeFieldOfView(float delta)
    {
        _cameraFieldOfView = Mathf.Clamp(_cameraFieldOfView + delta, 20f, 110f);
        return ReportTuning();
    }

    public bool NudgeCardWidthScale(float delta)
    {
        _cardWidthScale = Mathf.Clamp(_cardWidthScale + delta, 0.2f, 4f);
        return ReportTuning();
    }

    public bool NudgeCardHeightScale(float delta)
    {
        _cardHeightScale = Mathf.Clamp(_cardHeightScale + delta, 0.2f, 4f);
        return ReportTuning();
    }

    public bool NudgeCardSpacingX(float delta)
    {
        _cardSpacingX = Mathf.Clamp(_cardSpacingX + delta, 0.35f, 3f);
        return ReportTuning();
    }

    public bool ResetDebugTuning()
    {
        _boardHorizontalOffset = DefaultBoardHorizontalOffset;
        _boardDepth = DefaultBoardDepth;
        _boardVerticalOffset = DefaultBoardVerticalOffset;
        _cameraDepth = DefaultCameraDepth;
        _cameraVerticalCenter = DefaultCameraVerticalCenter;
        _cameraFieldOfView = DefaultCameraFieldOfView;
        _cardWidthScale = DefaultCardWidthScale;
        _cardHeightScale = DefaultCardHeightScale;
        _cardSpacingX = DefaultCardSpacingX;
        return ReportTuning();
    }

    public string GetDebugSummary()
    {
        return $"boardSpacing={_boardHorizontalOffset:0.00}, cardSpacingX={_cardSpacingX:0.00}, camDepth={_cameraDepth:0.00}, camZ={_cameraVerticalCenter:0.00}, fov={_cameraFieldOfView:0.0}, cardW={_cardWidthScale:0.00}, cardH={_cardHeightScale:0.00}";
    }

    public void RenderLiveFrame()
    {
        if (_camera == null || _texture == null || _rootObject == null || !_rootObject.activeSelf)
            return;

        if (string.IsNullOrWhiteSpace(_renderedBattleId))
            return;

        if (!_texture.IsCreated())
            _texture.Create();

        if (_camera.targetTexture != _texture)
            _camera.targetTexture = _texture;

        ApplyLayerRecursively(_playerBoardSurface?.RootTransform, PreviewLayer);
        ApplyLayerRecursively(_opponentBoardSurface?.RootTransform, PreviewLayer);
        _camera.Render();
    }

    public void Hide()
    {
        CancelPending();
        _renderedBattleId = null;

        if (_playerBoardRenderTarget != null)
            _playerBoardRenderTarget.SetVisible(false);

        if (_opponentBoardRenderTarget != null)
            _opponentBoardRenderTarget.SetVisible(false);

        if (_rootObject != null)
            _rootObject.SetActive(false);
    }

    public void Dispose()
    {
        CancelPending();

        _playerBoardRenderTarget?.Dispose();
        _playerBoardRenderTarget = null;
        _playerBoardSurface = null;

        _opponentBoardRenderTarget?.Dispose();
        _opponentBoardRenderTarget = null;
        _opponentBoardSurface = null;

        if (_texture != null)
        {
            _texture.Release();
            Object.Destroy(_texture);
            _texture = null;
        }

        if (_camera != null)
            Object.Destroy(_camera.gameObject);

        _camera = null;
        _keyLight = null;
        _fillLight = null;

        if (_rootObject != null)
            Object.Destroy(_rootObject);

        _rootObject = null;
        _renderedBattleId = null;
    }

    public IEnumerator RenderPreview(
        string? renderId,
        HistoryBattlePreviewData? previewData,
        Action<string?, bool> setStatus,
        Action? onRendered = null
    )
    {
        CancelPending();
        var generation = _generation;

        if (string.IsNullOrWhiteSpace(renderId) || previewData == null)
        {
            setStatus(HistoryPanelText.PreviewSelectRunOrBattle(), true);
            Hide();
            onRendered?.Invoke();
            yield break;
        }

        if (!previewData.HasRenderableCards)
        {
            setStatus(HistoryPanelText.NoLocallyRenderableCards(), true);
            Hide();
            onRendered?.Invoke();
            yield break;
        }

        EnsureInitialized();
        EnsureRenderTexture();
        if (
            _camera == null
            || _texture == null
            || _playerBoardSurface == null
            || _opponentBoardSurface == null
            || _playerBoardRenderTarget == null
            || _opponentBoardRenderTarget == null
        )
        {
            setStatus(HistoryPanelText.PreviewRendererInitFailed(), true);
            yield break;
        }

        if (_renderedBattleId == renderId)
        {
            setStatus(null, false);
            onRendered?.Invoke();
            yield break;
        }

        setStatus(HistoryPanelText.LoadingPreview(), true);

        _rootObject!.SetActive(true);
        var layout = ConfigureBoards(previewData);

        var playerTask = layout.ShowPlayerBoard
            ? QueueBoardRenderAsync(
                _playerBoardRenderTarget,
                previewData.PlayerBoard,
                CreatePresentation(),
                layout.PlayerX
            )
            : Task.CompletedTask;
        var opponentTask = layout.ShowOpponentBoard
            ? QueueBoardRenderAsync(
                _opponentBoardRenderTarget,
                previewData.OpponentBoard,
                CreatePresentation(),
                layout.OpponentX
            )
            : Task.CompletedTask;

        while ((!playerTask.IsCompleted || !opponentTask.IsCompleted) && generation == _generation)
            yield return null;

        if (generation != _generation)
        {
            _playerBoardRenderTarget.SetVisible(false);
            _opponentBoardRenderTarget.SetVisible(false);
            yield break;
        }

        if (playerTask.IsFaulted || opponentTask.IsFaulted)
        {
            setStatus(HistoryPanelText.PreviewBuildFailed(), true);
            Hide();
            onRendered?.Invoke();
            yield break;
        }

        ApplyLayerRecursively(_playerBoardSurface.RootTransform, PreviewLayer);
        ApplyLayerRecursively(_opponentBoardSurface.RootTransform, PreviewLayer);

        for (var frame = 0; frame < PreviewSettleFrames && generation == _generation; frame++)
            yield return null;

        if (generation != _generation)
            yield break;

        yield return new WaitForEndOfFrame();
        yield return new WaitForEndOfFrame();

        if (generation != _generation)
            yield break;

        ApplyLayerRecursively(_playerBoardSurface.RootTransform, PreviewLayer);
        ApplyLayerRecursively(_opponentBoardSurface.RootTransform, PreviewLayer);
        _camera.Render();
        _renderedBattleId = renderId;
        setStatus(null, false);
        onRendered?.Invoke();
    }

    private void EnsureInitialized()
    {
        if (IsInitialized())
            return;

        DisposeRuntimeObjects();

        _rootObject = new GameObject("HistoryPanelPreviewRoot");
        _rootObject.SetActive(false);

        var cameraObject = CreatePreviewCameraObject(_rootObject.transform);
        _camera = cameraObject.GetComponent<Camera>();
        if (_camera == null)
            _camera = cameraObject.AddComponent<Camera>();
        _camera.enabled = false;
        _camera.clearFlags = CameraClearFlags.SolidColor;
        _camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        _camera.cullingMask = 1 << PreviewLayer;
        _camera.orthographic = false;
        _camera.fieldOfView = _cameraFieldOfView;
        _camera.nearClipPlane = 0.1f;
        _camera.farClipPlane = 200f;
        _camera.allowMSAA = true;
        _camera.allowHDR = false;

        var lightObject = new GameObject("HistoryPanelPreviewLight");
        lightObject.transform.SetParent(_rootObject.transform, false);
        _keyLight = lightObject.AddComponent<Light>();
        _keyLight.type = LightType.Directional;
        _keyLight.color = new Color(1f, 0.98f, 0.94f, 1f);
        _keyLight.intensity = 2.45f;
        _keyLight.shadows = LightShadows.None;
        lightObject.transform.rotation = Quaternion.LookRotation(
            new Vector3(-0.18f, 0.93f, -0.32f).normalized,
            Vector3.forward
        );

        var fillLightObject = new GameObject("HistoryPanelPreviewFillLight");
        fillLightObject.transform.SetParent(_rootObject.transform, false);
        _fillLight = fillLightObject.AddComponent<Light>();
        _fillLight.type = LightType.Directional;
        _fillLight.color = new Color(0.76f, 0.84f, 1f, 1f);
        _fillLight.intensity = 1.15f;
        _fillLight.shadows = LightShadows.None;
        fillLightObject.transform.rotation = Quaternion.LookRotation(
            new Vector3(0.22f, 0.95f, 0.20f).normalized,
            Vector3.forward
        );

        _playerBoardSurface = new PreviewBoardSurface(
            "HistoryPanelPlayerBoard",
            new PreviewItemCardSurface(),
            new PreviewSkillCardSurface()
        );
        _playerBoardRenderTarget = new PreviewBoardRenderTarget(_playerBoardSurface);
        _opponentBoardSurface = new PreviewBoardSurface(
            "HistoryPanelOpponentBoard",
            new PreviewItemCardSurface(),
            new PreviewSkillCardSurface()
        );
        _opponentBoardRenderTarget = new PreviewBoardRenderTarget(_opponentBoardSurface);

        _playerBoardRenderTarget.SetVisible(false);
        _opponentBoardRenderTarget.SetVisible(false);

        _backdropPlate = CreateBackdropPlate(_rootObject.transform);

        if (_playerBoardSurface.RootTransform != null)
            _playerBoardSurface.RootTransform.SetParent(_rootObject.transform, false);

        if (_opponentBoardSurface.RootTransform != null)
            _opponentBoardSurface.RootTransform.SetParent(_rootObject.transform, false);

        ApplyLayerRecursively(_playerBoardSurface.RootTransform, PreviewLayer);
        ApplyLayerRecursively(_opponentBoardSurface.RootTransform, PreviewLayer);
    }

    private static GameObject CreatePreviewCameraObject(Transform parent)
    {
        var mainCamera = Camera.main;
        GameObject cameraObject;
        if (mainCamera != null)
        {
            cameraObject = Object.Instantiate(mainCamera.gameObject, parent, false);
            cameraObject.name = "HistoryPanelPreviewCamera";
            PrepareClonedCameraObject(cameraObject);
            return cameraObject;
        }

        cameraObject = new GameObject("HistoryPanelPreviewCamera");
        cameraObject.transform.SetParent(parent, false);
        return cameraObject;
    }

    private static void PrepareClonedCameraObject(GameObject cameraObject)
    {
        foreach (var component in cameraObject.GetComponents<Component>().ToArray())
        {
            if (component == null || component is Transform)
                continue;

            if (component is Camera)
                continue;

            if (component is AudioListener)
            {
                Object.Destroy(component);
                continue;
            }

            var type = component.GetType();
            if (type.FullName != null && type.FullName.Contains("AdditionalCameraData"))
                continue;

            if (component is Behaviour behaviour)
                behaviour.enabled = false;
        }
    }

    private void EnsureRenderTexture()
    {
        if (_texture == null || !_texture.IsCreated())
        {
            if (_texture != null)
            {
                _texture.Release();
                Object.Destroy(_texture);
            }

            _texture = new RenderTexture(
                TextureWidth,
                TextureHeight,
                24,
                RenderTextureFormat.ARGB32
            )
            {
                antiAliasing = 2,
                useMipMap = false,
                autoGenerateMips = false,
                name = "HistoryPanelPreviewRT",
            };
            _texture.Create();
        }

        if (_camera != null && _camera.targetTexture != _texture)
            _camera.targetTexture = _texture;
    }

    private BoardLayout ConfigureBoards(HistoryBattlePreviewData previewData)
    {
        if (_camera == null || _playerBoardSurface == null || _opponentBoardSurface == null)
            return BoardLayout.Hidden;

        var presentation = CreatePresentation();
        var layout = ResolveBoardLayout(previewData);

        _playerBoardSurface.SetPresentation(ClonePresentation(presentation));
        _opponentBoardSurface.SetPresentation(ClonePresentation(presentation));
        _playerBoardSurface.UpdateAnchor(
            new Vector3(layout.PlayerX, _boardDepth, _boardVerticalOffset),
            Quaternion.identity
        );
        _opponentBoardSurface.UpdateAnchor(
            new Vector3(layout.OpponentX, _boardDepth, _boardVerticalOffset),
            Quaternion.identity
        );
        _playerBoardSurface.SetVisible(layout.ShowPlayerBoard);
        _opponentBoardSurface.SetVisible(layout.ShowOpponentBoard);
        ConfigureBackdrop(layout, presentation);

        _camera.fieldOfView = _cameraFieldOfView;
        _camera.transform.position = new Vector3(0f, _cameraDepth, _cameraVerticalCenter);
        _camera.transform.rotation = Quaternion.LookRotation(-Vector3.up, Vector3.forward);
        return layout;
    }

    private Task QueueBoardRenderAsync(
        PreviewBoardRenderTarget renderTarget,
        PreviewBoardModel model,
        PreviewBoardPresentation presentation,
        float boardX
    )
    {
        return renderTarget.QueueRenderAsync(
            new BoardRenderModel
            {
                Data = model,
                Presentation = ClonePresentation(presentation),
                Debug = new PreviewBoardDebugOptions(),
                Pose = new BoardPose
                {
                    Position = new Vector3(boardX, _boardDepth, _boardVerticalOffset),
                    Rotation = Quaternion.identity,
                },
            }
        );
    }

    private PreviewBoardPresentation CreatePresentation()
    {
        var presentation = MonsterPreviewDefaults.CreateShowcasePresentation();
        presentation.ShowSkillBoard = HistoryPanelPreviewSettings.ShowSkillBoard;
        presentation.ShowBrandingBoard = HistoryPanelPreviewSettings.ShowBrandingBoard;
        presentation.ShowMonsterInfoBoard = HistoryPanelPreviewSettings.ShowMonsterInfoBoard;
        presentation.CardSpacing = new Vector3(
            _cardSpacingX,
            presentation.CardSpacing.y,
            presentation.CardSpacing.z
        );
        presentation.CardScale = new Vector3(
            _cardWidthScale,
            (_cardWidthScale + _cardHeightScale) * 0.5f,
            _cardHeightScale
        );
        presentation.BoardSize = new Vector2(16.0f, 2.65f);
        presentation.SkillBoardWidth = 1.55f;
        return presentation;
    }

    private static PreviewBoardPresentation ClonePresentation(PreviewBoardPresentation source)
    {
        return new PreviewBoardPresentation
        {
            Visible = source.Visible,
            DebugEnabled = source.DebugEnabled,
            ShowSkillBoard = source.ShowSkillBoard,
            ShowBrandingBoard = source.ShowBrandingBoard,
            ShowMonsterInfoBoard = source.ShowMonsterInfoBoard,
            ShowItemBoardFill = source.ShowItemBoardFill,
            LocalOffset = source.LocalOffset,
            CardScale = source.CardScale,
            CardSpacing = source.CardSpacing,
            BoardSize = source.BoardSize,
            SkillBoardWidth = source.SkillBoardWidth,
            BoardThickness = source.BoardThickness,
            BorderThickness = source.BorderThickness,
            BorderHeight = source.BorderHeight,
        };
    }

    private static void ApplyLayerRecursively(Transform? root, int layer)
    {
        if (root == null)
            return;

        root.gameObject.layer = layer;
        for (var index = 0; index < root.childCount; index++)
            ApplyLayerRecursively(root.GetChild(index), layer);
    }

    private bool ReportTuning()
    {
        Invalidate();
        BppLog.Info("HistoryPanelPreviewRenderer", $"Preview tuning updated: {GetDebugSummary()}");
        return true;
    }

    private void ConfigureBackdrop(BoardLayout layout, PreviewBoardPresentation presentation)
    {
        if (_backdropPlate == null)
            return;

        var showBackdrop = layout.ShowPlayerBoard || layout.ShowOpponentBoard;
        _backdropPlate.SetActive(showBackdrop);
        if (!showBackdrop)
            return;

        var visibleBoardCount =
            (layout.ShowPlayerBoard ? 1 : 0) + (layout.ShowOpponentBoard ? 1 : 0);
        var width = presentation.BoardSize.x + BackdropPaddingX * 2f;
        if (visibleBoardCount > 1)
            width += Mathf.Abs(layout.OpponentX - layout.PlayerX);

        var centerX =
            visibleBoardCount > 1 ? (layout.PlayerX + layout.OpponentX) * 0.5f
            : layout.ShowPlayerBoard ? layout.PlayerX
            : layout.OpponentX;

        var depth = presentation.BoardSize.y + BackdropPaddingZ * 2f;
        _backdropPlate.transform.localPosition = new Vector3(
            centerX,
            _boardDepth - BackdropDepthOffset,
            _boardVerticalOffset
        );
        _backdropPlate.transform.localRotation = Quaternion.identity;
        _backdropPlate.transform.localScale = new Vector3(width, BackdropHeight, depth);
        ApplyLayerRecursively(_backdropPlate.transform, PreviewLayer);
    }

    private bool IsInitialized()
    {
        return _rootObject != null
            && _camera != null
            && _playerBoardSurface != null
            && _playerBoardSurface.IsAlive
            && _opponentBoardSurface != null
            && _opponentBoardSurface.IsAlive;
    }

    private void DisposeRuntimeObjects()
    {
        _playerBoardRenderTarget?.Dispose();
        _playerBoardRenderTarget = null;
        _playerBoardSurface = null;

        _opponentBoardRenderTarget?.Dispose();
        _opponentBoardRenderTarget = null;
        _opponentBoardSurface = null;

        if (_camera != null)
            Object.Destroy(_camera.gameObject);
        _camera = null;
        _keyLight = null;

        if (_backdropPlate != null)
            Object.Destroy(_backdropPlate);
        _backdropPlate = null;

        if (_rootObject != null)
            Object.Destroy(_rootObject);
        _rootObject = null;

        if (_texture != null)
        {
            _texture.Release();
            Object.Destroy(_texture);
            _texture = null;
        }
    }

    private BoardLayout ResolveBoardLayout(HistoryBattlePreviewData previewData)
    {
        var showPlayerBoard = previewData.HasRenderablePlayerBoard;
        var showOpponentBoard = previewData.HasRenderableOpponentBoard;

        if (showPlayerBoard && showOpponentBoard)
            return new BoardLayout(true, true, -_boardHorizontalOffset, _boardHorizontalOffset);

        if (showPlayerBoard)
            return new BoardLayout(true, false, 0f, 0f);

        if (showOpponentBoard)
            return new BoardLayout(false, true, 0f, 0f);

        return BoardLayout.Hidden;
    }

    private readonly struct BoardLayout
    {
        public BoardLayout(
            bool showPlayerBoard,
            bool showOpponentBoard,
            float playerX,
            float opponentX
        )
        {
            ShowPlayerBoard = showPlayerBoard;
            ShowOpponentBoard = showOpponentBoard;
            PlayerX = playerX;
            OpponentX = opponentX;
        }

        public bool ShowPlayerBoard { get; }

        public bool ShowOpponentBoard { get; }

        public float PlayerX { get; }

        public float OpponentX { get; }

        public static BoardLayout Hidden => new BoardLayout(false, false, 0f, 0f);
    }

    private static GameObject CreateBackdropPlate(Transform parent)
    {
        var plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
        plate.name = "HistoryPanelPreviewBackdrop";
        plate.transform.SetParent(parent, false);
        plate.layer = PreviewLayer;

        var collider = plate.GetComponent<Collider>();
        if (collider != null)
            Object.Destroy(collider);

        if (plate.TryGetComponent<Renderer>(out var renderer))
        {
            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
            if (shader != null)
            {
                var material = new Material(shader);
                material.color = new Color(0.15f, 0.18f, 0.23f, 0.78f);
                renderer.sharedMaterial = material;
            }
        }

        plate.SetActive(false);
        return plate;
    }
}

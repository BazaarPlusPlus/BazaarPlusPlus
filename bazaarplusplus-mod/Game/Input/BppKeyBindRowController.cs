#nullable enable
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TheBazaar.UI;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.Input;

internal sealed class BppKeyBindRowController : MonoBehaviour
{
    private static BppKeyBindRowController? _activeController;

    private readonly List<GameObject> _displayObjects = [];
    private readonly List<GameObject> _editObjects = [];

    private BppHotkeyActionId _actionId;
    private Button? _keybindButton;
    private Button? _resetButton;
    private TextMeshProUGUI? _keybindText;
    private TextMeshProUGUI? _warningText;
    private TextMeshProUGUI? _labelText;
    private bool _isRebinding;
    private bool _initialized;
    private Coroutine? _beginRebindCoroutine;
    private InputAction? _rebindCaptureAction;
    private InputActionRebindingExtensions.RebindingOperation? _rebindOperation;

    internal void Initialize(BppHotkeyActionId actionId, KeyBindController? templateController)
    {
        _actionId = actionId;
        _displayObjects.Clear();
        _editObjects.Clear();

        if (templateController != null)
        {
            templateController.enabled = false;
            CopyTemplateReferences(templateController);
        }

        FindFallbackReferences();

        if (_keybindButton != null)
        {
            _keybindButton.onClick.RemoveAllListeners();
            _keybindButton.onClick.AddListener(BeginDelayRebind);
        }

        if (_resetButton != null)
        {
            _resetButton.onClick.RemoveAllListeners();
            _resetButton.onClick.AddListener(ResetToDefault);
        }

        _initialized = true;
        EnterDefaultState();
    }

    private void OnDisable()
    {
        if (_activeController == this)
            _activeController = null;

        DisposeRebindResources();
    }

    internal void RefreshLanguage()
    {
        if (!_initialized)
            return;

        UpdateTexts();
        if (_isRebinding)
            ShowWarning(
                BppKeybindLabelResolver.ResolveRebindPrompt(PlayerPreferences.Data.LanguageCode)
            );
    }

    private void BeginDelayRebind()
    {
        if (!_initialized)
            return;

        if (_beginRebindCoroutine != null)
            StopCoroutine(_beginRebindCoroutine);

        _beginRebindCoroutine = StartCoroutine(BeginDelayRebindCoroutine());
    }

    private IEnumerator BeginDelayRebindCoroutine()
    {
        yield return null;
        _beginRebindCoroutine = null;

        if (!isActiveAndEnabled)
            yield break;

        EnterRebindState();
    }

    private void EnterRebindState()
    {
        if (_activeController != null && _activeController != this)
            return;

        _activeController = this;
        _isRebinding = true;
        if (_resetButton != null)
            _resetButton.interactable = false;
        if (_keybindButton != null)
            _keybindButton.interactable = false;
        SetObjectsActive(_displayObjects, false);
        SetObjectsActive(_editObjects, true);
        ShowWarning(
            BppKeybindLabelResolver.ResolveRebindPrompt(PlayerPreferences.Data.LanguageCode)
        );
        StartInteractiveRebind();
    }

    private void EnterDefaultState()
    {
        if (_activeController == this)
            _activeController = null;

        _isRebinding = false;
        SetObjectsActive(_displayObjects, true);
        SetObjectsActive(_editObjects, false);
        DisposeRebindOperation();
        if (_keybindButton != null)
            _keybindButton.interactable = true;
        UpdateTexts();
        ShowWarning(null);
    }

    private void ResetToDefault()
    {
        BppHotkeyService.ResetToDefault(_actionId);
        EnterDefaultState();
    }

    private void UpdateTexts()
    {
        var languageCode = PlayerPreferences.Data.LanguageCode;
        if (_labelText != null)
            _labelText.text = BppKeybindLabelResolver.ResolveActionLabel(_actionId, languageCode);

        if (_keybindText != null)
            _keybindText.text = BppHotkeyService.GetBindingDisplay(_actionId);

        if (_resetButton != null)
            _resetButton.interactable = !BppHotkeyService.UsesDefault(_actionId);
    }

    private void ShowWarning(string? message)
    {
        if (_warningText == null)
            return;

        var shouldShow = !string.IsNullOrWhiteSpace(message);
        _warningText.text = shouldShow ? message : string.Empty;
        _warningText.gameObject.SetActive(shouldShow);
    }

    private void CopyTemplateReferences(KeyBindController templateController)
    {
        _keybindButton = GetFieldValue<Button>(templateController, "_keybindButton");
        _resetButton = GetFieldValue<Button>(templateController, "_resetToDefaultButton");
        _keybindText = GetFieldValue<TextMeshProUGUI>(templateController, "_keybindText");
        _warningText = GetFieldValue<TextMeshProUGUI>(templateController, "_warningText");

        var displayObjects = GetFieldValue<List<GameObject>>(
            templateController,
            "_displayKeybindObjects"
        );
        if (displayObjects != null)
            _displayObjects.AddRange(displayObjects.Where(candidate => candidate != null));

        var editObjects = GetFieldValue<List<GameObject>>(templateController, "_editRebindObjects");
        if (editObjects != null)
            _editObjects.AddRange(editObjects.Where(candidate => candidate != null));
    }

    private void FindFallbackReferences()
    {
        _keybindButton ??= GetComponentInChildren<Button>(includeInactive: true);
        _resetButton ??= GetComponentsInChildren<Button>(includeInactive: true)
            .Skip(1)
            .FirstOrDefault();
        _keybindText ??= GetComponentsInChildren<TextMeshProUGUI>(includeInactive: true)
            .FirstOrDefault(text =>
                text != null && text.transform.IsChildOf(_keybindButton?.transform)
            );
        _warningText ??= GetComponentsInChildren<TextMeshProUGUI>(includeInactive: true)
            .FirstOrDefault(text =>
                text != null
                && text != _keybindText
                && !string.IsNullOrWhiteSpace(text.text)
                && text.gameObject != _labelText?.gameObject
            );

        _labelText = FindActionLabelText();

        if (_displayObjects.Count == 0 && _keybindButton != null)
            _displayObjects.Add(_keybindButton.gameObject);
    }

    private TextMeshProUGUI? FindActionLabelText()
    {
        return GetComponentsInChildren<TextMeshProUGUI>(includeInactive: true)
            .FirstOrDefault(text =>
                text != null
                && text != _keybindText
                && text != _warningText
                && (_keybindButton == null || !text.transform.IsChildOf(_keybindButton.transform))
                && (_resetButton == null || !text.transform.IsChildOf(_resetButton.transform))
            );
    }

    private static T? GetFieldValue<T>(object instance, string fieldName)
        where T : class
    {
        return AccessTools.Field(instance.GetType(), fieldName)?.GetValue(instance) as T;
    }

    private static void SetObjectsActive(IEnumerable<GameObject> objects, bool active)
    {
        foreach (var gameObject in objects.Where(candidate => candidate != null))
            gameObject.SetActive(active);
    }

    private void StartInteractiveRebind()
    {
        DisposeRebindOperation();
        _rebindCaptureAction ??= CreateRebindCaptureAction();
        _rebindOperation = _rebindCaptureAction
            .PerformInteractiveRebinding(0)
            .WithControlsExcluding("<Mouse>/position")
            .WithControlsExcluding("<Mouse>/delta")
            .WithControlsExcluding("<Mouse>/scroll")
            .WithControlsExcluding("<Mouse>/leftButton")
            .WithCancelingThrough("<Keyboard>/escape")
            .OnMatchWaitForAnother(0.1f)
            .OnCancel(_ =>
            {
                if (isActiveAndEnabled)
                    EnterDefaultState();
            })
            .OnComplete(HandleRebindComplete);
        _rebindOperation.Start();
    }

    private void HandleRebindComplete(InputActionRebindingExtensions.RebindingOperation operation)
    {
        var bindingPath =
            _rebindCaptureAction?.bindings.Count > 0
                ? _rebindCaptureAction.bindings[0].overridePath
                : null;
        bindingPath ??= operation.selectedControl?.path;

        DisposeRebindOperation();

        if (BppHotkeyService.TrySetBindingPath(_actionId, bindingPath, out var errorMessage))
        {
            EnterDefaultState();
            return;
        }

        ShowWarning(errorMessage);
        StartInteractiveRebind();
    }

    private void DisposeRebindOperation()
    {
        _rebindOperation?.Dispose();
        _rebindOperation = null;
    }

    private void DisposeRebindResources()
    {
        if (_beginRebindCoroutine != null)
        {
            StopCoroutine(_beginRebindCoroutine);
            _beginRebindCoroutine = null;
        }

        DisposeRebindOperation();

        _rebindCaptureAction?.Dispose();
        _rebindCaptureAction = null;
    }

    private static InputAction CreateRebindCaptureAction()
    {
        var action = new InputAction(type: InputActionType.Button);
        action.AddBinding("<Keyboard>/space");
        return action;
    }
}

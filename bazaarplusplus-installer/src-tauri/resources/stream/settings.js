const DEFAULT_CROP = {
  left: 0.342,
  top: 0.313,
  width: 0.58,
  height: 0.22
};

// This page is served by the local stream service on its own origin, so it
// cannot read the app's stored locale. The app appends `?lang=` when it opens
// the page; a direct visit falls back to the browser language.
const MESSAGES = {
  en: {
    documentTitle: 'BazaarPlusPlus Overlay Calibration',
    eyebrow: 'Stream Overlay',
    heading: 'Crop Calibration',
    pageIntro:
      'Define the exact part of the end-of-run screenshot that appears in your stream overlay.',
    stepsLabel: 'Calibration steps',
    stepAdjustTitle: 'Adjust',
    stepAdjustDetail: 'Frame the result',
    stepPreviewTitle: 'Preview',
    stepPreviewDetail: 'Check the output',
    stepSaveTitle: 'Save',
    stepSaveDetail: 'Apply to the overlay',
    previewEyebrow: 'Preview',
    previewHeading: 'Frame your overlay precisely',
    previewEmpty: 'Waiting for screenshots with end-of-run crops.',
    sourceImage: 'Source image',
    sourceHint: 'The highlighted frame is the selected area.',
    resultImage: 'Overlay output',
    resultHint: 'This is what viewers will see.',
    noSample: 'No sample selected',
    imageAlt: 'Selected end-of-run record image',
    resultAlt: 'Live preview of the cropped overlay output',
    controlsEyebrow: 'Controls',
    controlsHeading: 'Fine-tune the crop',
    positionGroup: 'Position',
    sizeGroup: 'Size',
    cropLeft: 'Horizontal start',
    cropTop: 'Vertical start',
    cropWidth: 'Width',
    cropHeight: 'Height',
    controlHint:
      'Tip: focus a slider and use the arrow keys for precise adjustments.',
    saveButton: 'Save changes',
    saveButtonBusy: 'Saving…',
    resetButton: 'Undo changes',
    copyButton: 'Copy code',
    settingsCodeSummary: 'Advanced: configuration code',
    settingsCodeHint:
      'Use this code to transfer the same crop to another setup.',
    statusLoading: 'Loading the selected stream record...',
    statusLoaded:
      'Sample ready. Adjust the controls and check the overlay output before saving.',
    statusEmpty:
      'No end-of-run record is available in the current stream window yet. Finish a run, then refresh this page.',
    statusUnsaved: 'You have unsaved crop changes.',
    statusSaving: 'Saving crop changes…',
    statusSaved: 'Crop saved. The overlay will use it on the next refresh.',
    statusReverted: 'Unsaved changes reverted.',
    statusCopied: 'Base64 settings code copied to clipboard.',
    statusSaveFailed: 'Failed to save crop.',
    statusCopyFailed: 'Failed to copy code.',
    statusLoadFailed: 'Failed to load calibration data.',
    unknownHero: 'Unknown hero',
    battles: '{count} battles'
  },
  zh: {
    documentTitle: 'BazaarPlusPlus 叠加层校准',
    eyebrow: '直播叠加层',
    heading: '裁切校准',
    pageIntro: '精确选择结算截图中要显示在直播叠加层里的区域。',
    stepsLabel: '校准步骤',
    stepAdjustTitle: '调整',
    stepAdjustDetail: '框定目标区域',
    stepPreviewTitle: '预览',
    stepPreviewDetail: '确认直播画面',
    stepSaveTitle: '保存',
    stepSaveDetail: '应用到叠加层',
    previewEyebrow: '预览',
    previewHeading: '精准框定直播画面',
    previewEmpty: '正在等待带有结算裁切的截图。',
    sourceImage: '源图像',
    sourceHint: '高亮边框内是当前选择的区域。',
    resultImage: '叠加层输出',
    resultHint: '观众最终会看到这个画面。',
    noSample: '未选择样本',
    imageAlt: '当前选中的结算记录图像',
    resultAlt: '裁切后的直播叠加层实时预览',
    controlsEyebrow: '控制',
    controlsHeading: '微调裁切范围',
    positionGroup: '位置',
    sizeGroup: '尺寸',
    cropLeft: '横向起点',
    cropTop: '纵向起点',
    cropWidth: '宽',
    cropHeight: '高',
    controlHint: '提示：聚焦滑杆后可用方向键进行精细调整。',
    saveButton: '保存更改',
    saveButtonBusy: '正在保存…',
    resetButton: '撤销更改',
    copyButton: '复制代码',
    settingsCodeSummary: '高级：配置代码',
    settingsCodeHint: '可使用此代码将同一套裁切配置迁移到其他环境。',
    statusLoading: '正在读取当前展示的对局记录…',
    statusLoaded: '样本已就绪。调整控制项并确认叠加层输出后保存。',
    statusEmpty: '当前展示窗口内还没有结算记录。完成一局对局后刷新本页即可。',
    statusUnsaved: '裁切范围有尚未保存的更改。',
    statusSaving: '正在保存裁切更改…',
    statusSaved: '裁切已保存，叠加层将在下次刷新时使用该配置。',
    statusReverted: '已撤销尚未保存的更改。',
    statusCopied: 'Base64 设置代码已复制到剪贴板。',
    statusSaveFailed: '保存裁切失败。',
    statusCopyFailed: '复制裁切代码失败。',
    statusLoadFailed: '加载校准数据失败。',
    unknownHero: '未知英雄',
    battles: '{count} 场战斗'
  }
};

const locale = resolveLocale();
const strings = MESSAGES[locale];

function resolveLocale() {
  const candidates = [];
  try {
    candidates.push(new URLSearchParams(window.location.search).get('lang'));
  } catch {
    // A malformed query string just falls through to the browser language.
  }
  candidates.push(...(navigator.languages || []), navigator.language);

  for (const candidate of candidates) {
    if (typeof candidate !== 'string' || !candidate) continue;
    const lower = candidate.toLowerCase();
    if (lower.startsWith('zh')) return 'zh';
    if (lower.startsWith('en')) return 'en';
  }
  return 'en';
}

function t(key, params) {
  const text = strings[key] ?? MESSAGES.en[key] ?? key;
  if (!params) return text;
  return Object.entries(params).reduce(
    (result, [name, value]) => result.replaceAll(`{${name}}`, String(value)),
    text
  );
}

function applyStaticTranslations() {
  document.documentElement.lang = locale === 'zh' ? 'zh-CN' : 'en';
  document.title = t('documentTitle');

  document.querySelectorAll('[data-i18n]').forEach((node) => {
    node.textContent = t(node.dataset.i18n);
  });
  document.querySelectorAll('[data-i18n-alt]').forEach((node) => {
    node.setAttribute('alt', t(node.dataset.i18nAlt));
  });
  document.querySelectorAll('[data-i18n-aria-label]').forEach((node) => {
    node.setAttribute('aria-label', t(node.dataset.i18nAriaLabel));
  });
}

const pageStatus = document.getElementById('page-status');
const pageStatusText = document.getElementById('page-status-text');
const previewEmpty = document.getElementById('preview-empty');
const previewWorkspace = document.getElementById('preview-workspace');
const fullPreviewImage = document.getElementById('full-preview-image');
const stripPreviewImage = document.getElementById('strip-preview-image');
const selectedMeta = document.getElementById('selected-meta');
const codeField = document.getElementById('settings-code');
const saveButton = document.getElementById('save-button');
const resetButton = document.getElementById('reset-button');
const copyButton = document.getElementById('copy-button');
const inputs = {
  left: document.getElementById('crop-left'),
  top: document.getElementById('crop-top'),
  width: document.getElementById('crop-width'),
  height: document.getElementById('crop-height')
};
const outputs = {
  left: document.getElementById('crop-left-value'),
  top: document.getElementById('crop-top-value'),
  width: document.getElementById('crop-width-value'),
  height: document.getElementById('crop-height-value')
};

let selectedRecord = null;
let currentCrop = { ...DEFAULT_CROP };
let savedCrop = { ...DEFAULT_CROP };
let initialized = false;
let previewNonce = 0;
let previewTimer = null;
const requestedOffset = readRequestedOffset();

function setStatus(message, state = 'neutral') {
  if (pageStatusText) {
    pageStatusText.textContent = message;
  }
  if (pageStatus) {
    pageStatus.dataset.state = state;
  }
}

// Request failures carry the raw response body, which can be a whole HTML error
// document. That belongs in the console, not in the page's status line.
function reportFailure(error, messageKey) {
  console.error('[calibration]', error);
  setStatus(t(messageKey), 'error');
}

function readRequestedOffset() {
  try {
    const params = new URLSearchParams(window.location.search);
    const raw = Number(params.get('offset') || '0');
    if (!Number.isFinite(raw)) {
      return 0;
    }
    return Math.max(0, Math.trunc(raw));
  } catch {
    return 0;
  }
}

function setCropVariables(crop) {
  const root = document.documentElement;
  root.style.setProperty('--crop-left', `${crop.left * 100}%`);
  root.style.setProperty('--crop-top', `${crop.top * 100}%`);
  root.style.setProperty('--crop-width', `${crop.width * 100}%`);
  root.style.setProperty('--crop-height', `${crop.height * 100}%`);
}

function formatPercent(value) {
  return `${(value * 100).toFixed(1)}%`;
}

function cropsMatch(left, right) {
  return Object.keys(DEFAULT_CROP).every(
    (key) => Math.abs(left[key] - right[key]) < 0.0005
  );
}

function updateActionState() {
  const dirty = initialized && !cropsMatch(currentCrop, savedCrop);
  if (saveButton) {
    saveButton.disabled = !dirty;
  }
  if (resetButton) {
    resetButton.disabled = !dirty;
  }
}

function readCropFromInputs() {
  return {
    left: Number(inputs.left?.value || DEFAULT_CROP.left),
    top: Number(inputs.top?.value || DEFAULT_CROP.top),
    width: Number(inputs.width?.value || DEFAULT_CROP.width),
    height: Number(inputs.height?.value || DEFAULT_CROP.height)
  };
}

function writeCropToInputs(crop) {
  Object.entries(crop).forEach(([key, value]) => {
    const input = inputs[key];
    if (input) {
      input.value = value.toFixed(3);
    }
    const output = outputs[key];
    if (output) {
      output.value = formatPercent(value);
      output.textContent = formatPercent(value);
    }
    if (input) {
      input.setAttribute('aria-valuetext', formatPercent(value));
    }
  });
}

function buildStripUrl(recordId, crop) {
  const params = new URLSearchParams({
    left: crop.left.toFixed(3),
    top: crop.top.toFixed(3),
    width: crop.width.toFixed(3),
    height: crop.height.toFixed(3),
    preview: '1',
    v: String(previewNonce)
  });

  return `/images/${encodeURIComponent(recordId)}/strip?${params.toString()}`;
}

function encodeCropCode(crop) {
  const payload = JSON.stringify({ v: 1, crop });
  const bytes = new TextEncoder().encode(payload);
  let binary = '';
  bytes.forEach((byte) => {
    binary += String.fromCharCode(byte);
  });
  return window.btoa(binary);
}

function updateCodeField(crop) {
  if (!codeField) {
    return;
  }

  codeField.textContent = encodeCropCode(crop);
}

function renderPreview() {
  const crop = readCropFromInputs();
  currentCrop = crop;
  previewNonce += 1;
  writeCropToInputs(crop);
  setCropVariables(crop);

  if (!selectedRecord?.id) {
    if (previewEmpty) {
      previewEmpty.hidden = false;
    }
    if (previewWorkspace) {
      previewWorkspace.hidden = true;
    }
    return;
  }

  if (previewEmpty) {
    previewEmpty.hidden = true;
  }
  if (previewWorkspace) {
    previewWorkspace.hidden = false;
  }
  if (fullPreviewImage) {
    const nextSource = `/images/${encodeURIComponent(selectedRecord.id)}`;
    if (fullPreviewImage.getAttribute('src') !== nextSource) {
      fullPreviewImage.src = nextSource;
    }
  }
  if (stripPreviewImage) {
    stripPreviewImage.src = buildStripUrl(selectedRecord.id, crop);
  }
  if (selectedMeta) {
    selectedMeta.textContent = [
      selectedRecord.title || t('unknownHero'),
      typeof selectedRecord.wins === 'number'
        ? `${selectedRecord.wins}W`
        : null,
      typeof selectedRecord.battle_count === 'number'
        ? t('battles', { count: selectedRecord.battle_count })
        : null
    ]
      .filter(Boolean)
      .join(' · ');
  }
}

async function loadCropSettings() {
  const response = await fetch('/api/overlay/crop-config', {
    cache: 'no-store'
  });
  if (!response.ok) {
    throw new Error(await response.text());
  }

  return response.json();
}

async function loadRecords() {
  const endpoint = new URL(
    '/api/stream/records/latest',
    window.location.origin
  );
  if (requestedOffset > 0) {
    endpoint.searchParams.set('offset', String(requestedOffset));
  }

  const response = await fetch(endpoint, { cache: 'no-store' });
  if (!response.ok) {
    throw new Error(await response.text());
  }

  const payload = await response.json();
  return payload?.id ? payload : null;
}

async function saveCrop() {
  const crop = readCropFromInputs();
  if (saveButton) {
    saveButton.disabled = true;
    saveButton.setAttribute('aria-busy', 'true');
    saveButton.textContent = t('saveButtonBusy');
  }
  if (resetButton) {
    resetButton.disabled = true;
  }
  setStatus(t('statusSaving'), 'loading');
  const response = await fetch('/api/overlay/crop-config', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json'
    },
    body: JSON.stringify({ crop })
  });

  if (!response.ok) {
    throw new Error(await response.text());
  }

  const payload = await response.json();
  currentCrop = payload.crop;
  savedCrop = { ...payload.crop };
  writeCropToInputs(payload.crop);
  setCropVariables(payload.crop);
  updateCodeField(payload.crop);
  renderPreview();
  setStatus(t('statusSaved'), 'success');
  if (saveButton) {
    saveButton.removeAttribute('aria-busy');
    saveButton.textContent = t('saveButton');
  }
  updateActionState();
}

function resetCrop() {
  currentCrop = { ...savedCrop };
  writeCropToInputs(currentCrop);
  setCropVariables(currentCrop);
  updateCodeField(currentCrop);
  renderPreview();
  updateActionState();
  setStatus(t('statusReverted'), 'neutral');
}

async function copyCode() {
  const code = codeField?.textContent?.trim() || '';
  if (!code) {
    return;
  }

  await navigator.clipboard.writeText(code);
  setStatus(t('statusCopied'));
}

function bindInputHandlers() {
  Object.values(inputs).forEach((input) => {
    input?.addEventListener('input', () => {
      currentCrop = readCropFromInputs();
      writeCropToInputs(currentCrop);
      setCropVariables(currentCrop);
      updateCodeField(currentCrop);
      updateActionState();
      if (initialized) {
        setStatus(t('statusUnsaved'), 'pending');
      }
      if (previewTimer) {
        window.clearTimeout(previewTimer);
      }
      previewTimer = window.setTimeout(() => {
        renderPreview();
      }, 120);
    });
  });

  saveButton?.addEventListener('click', async () => {
    try {
      await saveCrop();
    } catch (error) {
      reportFailure(error, 'statusSaveFailed');
      if (saveButton) {
        saveButton.removeAttribute('aria-busy');
        saveButton.textContent = t('saveButton');
      }
      updateActionState();
    }
  });

  resetButton?.addEventListener('click', resetCrop);

  copyButton?.addEventListener('click', async () => {
    try {
      await copyCode();
    } catch (error) {
      reportFailure(error, 'statusCopyFailed');
    }
  });
}

async function initialize() {
  try {
    applyStaticTranslations();
    bindInputHandlers();
    const [settingsPayload, latestRecord] = await Promise.all([
      loadCropSettings(),
      loadRecords()
    ]);

    currentCrop = settingsPayload?.crop || { ...DEFAULT_CROP };
    savedCrop = { ...currentCrop };
    writeCropToInputs(currentCrop);
    setCropVariables(currentCrop);
    updateCodeField(currentCrop);

    selectedRecord = latestRecord;
    renderPreview();

    initialized = true;
    updateActionState();

    setStatus(
      t(selectedRecord ? 'statusLoaded' : 'statusEmpty'),
      selectedRecord ? 'ready' : 'neutral'
    );
  } catch (error) {
    reportFailure(error, 'statusLoadFailed');
  }
}

void initialize();

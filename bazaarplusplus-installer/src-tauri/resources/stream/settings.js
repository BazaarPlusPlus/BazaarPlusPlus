const DEFAULT_CROP = {
  left: 0.342,
  top: 0.313,
  width: 0.58,
  height: 0.22
};

const pageStatus = document.getElementById('page-status');
const previewEmpty = document.getElementById('preview-empty');
const previewWorkspace = document.getElementById('preview-workspace');
const fullPreviewImage = document.getElementById('full-preview-image');
const selectedMeta = document.getElementById('selected-meta');
const codeField = document.getElementById('settings-code');
const saveButton = document.getElementById('save-button');
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
let previewNonce = 0;
let previewTimer = null;
const requestedOffset = readRequestedOffset();

function setStatus(message) {
  if (pageStatus) {
    pageStatus.textContent = message;
  }
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
      output.value = value.toFixed(3);
      output.textContent = value.toFixed(3);
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

  if (!selectedRecord?.image_url) {
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
    fullPreviewImage.src = selectedRecord.image_url;
  }
  if (selectedMeta) {
    selectedMeta.textContent = [
      selectedRecord.title || 'Unknown hero',
      typeof selectedRecord.wins === 'number' ? `${selectedRecord.wins}W` : null,
      typeof selectedRecord.battle_count === 'number'
        ? `${selectedRecord.battle_count} battles`
        : null
    ]
      .filter(Boolean)
      .join(' · ');
  }
}

async function loadCropSettings() {
  const response = await fetch('/api/overlay/crop-config', { cache: 'no-store' });
  if (!response.ok) {
    throw new Error(await response.text());
  }

  return response.json();
}

async function loadRecords() {
  const endpoint = new URL('/api/records/latest', window.location.origin);
  if (requestedOffset > 0) {
    endpoint.searchParams.set('offset', String(requestedOffset));
  }

  const response = await fetch(endpoint, { cache: 'no-store' });
  if (!response.ok) {
    throw new Error(await response.text());
  }

  const payload = await response.json();
  return payload?.image_url ? payload : null;
}

async function saveCrop() {
  const crop = readCropFromInputs();
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
  writeCropToInputs(payload.crop);
  setCropVariables(payload.crop);
  updateCodeField(payload.crop);
  renderPreview();
  setStatus('Crop saved. Overlay will use this code on the next refresh.');
}

async function copyCode() {
  const code = codeField?.textContent?.trim() || '';
  if (!code) {
    return;
  }

  await navigator.clipboard.writeText(code);
  setStatus('Base64 settings code copied to clipboard.');
}

function bindInputHandlers() {
  Object.values(inputs).forEach((input) => {
    input?.addEventListener('input', () => {
      currentCrop = readCropFromInputs();
      writeCropToInputs(currentCrop);
      setCropVariables(currentCrop);
      updateCodeField(currentCrop);
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
      setStatus(error instanceof Error ? error.message : 'Failed to save crop.');
    }
  });

  copyButton?.addEventListener('click', async () => {
    try {
      await copyCode();
    } catch (error) {
      setStatus(error instanceof Error ? error.message : 'Failed to copy code.');
    }
  });

}

async function initialize() {
  try {
    bindInputHandlers();
    const [settingsPayload, latestRecord] = await Promise.all([
      loadCropSettings(),
      loadRecords()
    ]);

    currentCrop = settingsPayload?.crop || { ...DEFAULT_CROP };
    writeCropToInputs(currentCrop);
    setCropVariables(currentCrop);
    updateCodeField(currentCrop);

    selectedRecord = latestRecord;
    renderPreview();

    setStatus(
      selectedRecord
        ? 'Loaded the selected stream record image. Adjust the crop on the source image, then save or copy the code.'
        : 'No end-of-run record is available in the current stream window yet. Finish a run, then refresh this page.'
    );
  } catch (error) {
    setStatus(
      error instanceof Error
        ? error.message
        : 'Failed to load calibration data.'
    );
  }
}

void initialize();

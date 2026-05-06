const root = document.getElementById('overlay-root');
const signal = document.getElementById('overlay-signal');
const status = document.getElementById('overlay-status');
const emptyState = document.getElementById('overlay-empty');
const emptyTitle = document.getElementById('overlay-empty-title');
const emptyDetail = document.getElementById('overlay-empty-detail');
const list = document.getElementById('overlay-list');

let lastRecordKey = null;
let rowResizeObserver = null;
let currentDisplayMode = 'current';

function setClassNames(...tokens) {
  if (!root) {
    return;
  }

  root.className = ['overlay', ...tokens].join(' ');
}

function setText(node, value) {
  if (node) {
    node.textContent = value;
  }
}

function markUpdated() {
  if (!root) {
    return;
  }

  root.classList.remove('is-updated');
  window.requestAnimationFrame(() => {
    root.classList.add('is-updated');
    window.setTimeout(() => root.classList.remove('is-updated'), 500);
  });
}

function formatMetric(value, suffix) {
  if (typeof value !== 'number' || !Number.isFinite(value)) {
    return null;
  }

  return `${value}${suffix}`;
}

function formatRank(record) {
  const rankValue = typeof record?.rank === 'string' ? record.rank.trim() : '';
  const ratingValue =
    typeof record?.rating === 'number' && Number.isFinite(record.rating)
      ? String(record.rating)
      : '';

  if (rankValue && ratingValue) {
    return `${rankValue} ${ratingValue}`;
  }

  if (rankValue) {
    return rankValue;
  }

  if (ratingValue) {
    return ratingValue;
  }

  return null;
}

function formatTimestamp(value) {
  if (typeof value !== 'string' || !value.trim()) {
    return '';
  }

  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) {
    return value;
  }

  const month = String(parsed.getMonth() + 1).padStart(2, '0');
  const day = String(parsed.getDate()).padStart(2, '0');
  const hours = String(parsed.getHours()).padStart(2, '0');
  const minutes = String(parsed.getMinutes()).padStart(2, '0');

  return `${month}-${day} ${hours}:${minutes}`;
}

function escapeHtml(value) {
  return String(value)
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;');
}

function buildStats(record) {
  return {
    wins:
      typeof record?.wins === 'number' && Number.isFinite(record.wins)
        ? record.wins
        : null,
    battles:
      typeof record?.battle_count === 'number' && Number.isFinite(record.battle_count)
        ? record.battle_count
        : null
  };
}

function getVictoryTier(wins, battles) {
  if (typeof wins !== 'number' || !Number.isFinite(wins)) {
    return {
      className: 'tier-unknown',
      label: 'RUN'
    };
  }

  if (wins === 10 && battles === 10) {
    return {
      className: 'tier-diamond',
      label: 'DIA'
    };
  }

  if (wins >= 10 && typeof battles === 'number' && battles > 10) {
    return {
      className: 'tier-gold',
      label: 'GLD'
    };
  }

  if (wins >= 7) {
    return {
      className: 'tier-silver',
      label: 'SLV'
    };
  }

  if (wins >= 4) {
    return {
      className: 'tier-bronze',
      label: 'BRZ'
    };
  }

  return {
    className: 'tier-misfortune',
    label: 'MIS'
  };
}

function getHeroBadgeStyle(heroName) {
  const normalized = typeof heroName === 'string' ? heroName.trim() : '';
  if (!normalized) {
    return {
      shortCode: 'UNK',
      background: 'rgba(51, 74, 97, 0.95)',
      text: '#ffffff',
      assetKey: 'unk'
    };
  }

  const map = {
    Vanessa: { shortCode: 'VAN', background: 'rgb(192, 33, 33)', text: '#ffffff', assetKey: 'van' },
    Pygmalien: { shortCode: 'PYG', background: 'rgb(39, 103, 192)', text: '#ffffff', assetKey: 'pyg' },
    Dooley: { shortCode: 'DOO', background: 'rgb(225, 154, 8)', text: '#ffffff', assetKey: 'doo' },
    Mak: { shortCode: 'MAK', background: 'rgb(190, 230, 91)', text: 'rgb(26, 31, 38)', assetKey: 'mak' },
    Jules: { shortCode: 'JUL', background: 'rgb(180, 52, 236)', text: '#ffffff', assetKey: 'jul' },
    Karnok: { shortCode: 'KAR', background: 'rgb(59, 136, 156)', text: '#ffffff', assetKey: 'kar' },
    Stelle: { shortCode: 'STE', background: 'rgb(255, 235, 24)', text: 'rgb(26, 31, 38)', assetKey: 'ste' }
  };

  if (map[normalized]) {
    return map[normalized];
  }

  return {
    shortCode: normalized.length <= 3 ? normalized.toUpperCase() : normalized.slice(0, 3).toUpperCase(),
    background: 'rgba(57, 73, 97, 0.98)',
    text: '#ffffff',
    assetKey: 'unk'
  };
}

function normalizeDisplayMode(value) {
  if (value === 'hero' || value === 'herohalf') {
    return value;
  }

  return 'current';
}

function getWinsBadgeAsset(wins, battles) {
  if (typeof wins !== 'number' || !Number.isFinite(wins) || wins < 0) {
    return '/assets/badges/wins/wins-0-mis.svg';
  }

  const safeWins = Math.max(0, Math.min(10, Math.trunc(wins)));
  if (safeWins === 10) {
    if (typeof battles === 'number' && Number.isFinite(battles) && Math.trunc(battles) === 10) {
      return '/assets/badges/wins/wins-10-dia.svg';
    }
    return '/assets/badges/wins/wins-10-gld.svg';
  }
  if (safeWins >= 7) {
    return `/assets/badges/wins/wins-${safeWins}-slv.svg`;
  }
  if (safeWins >= 4) {
    return `/assets/badges/wins/wins-${safeWins}-brz.svg`;
  }
  return `/assets/badges/wins/wins-${safeWins}-mis.svg`;
}

function getInfoBadgeAsset(heroKey, battles) {
  const safeHeroKey = typeof heroKey === 'string' && heroKey ? heroKey : 'unk';
  const safeBattles =
    typeof battles === 'number' && Number.isFinite(battles)
      ? Math.max(0, Math.min(20, Math.trunc(battles)))
      : 0;
  return `/assets/badges/info/info-${safeHeroKey}-${safeBattles}.svg`;
}

function getHeroModeAsset(heroKey, displayMode) {
  const safeHeroKey = typeof heroKey === 'string' && heroKey ? heroKey : 'unk';
  if (displayMode === 'hero') {
    return `/assets/badges/heroes/hero-${safeHeroKey}.svg`;
  }

  return `/assets/badges/herohalf/herohalf-${safeHeroKey}.svg`;
}

function buildRowMarkup(record) {
  const title = escapeHtml(record?.title || 'Unknown hero');
  const stats = buildStats(record);
  const heroBadge = getHeroBadgeStyle(record?.title);
  const secondBadgeSrc =
    currentDisplayMode === 'current'
      ? getInfoBadgeAsset(heroBadge.assetKey, stats.battles)
      : getHeroModeAsset(heroBadge.assetKey, currentDisplayMode);
  const secondBadgeAlt =
    currentDisplayMode === 'current'
      ? `${stats.battles ?? 'unknown'} battles with ${escapeHtml(heroBadge.shortCode)}`
      : `${escapeHtml(title)} hero badge`;
  const scoreMarkup = `
    <div class="meta-stack mode-${currentDisplayMode}">
      <div class="metric-tile score-tile">
        <img class="metric-badge-svg" src="${getWinsBadgeAsset(stats.wins, stats.battles)}" alt="${stats.wins ?? 'unknown'} wins" loading="eager" />
      </div>
      <div class="metric-tile secondary-tile">
        <img class="metric-badge-svg" src="${secondBadgeSrc}" alt="${secondBadgeAlt}" loading="eager" />
      </div>
    </div>
  `;
  const imageUrl =
    typeof record?.image_url === 'string' && record.image_url
      ? `${record.image_url}/strip`
      : '';
  const image = imageUrl
    ? `<figure class="run-visual"><div class="visual-frame"><div class="visual-stage"><img src="${escapeHtml(imageUrl)}" alt="${title} run strip" loading="eager" /></div></div></figure>`
    : `<figure class="run-visual"><div class="visual-frame"><div class="visual-stage"></div></div></figure>`;

  return `
    <article class="run-row">
      <div class="run-card">
        <section class="run-meta">
          <p class="hero-name sr-only">${title}</p>
          ${scoreMarkup}
        </section>
        ${image}
      </div>
    </article>
  `;
}

function renderEmpty(message) {
  setClassNames('empty');
  setText(signal, 'Local feed idle');
  setText(status, 'Waiting');

  if (emptyState) {
    emptyState.hidden = false;
    emptyState.style.display = '';
  }

  if (list) {
    list.hidden = true;
    list.innerHTML = '';
  }

  setText(emptyTitle, 'No records yet');
  setText(
    emptyDetail,
    message ?? 'Start a match and the overlay will update automatically.'
  );
}

function renderRecords(records, options = {}) {
  if (!list) {
    return;
  }

  const { stale = false, updated = false } = options;
  setClassNames(stale ? 'stale' : 'live');
  setText(
    signal,
    stale ? 'Connection lost, showing cached data' : 'Local SQLite feed live'
  );
  setText(status, stale ? 'Retrying' : 'Live');

  if (emptyState) {
    emptyState.hidden = true;
    emptyState.style.display = 'none';
  }

  list.hidden = false;
  list.innerHTML = records.map(buildRowMarkup).join('');
  syncRowHeights();

  if (updated) {
    markUpdated();
  }
}

function applyRowHeight(row) {
  if (!(row instanceof HTMLElement)) {
    return;
  }

  const visualStage = row.querySelector('.visual-stage');
  const meta = row.querySelector('.run-meta');
  if (!(visualStage instanceof HTMLElement) || !(meta instanceof HTMLElement)) {
    return;
  }

  const visualHeight = visualStage.getBoundingClientRect().height;
  if (visualHeight > 0) {
    meta.style.height = `${visualHeight}px`;
  }
}

function syncRowHeights() {
  if (!(list instanceof HTMLElement)) {
    return;
  }

  if (rowResizeObserver) {
    rowResizeObserver.disconnect();
    rowResizeObserver = null;
  }

  rowResizeObserver = new ResizeObserver((entries) => {
    entries.forEach((entry) => {
      const row = entry.target.closest('.run-row');
      if (row instanceof HTMLElement) {
        applyRowHeight(row);
      }
    });
  });

  list.querySelectorAll('.run-row').forEach((row) => {
    if (!(row instanceof HTMLElement)) {
      return;
    }

    applyRowHeight(row);

    const visualStage = row.querySelector('.visual-stage');
    const image = row.querySelector('.visual-stage img');

    if (visualStage instanceof HTMLElement) {
      rowResizeObserver.observe(visualStage);
    }

    if (image instanceof HTMLImageElement) {
      if (image.complete) {
        applyRowHeight(row);
      } else {
        image.addEventListener('load', () => applyRowHeight(row), { once: true });
      }
    }
  });
}

function getRecordKey(record) {
  return [
    record?.id ?? '',
    record?.captured_at ?? '',
    record?.wins ?? '',
    record?.battle_count ?? '',
    record?.image_url ?? ''
  ].join('::');
}

async function loadOverlaySettings() {
  const response = await fetch('/api/overlay/crop-config', { cache: 'no-store' });
  if (!response.ok) {
    const message = (await response.text()).trim();
    throw new Error(message || `unexpected status ${response.status}`);
  }

  const payload = await response.json();
  return normalizeDisplayMode(payload?.display_mode);
}

async function refresh() {
  try {
    const settingsPromise = loadOverlaySettings().catch(() => currentDisplayMode);
    const endpoint = new URL('/api/records/list', window.location.origin);
    const response = await fetch(endpoint, { cache: 'no-store' });
    if (!response.ok) {
      const message = (await response.text()).trim();
      throw new Error(message || `unexpected status ${response.status}`);
    }
    const payload = await response.json();
    const records = Array.isArray(payload) ? payload.filter((r) => r && r.image_url) : [];

    const nextDisplayMode = await settingsPromise;
    const modeChanged = nextDisplayMode !== currentDisplayMode;
    currentDisplayMode = nextDisplayMode;

    if (records.length === 0) {
      lastRecordKey = null;
      renderEmpty();
      return;
    }

    const nextKey = records.map(getRecordKey).join('|');
    const updated = nextKey !== lastRecordKey;

    if (
      !updated &&
      !modeChanged &&
      root?.classList.contains('live') &&
      list &&
      !list.hidden
    ) {
      return;
    }

    lastRecordKey = nextKey;
    renderRecords(records, { updated });
  } catch (error) {
    if (lastRecordKey && list && !list.hidden) {
      setClassNames('stale');
      setText(signal, 'Connection lost, showing cached data');
      setText(status, 'Retrying');
      return;
    }

    renderEmpty(
      error instanceof Error && error.message
        ? error.message
        : 'Waiting for records captured after stream start.'
    );
  }
}

renderEmpty();
void refresh();
window.addEventListener('resize', syncRowHeights);
window.setInterval(refresh, 4000);

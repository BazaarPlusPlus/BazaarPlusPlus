<script lang="ts">
  import { locale, handleLocaleToggle } from '$lib/locale';

  export let kicker: string;
  export let subtitle: string;
  export let localeBadge: string;
  export let localeButtonLabel: string;
  export let bilibiliUrl: string;
  export let onOpenBilibili: (event?: MouseEvent) => void;
  export let updaterButtonLabel: string;
  export let updaterButtonTitle: string;
  export let updaterButtonDisabled = false;
  export let updaterButtonHighlighted = false;
  export let onOpenUpdater: () => void;
</script>

<header class="header">
  <div class="corner tl" aria-hidden="true">
    <svg width="28" height="28" viewBox="0 0 40 40" fill="none">
      <path
        d="M2 2L2 16M2 2L16 2"
        stroke="currentColor"
        stroke-width="1.5"
        stroke-linecap="square"
      />
      <circle cx="2" cy="2" r="1.5" fill="currentColor" />
    </svg>
  </div>
  <div class="corner tr" aria-hidden="true">
    <svg width="28" height="28" viewBox="0 0 40 40" fill="none">
      <path
        d="M38 2L38 16M38 2L24 2"
        stroke="currentColor"
        stroke-width="1.5"
        stroke-linecap="square"
      />
      <circle cx="38" cy="2" r="1.5" fill="currentColor" />
    </svg>
  </div>

  <div class="header-corner-links">
    <a
      class="about-toggle"
      href="/about"
      title={$locale === 'zh' ? '关于' : 'About'}
    >
      <svg class="about-icon" viewBox="0 0 24 24" aria-hidden="true">
        <circle
          cx="12"
          cy="12"
          r="9"
          stroke="currentColor"
          stroke-width="1.5"
          fill="none"
        />
        <path
          d="M12 11v5M12 8h.01"
          stroke="currentColor"
          stroke-width="1.5"
          stroke-linecap="round"
        />
      </svg>
    </a>

    <a
      class="about-toggle social-toggle"
      href={bilibiliUrl}
      rel="noreferrer"
      target="_blank"
      title={$locale === 'zh' ? '打开 Bilibili 主页' : 'Open Bilibili'}
      aria-label={$locale === 'zh' ? '打开 Bilibili 主页' : 'Open Bilibili'}
      onclick={onOpenBilibili}
    >
      <svg class="about-icon" viewBox="0 0 24 24" aria-hidden="true">
        <rect
          x="4.5"
          y="7.5"
          width="15"
          height="10"
          rx="2.2"
          stroke="currentColor"
          stroke-width="1.5"
          fill="none"
        />
        <path
          d="M9 5.5L7.4 3.8M15 5.5l1.6-1.7M9 11.2h1.8M13.2 11.2H15M9.3 14.1c1 .7 4.4.7 5.4 0"
          stroke="currentColor"
          stroke-width="1.5"
          stroke-linecap="round"
          stroke-linejoin="round"
          fill="none"
        />
      </svg>
    </a>

    <button
      class="about-toggle updater-toggle"
      class:is-highlighted={updaterButtonHighlighted}
      type="button"
      title={updaterButtonTitle}
      aria-label={updaterButtonTitle}
      disabled={updaterButtonDisabled}
      onclick={onOpenUpdater}
    >
      <svg class="about-icon" viewBox="0 0 24 24" aria-hidden="true">
        <path
          d="M12 4v9m0 0l-3.2-3.2M12 13l3.2-3.2M5.5 16.8h13"
          stroke="currentColor"
          stroke-width="1.5"
          stroke-linecap="round"
          stroke-linejoin="round"
          fill="none"
        />
      </svg>
      <span class="updater-label">{updaterButtonLabel}</span>
    </button>
  </div>

  <button
    class="locale-toggle"
    onclick={handleLocaleToggle}
    type="button"
    aria-label={localeButtonLabel}
    title={localeButtonLabel}
  >
    <svg class="locale-icon" viewBox="0 0 24 24" aria-hidden="true">
      <path
        d="M12 3a9 9 0 1 0 9 9a9 9 0 0 0-9-9Zm5.9 8h-2.2a14.3 14.3 0 0 0-1.2-4A7.1 7.1 0 0 1 17.9 11Zm-5.9-5.8c.7.9 1.7 2.9 2.1 5.8H9.9c.4-2.9 1.4-4.9 2.1-5.8ZM6.5 7a14.3 14.3 0 0 0-1.2 4H3.1A7.1 7.1 0 0 1 6.5 7ZM3.1 13h2.2a14.3 14.3 0 0 0 1.2 4A7.1 7.1 0 0 1 3.1 13Zm8.9 5.8c-.7-.9-1.7-2.9-2.1-5.8h4.2c-.4 2.9-1.4 4.9-2.1 5.8Zm2.5-1.8a14.3 14.3 0 0 0 1.2-4h2.2a7.1 7.1 0 0 1-3.4 4Zm-8.1-4c.1 1.4.4 2.7.9 4A12.4 12.4 0 0 1 5.9 13Zm.8-2a12.4 12.4 0 0 1 1.4-4c-.5 1.3-.8 2.6-.9 4Zm8.8 0c-.1-1.4-.4-2.7-.9-4a12.4 12.4 0 0 1 1.4 4Zm-1.4 2c.5-1.3.8-2.6.9-4a12.4 12.4 0 0 1-1.4 4Z"
        fill="currentColor"
      />
    </svg>
    <span class="locale-badge">{localeBadge}</span>
  </button>

  <div class="sigil" aria-hidden="true">
    <svg width="32" height="32" viewBox="0 0 44 44" fill="none">
      <polygon
        points="22,3 41,34 3,34"
        stroke="currentColor"
        stroke-width="1"
        fill="none"
        opacity="0.55"
      />
      <polygon
        points="22,11 35,31 9,31"
        stroke="currentColor"
        stroke-width="0.5"
        fill="none"
        opacity="0.3"
      />
      <circle
        cx="22"
        cy="22"
        r="5"
        stroke="currentColor"
        stroke-width="0.8"
        fill="none"
      />
      <circle cx="22" cy="22" r="2" fill="currentColor" opacity="0.75" />
    </svg>
  </div>

  <p class="kicker">{kicker}</p>
  <h1>BazaarPlusPlus</h1>
  <p class="subtitle"><em>{subtitle}</em></p>

  <div class="rule" aria-hidden="true">
    <span></span><span class="diamond">+</span><span></span>
  </div>
</header>

<style>
  .header {
    position: relative;
    text-align: center;
    padding: 1.45rem 1.75rem 1.15rem;
    background: linear-gradient(
      175deg,
      rgba(38, 23, 9, 0.92),
      rgba(16, 10, 5, 0.88)
    );
    border: 1px solid rgba(200, 148, 55, 0.18);
    border-radius: 3px;
    box-shadow:
      0 0 0 1px rgba(200, 148, 55, 0.06) inset,
      0 24px 64px rgba(0, 0, 0, 0.5);
    display: grid;
    gap: 0.15rem;
    justify-items: center;
  }

  .corner {
    position: absolute;
    color: rgba(200, 148, 55, 0.42);
  }
  .tl {
    top: 8px;
    left: 8px;
  }
  .tr {
    top: 8px;
    right: 8px;
  }

  .header-corner-links {
    position: absolute;
    top: 0.9rem;
    left: 0.9rem;
    display: flex;
    gap: 0.45rem;
    z-index: 2;
  }

  .about-toggle {
    width: 2rem;
    height: 2rem;
    display: inline-flex;
    align-items: center;
    justify-content: center;
    border: 1px solid rgba(200, 148, 55, 0.24);
    border-radius: 2px;
    background: linear-gradient(
      180deg,
      rgba(200, 148, 55, 0.12),
      rgba(200, 148, 55, 0.06)
    );
    color: rgba(228, 216, 191, 0.82);
    box-shadow: 0 0 0 1px rgba(255, 198, 98, 0.08) inset;
    text-decoration: none;
    transition:
      background 0.15s ease,
      border-color 0.15s ease;
  }

  .about-toggle:hover {
    background: linear-gradient(
      180deg,
      rgba(200, 148, 55, 0.2),
      rgba(200, 148, 55, 0.1)
    );
    border-color: rgba(200, 148, 55, 0.4);
  }

  .about-toggle:focus-visible {
    outline: 2px solid rgba(255, 214, 140, 0.9);
    outline-offset: 2px;
  }

  .about-icon {
    width: 1rem;
    height: 1rem;
    opacity: 0.9;
  }

  .social-toggle {
    padding: 0;
    cursor: pointer;
  }

  .updater-toggle {
    width: auto;
    min-width: 2rem;
    padding: 0 0.45rem;
    gap: 0.32rem;
    cursor: pointer;
    font-family: 'Cinzel', serif;
    font-size: 0.5rem;
    letter-spacing: 0.12em;
    text-transform: uppercase;
  }

  .updater-toggle.is-highlighted {
    color: #f3d38d;
    border-color: rgba(232, 200, 122, 0.68);
    background: linear-gradient(
      180deg,
      rgba(232, 200, 122, 0.24),
      rgba(200, 148, 55, 0.12)
    );
    box-shadow:
      0 0 0 1px rgba(255, 214, 140, 0.14) inset,
      0 0 18px rgba(232, 200, 122, 0.12);
  }

  .updater-toggle:disabled {
    opacity: 0.55;
    cursor: wait;
  }

  .updater-label {
    white-space: nowrap;
  }

  .locale-toggle {
    position: absolute;
    top: 0.9rem;
    right: 0.9rem;
    min-width: 3.2rem;
    height: 2rem;
    padding: 0.3rem 0.55rem;
    border: 1px solid rgba(200, 148, 55, 0.24);
    border-radius: 2px;
    background: linear-gradient(
      180deg,
      rgba(200, 148, 55, 0.12),
      rgba(200, 148, 55, 0.06)
    );
    color: rgba(228, 216, 191, 0.82);
    font-family: 'Cinzel', serif;
    font-size: 0.54rem;
    letter-spacing: 0.14em;
    text-transform: uppercase;
    display: inline-flex;
    align-items: center;
    justify-content: center;
    gap: 0.35rem;
    box-shadow: 0 0 0 1px rgba(255, 198, 98, 0.08) inset;
    z-index: 2;
  }

  .locale-toggle:hover {
    background: linear-gradient(
      180deg,
      rgba(200, 148, 55, 0.2),
      rgba(200, 148, 55, 0.1)
    );
    border-color: rgba(200, 148, 55, 0.4);
  }

  .locale-toggle:focus-visible {
    outline: 2px solid rgba(255, 214, 140, 0.9);
    outline-offset: 2px;
  }

  .locale-icon {
    width: 0.9rem;
    height: 0.9rem;
    flex-shrink: 0;
    opacity: 0.9;
  }

  .locale-badge {
    min-width: 1.1rem;
    text-align: center;
    font-family: 'Fira Code', monospace;
    font-size: 0.62rem;
    letter-spacing: 0.05em;
  }

  .sigil {
    color: rgba(205, 150, 60, 0.65);
    margin-bottom: 0.2rem;
    animation: slow-spin 45s linear infinite;
    filter: drop-shadow(0 0 7px rgba(205, 150, 60, 0.22));
  }

  @keyframes slow-spin {
    from {
      transform: rotate(0deg);
    }
    to {
      transform: rotate(360deg);
    }
  }

  .kicker {
    margin: 0;
    font-family: 'Cinzel', serif;
    font-size: 0.5rem;
    letter-spacing: 0.38em;
    text-transform: uppercase;
    color: rgba(205, 150, 60, 0.55);
  }

  h1 {
    margin: 0.1rem 0 0;
    font-family: 'Cinzel Decorative', serif;
    font-size: clamp(1.35rem, 4.2vw, 2.1rem);
    font-weight: 700;
    line-height: 1;
    background: linear-gradient(155deg, #e8c87a 0%, #bf852e 55%, #e8c87a 100%);
    -webkit-background-clip: text;
    background-clip: text;
    -webkit-text-fill-color: transparent;
    filter: drop-shadow(0 2px 10px rgba(205, 150, 60, 0.28));
  }

  .subtitle {
    margin: 0.22rem 0 0.5rem;
    font-family: 'IM Fell English', serif;
    font-style: italic;
    font-size: 0.78rem;
    color: rgba(200, 170, 120, 0.55);
  }

  .rule {
    width: 100%;
    display: flex;
    align-items: center;
    gap: 0.65rem;
    color: rgba(200, 148, 55, 0.35);
  }

  .rule span:first-child,
  .rule span:last-child {
    flex: 1;
    height: 1px;
    background: linear-gradient(
      90deg,
      transparent,
      rgba(200, 148, 55, 0.3) 40%,
      rgba(200, 148, 55, 0.3) 60%,
      transparent
    );
  }

  .diamond {
    font-size: 0.55rem;
    color: rgba(205, 150, 60, 0.55);
  }

  @media (max-width: 520px) {
    .header {
      padding: 1.2rem 1rem 1rem;
    }
    .locale-toggle {
      top: 0.7rem;
      right: 0.7rem;
    }
    .header-corner-links {
      top: 0.7rem;
      left: 0.7rem;
    }
  }
</style>

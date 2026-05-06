<script lang="ts">
  import { locale } from '$lib/locale';
  import LocaleToggle from '$lib/components/LocaleToggle.svelte';

  export let kicker: string;
  export let subtitle: string;
  export let localeBadge: string;
  export let localeButtonLabel: string;
  export let updaterButtonLabel: string;
  export let updaterButtonTitle: string;
  export let updaterButtonDisabled = false;
  export let updaterButtonHighlighted = false;
  export let onOpenUpdater: () => void;
  export let streamModeActive = false;
  export let streamModeLabel = $locale === 'zh' ? '直播模式' : 'Stream Mode';
  export let onToggleStreamMode: () => void;
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

    <button
      class="about-toggle stream-toggle"
      class:is-active={streamModeActive}
      type="button"
      title={streamModeLabel}
      aria-pressed={streamModeActive}
      onclick={onToggleStreamMode}
    >
      <span class="updater-label">{streamModeLabel}</span>
    </button>
  </div>

  <LocaleToggle label={localeButtonLabel} badge={localeBadge} />

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
    padding: 0.95rem 1.35rem 0.85rem;
    background: linear-gradient(
      175deg,
      rgba(36, 22, 9, 0.9),
      rgba(15, 9, 5, 0.86)
    );
    border: 1px solid rgba(var(--color-accent-rgb), 0.18);
    border-radius: 3px;
    box-shadow:
      0 0 0 1px rgba(var(--color-accent-rgb), 0.06) inset,
      0 16px 42px rgba(0, 0, 0, 0.42);
    display: grid;
    gap: 0.28rem;
    justify-items: center;
  }

  .corner {
    position: absolute;
    color: rgba(var(--color-accent-rgb), 0.42);
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
    top: 0.72rem;
    left: 0.72rem;
    display: flex;
    gap: 0.38rem;
    z-index: 2;
  }

  .about-toggle {
    min-width: 1.9rem;
    height: 1.9rem;
    display: inline-flex;
    align-items: center;
    justify-content: center;
    border: 1px solid rgba(var(--color-accent-rgb), 0.24);
    border-radius: 2px;
    background: linear-gradient(
      180deg,
      rgba(var(--color-accent-rgb), 0.12),
      rgba(var(--color-accent-rgb), 0.06)
    );
    color: rgba(var(--color-cream-rgb), 0.82);
    box-shadow: 0 0 0 1px rgba(var(--color-warm-bright-rgb), 0.08) inset;
    text-decoration: none;
    transition:
      background 0.15s ease,
      border-color 0.15s ease;
  }

  .about-toggle:hover {
    background: linear-gradient(
      180deg,
      rgba(var(--color-accent-rgb), 0.2),
      rgba(var(--color-accent-rgb), 0.1)
    );
    border-color: rgba(var(--color-accent-rgb), 0.4);
  }

  .about-toggle:focus-visible {
    outline: 2px solid rgba(var(--color-warm-rgb), 0.9);
    outline-offset: 2px;
  }

  .about-icon {
    width: 1rem;
    height: 1rem;
    opacity: 0.9;
  }
  .updater-toggle,
  .stream-toggle {
    width: auto;
    min-width: 1.9rem;
    padding: 0 0.42rem;
    gap: 0.28rem;
    cursor: pointer;
    font-family: 'Cinzel', serif;
    font-size: 0.47rem;
    letter-spacing: 0.1em;
    text-transform: uppercase;
    white-space: nowrap;
  }

  .updater-toggle.is-highlighted {
    color: var(--color-soft-gold);
    border-color: rgba(232, 200, 122, 0.68);
    background: linear-gradient(
      180deg,
      rgba(232, 200, 122, 0.24),
      rgba(var(--color-accent-rgb), 0.12)
    );
    box-shadow:
      0 0 0 1px rgba(var(--color-warm-rgb), 0.14) inset,
      0 0 18px rgba(232, 200, 122, 0.12);
  }

  .stream-toggle.is-active {
    color: var(--color-soft-gold);
    border-color: rgba(232, 200, 122, 0.52);
    background: linear-gradient(
      180deg,
      rgba(232, 200, 122, 0.18),
      rgba(var(--color-accent-rgb), 0.1)
    );
    box-shadow:
      0 0 0 1px rgba(var(--color-warm-rgb), 0.1) inset,
      0 0 12px rgba(232, 200, 122, 0.08);
  }

  .updater-toggle:disabled {
    opacity: 0.55;
    cursor: wait;
  }

  .updater-label {
    display: inline-flex;
    align-items: center;
    height: 100%;
    white-space: nowrap;
  }

  .sigil {
    color: rgba(205, 150, 60, 0.65);
    margin-top: 0.1rem;
    margin-bottom: 0.05rem;
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
    font-size: 0.46rem;
    letter-spacing: 0.3em;
    text-transform: uppercase;
    color: rgba(205, 150, 60, 0.55);
  }

  h1 {
    margin: 0;
    font-family: 'Cinzel Decorative', serif;
    font-size: clamp(1.2rem, 3.6vw, 1.86rem);
    font-weight: 700;
    line-height: 1;
    background: linear-gradient(155deg, var(--color-gold-text) 0%, var(--color-gold-deep) 55%, var(--color-gold-text) 100%);
    -webkit-background-clip: text;
    background-clip: text;
    -webkit-text-fill-color: transparent;
    filter: drop-shadow(0 2px 10px rgba(205, 150, 60, 0.28));
  }

  .subtitle {
    margin: 0.08rem 0 0.32rem;
    font-family: 'IM Fell English', serif;
    font-style: italic;
    font-size: 0.7rem;
    color: rgba(var(--color-muted-gold-rgb), 0.58);
  }

  .rule {
    width: 100%;
    display: flex;
    align-items: center;
    gap: 0.65rem;
    color: rgba(var(--color-accent-rgb), 0.35);
  }

  .rule span:first-child,
  .rule span:last-child {
    flex: 1;
    height: 1px;
    background: linear-gradient(
      90deg,
      transparent,
      rgba(var(--color-accent-rgb), 0.3) 40%,
      rgba(var(--color-accent-rgb), 0.3) 60%,
      transparent
    );
  }

  .diamond {
    font-size: 0.55rem;
    color: rgba(205, 150, 60, 0.55);
  }

  @media (max-width: 640px) {
    .header {
      padding: 0.88rem 0.92rem 0.8rem;
    }

    .header-corner-links {
      position: static;
      width: 100%;
      justify-content: flex-start;
      margin-bottom: 0.55rem;
      padding-right: 3.3rem;
      flex-wrap: wrap;
    }
  }
</style>

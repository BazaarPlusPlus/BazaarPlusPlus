<script lang="ts">
  import { getVersion } from '@tauri-apps/api/app';
  import { onMount } from 'svelte';
  import AppModal from '$lib/components/AppModal.svelte';
  import { formatMessage, messages } from '$lib/i18n';
  import { locale, handleLocaleToggle } from '$lib/locale';
  import SupporterListModal from '$lib/components/supporters/SupporterListModal.svelte';

  let appVersion = '0.0.0';
  let showPaymentCodes = false;
  let showSupporterList = false;
  let hiddenPaymentImages: Record<string, boolean> = {};

  $: t = (
    key: keyof typeof messages.en,
    params?: Record<string, string | number>
  ): string => formatMessage($locale, key, params);

  $: localeBadge = $locale === 'zh' ? '中' : 'EN';
  $: localeButtonLabel = $locale === 'zh' ? 'Switch to English' : '切换到中文';

  const paymentMethods = [
    {
      id: 'wechat',
      zhName: '微信收款码',
      enName: 'WePay',
      src: '/support/wechat-pay.svg',
      accent: 'payment-card-wechat'
    }
  ];

  const inspiredBy = [
    { name: 'BazaarHelper', url: 'https://github.com/Duangi/BazaarHelper' },
    {
      name: 'BazaarPlannerMod',
      url: 'https://github.com/oceanseth/BazaarPlannerMod'
    }
  ];

  const dataSources = [{ name: 'BazaarDB', url: 'https://bazaardb.gg' }];

  const projectDeps = [
    {
      name: 'BepInEx',
      license: 'LGPL-2.1',
      url: 'https://github.com/BepInEx/BepInEx'
    }
  ];

  const frontendDeps = [
    { name: 'Svelte', license: 'MIT', url: 'https://svelte.dev' },
    { name: 'SvelteKit', license: 'MIT', url: 'https://kit.svelte.dev' },
    { name: 'Vite', license: 'MIT', url: 'https://vitejs.dev' },
    { name: 'Tauri', license: 'MIT / Apache-2.0', url: 'https://tauri.app' }
  ];

  const rustDeps = [
    { name: 'serde', license: 'MIT / Apache-2.0', url: 'https://serde.rs' },
    {
      name: 'reqwest',
      license: 'MIT / Apache-2.0',
      url: 'https://github.com/seanmonstar/reqwest'
    },
    { name: 'zip', license: 'MIT', url: 'https://github.com/zip-rs/zip2' },
    {
      name: 'dirs',
      license: 'MIT / Apache-2.0',
      url: 'https://github.com/dirs-dev/dirs-rs'
    },
    {
      name: 'keyvalues-parser',
      license: 'MIT',
      url: 'https://github.com/CosmicHorrorDev/keyvalues-rs'
    },
    {
      name: 'winreg',
      license: 'MIT',
      url: 'https://github.com/gentoo90/winreg-rs'
    }
  ];

  locale.init();

  onMount(() => {
    void loadAppVersion();
  });

  async function loadAppVersion() {
    try {
      const version = await getVersion();
      appVersion = version?.trim() ? version : '0.0.0';
    } catch {
      appVersion = '0.0.0';
    }
  }

  function openPaymentCodes() {
    hiddenPaymentImages = {};
    showPaymentCodes = true;
  }

  function closePaymentCodes() {
    showPaymentCodes = false;
  }

  function openSupporterList() {
    showSupporterList = true;
  }

  function closeSupporterList() {
    showSupporterList = false;
  }

  function handlePaymentImageError(methodId: string) {
    hiddenPaymentImages = {
      ...hiddenPaymentImages,
      [methodId]: true
    };
  }
</script>

<svelte:head>
  <title>{t('aboutTitle')} - BazaarPlusPlus</title>
</svelte:head>

<AppModal
  open={showPaymentCodes}
  eyebrow="BazaarPlusPlus"
  title={$locale === 'zh' ? '支持项目' : 'Support the Project'}
  bodyClass="payment-modal-body"
  confirmText={$locale === 'zh' ? '关闭' : 'Close'}
  onConfirm={closePaymentCodes}
>
  <section class="payment-modal-shell">
    <div class="payment-grid">
      {#each paymentMethods as method}
        <article class={`payment-card ${method.accent}`}>
          <div class="payment-frame">
            {#if !hiddenPaymentImages[method.id]}
              <img
                class="payment-image"
                src={method.src}
                alt={$locale === 'zh' ? method.zhName : method.enName}
                onerror={() => handlePaymentImageError(method.id)}
              />
            {:else}
              <div class="payment-placeholder" aria-hidden="true"></div>
            {/if}
          </div>

          <div class="payment-copy">
            <h3>{$locale === 'zh' ? '支持项目' : 'Support the Project'}</h3>
            <p>
              {$locale === 'zh'
                ? '请 Bazaar++ 喝一杯'
                : 'Buy Bazaar++ a drink.'}
            </p>
          </div>
        </article>
      {/each}
    </div>

    <p class="payment-support-note">
      {$locale === 'zh'
        ? '有你支持，Bazaar++ 会冒出更多好东西'
        : 'With your support, Bazaar++ gets to grow more good stuff.'}
    </p>
    <p class="payment-support-tip">
      {$locale === 'zh'
        ? '如果愿意，欢迎在备注里留一个支持者 ID'
        : 'If you want, you can leave a supporter ID in the payment note.'}
    </p>
  </section>
</AppModal>

<SupporterListModal open={showSupporterList} onClose={closeSupporterList} />

<main class="shell">
  <header class="header">
    <a class="back-btn" href="/">
      <svg class="back-icon" viewBox="0 0 24 24" aria-hidden="true">
        <path
          d="M15 18l-6-6 6-6"
          stroke="currentColor"
          stroke-width="1.5"
          stroke-linecap="round"
          stroke-linejoin="round"
          fill="none"
        />
      </svg>
      {t('aboutBack')}
    </a>

    <button
      class="locale-toggle"
      onclick={handleLocaleToggle}
      type="button"
      aria-label={localeButtonLabel}
      title={localeButtonLabel}
    >
      <svg class="locale-icon" viewBox="0 0 24 24" aria-hidden="true">
        <path
          d="M12 3a9 9 0 1 0 9 9a9 9 0 0 0-9-9Zm5.9 8h-2.2a14.3 14.3 0 0 0-1.2-4A7.1 7.1 0 0 1 17.9 11Zm-5.9-5.8c.7.9 1.7 2.9 2.1 5.8H9.9c.4-2.9 1.4-4.9 2.1-5.8ZM6.5 7a14.3 14.3 0 0 0-1.2 4H3.1A7.1 7.1 0 0 1 6.5 7ZM3.1 13h2.2a14.3 14.3 0 0 0 1.2 4A7.1 7.1 0 0 1 3.1 13Zm8.9 5.8c-.7-.9-1.7-2.9-2.1-5.8h4.2c-.4 2.9-1.4 4.9-2.1 5.8Zm2.5-1.8a14.3 14.3 0 0 0 1.2-4h2.2a7.1 7.1 0 0 1-3.4 4Z"
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

    <h1>{t('aboutTitle')}</h1>

    <div class="rule" aria-hidden="true">
      <span></span><span class="diamond">+</span><span></span>
    </div>
  </header>

  <section class="card">
    <h2 class="section-title">{t('aboutInfo')}</h2>
    <div class="info-card-row">
      <div class="info-summary">
        <p class="info-line info-line-compact">
          <a
            class="info-link"
            href="https://github.com/cauyxy/BazaarPlusPlus"
            target="_blank"
            rel="noopener noreferrer"
          >
            BazaarPlusPlus
          </a>
          <span>Installer</span>
        </p>
        <p class="info-meta-line">
          <span class="version-label">Version</span>
          <span class="tag-version">v{appVersion}</span>
          <span class="info-divider" aria-hidden="true"></span>
          <span class="version-label">License</span>
          <span class="info-muted">MIT</span>
        </p>
      </div>
      <button class="supporter-entry" type="button" onclick={openSupporterList}>
        <span class="supporter-entry-title"
          >{$locale === 'zh' ? '支持者名单' : 'Supporters'}</span
        >
        <span class="supporter-entry-subtitle"
          >{$locale === 'zh' ? '查看名单' : 'Open list'}</span
        >
      </button>
    </div>
  </section>

  <section class="card">
    <h2 class="section-title">{t('aboutAuthors')}</h2>
    <ul class="dep-list">
      <li>
        <a
          class="dep-item dep-item-link"
          href="https://github.com/cauyxy"
          target="_blank"
          rel="noopener noreferrer"
        >
          <span class="dep-name">cauyxy</span>
          <span class="dep-role">{t('aboutAuthorRole')}</span>
        </a>
      </li>
      <li>
        <a
          class="dep-item dep-item-link"
          href="https://openai.com/codex"
          target="_blank"
          rel="noopener noreferrer"
        >
          <span class="dep-name">Codex</span>
          <span class="dep-role">{t('aboutCocreatorRole')}</span>
        </a>
      </li>
      <li>
        <a
          class="dep-item dep-item-link"
          href="https://claude.com/product/claude-code"
          target="_blank"
          rel="noopener noreferrer"
        >
          <span class="dep-name">Claude Code</span>
          <span class="dep-role">{t('aboutCocreatorRole')}</span>
        </a>
      </li>
    </ul>
  </section>

  <section class="card support-section">
    <h2 class="section-title">{t('aboutSupport')}</h2>
    <ul class="dep-list">
      <li>
        <button
          class="dep-item dep-item-link payment-launch"
          type="button"
          onclick={openPaymentCodes}
        >
          <span class="dep-name">{$locale === 'zh' ? '微信' : 'Wepay'}</span>
          <span class="dep-link-label"
            >{$locale === 'zh' ? '感谢支持' : 'Support'}</span
          >
        </button>
      </li>
      <li>
        <a
          class="dep-item dep-item-link"
          href="https://ko-fi.com/cauyxy"
          target="_blank"
          rel="noopener noreferrer"
        >
          <span class="dep-name">Ko-fi</span>
          <span class="dep-link-label">ko-fi.com/cauyxy</span>
        </a>
      </li>
    </ul>
  </section>

  <section class="card">
    <h2 class="section-title">{t('aboutInspiredBy')}</h2>
    <ul class="dep-list">
      {#each inspiredBy as item}
        <li class="dep-item">
          <span class="dep-name">{item.name}</span>
          <a
            class="dep-link"
            href={item.url}
            target="_blank"
            rel="noopener noreferrer"
          >
            {item.url.replace('https://github.com/', '')}
          </a>
        </li>
      {/each}
    </ul>
  </section>

  <section class="card">
    <h2 class="section-title">{t('aboutDataSources')}</h2>
    <ul class="dep-list">
      {#each dataSources as src}
        <li class="dep-item">
          <span class="dep-name">{src.name}</span>
          <a
            class="dep-link"
            href={src.url}
            target="_blank"
            rel="noopener noreferrer"
          >
            {src.url.replace('https://', '')}
          </a>
        </li>
      {/each}
    </ul>
  </section>

  <section class="card">
    <h2 class="section-title">{t('aboutDependencies')}</h2>
    <ul class="dep-list">
      {#each projectDeps as dep}
        <li class="dep-item">
          <span class="dep-name">{dep.name}</span>
          <span class="dep-license">{dep.license}</span>
        </li>
      {/each}
    </ul>
  </section>

  <section class="card">
    <h2 class="section-title">{t('aboutOpenSource')}</h2>

    <h3 class="group-title">Frontend</h3>
    <ul class="dep-list">
      {#each frontendDeps as dep}
        <li class="dep-item">
          <span class="dep-name">{dep.name}</span>
          <span class="dep-license">{dep.license}</span>
        </li>
      {/each}
    </ul>

    <h3 class="group-title">Rust / Backend</h3>
    <ul class="dep-list">
      {#each rustDeps as dep}
        <li class="dep-item">
          <span class="dep-name">{dep.name}</span>
          <span class="dep-license">{dep.license}</span>
        </li>
      {/each}
    </ul>
  </section>

  <footer class="footer" aria-hidden="true">
    <div class="rule">
      <span></span><span class="diamond small">+</span><span></span>
    </div>
    <p>{t('footer')}</p>
  </footer>
</main>

<style>
  .shell {
    width: 100%;
    max-width: 560px;
    margin: 0 auto;
    padding: 1.25rem 1rem 1.75rem;
    display: grid;
    gap: 0.85rem;
    animation: fade-up 0.5s ease both;
  }

  @keyframes fade-up {
    from {
      opacity: 0;
      transform: translateY(14px);
    }
    to {
      opacity: 1;
      transform: translateY(0);
    }
  }

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

  .back-btn {
    position: absolute;
    top: 0.9rem;
    left: 0.9rem;
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
    gap: 0.3rem;
    box-shadow: 0 0 0 1px rgba(255, 198, 98, 0.08) inset;
    z-index: 2;
    text-decoration: none;
    transition:
      background 0.15s ease,
      border-color 0.15s ease;
  }

  .back-btn:hover {
    background: linear-gradient(
      180deg,
      rgba(200, 148, 55, 0.2),
      rgba(200, 148, 55, 0.1)
    );
    border-color: rgba(200, 148, 55, 0.4);
  }

  .back-btn:focus-visible {
    outline: 2px solid rgba(255, 214, 140, 0.9);
    outline-offset: 2px;
  }

  .back-icon {
    width: 0.9rem;
    height: 0.9rem;
    flex-shrink: 0;
    opacity: 0.9;
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
    cursor: pointer;
    border-style: solid;
    font: inherit;
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
  .diamond.small {
    font-size: 0.42rem;
  }

  .card {
    padding: 0.95rem 1.05rem;
    background: rgba(18, 11, 5, 0.88);
    border: 1px solid rgba(180, 130, 48, 0.13);
    border-radius: 3px;
    box-shadow: 0 6px 28px rgba(0, 0, 0, 0.35);
    display: grid;
    gap: 0.6rem;
  }

  .section-title {
    margin: 0;
    font-family: 'Cinzel', serif;
    font-size: 0.72rem;
    letter-spacing: 0.12em;
    text-transform: uppercase;
    color: rgba(220, 195, 145, 0.8);
  }

  .group-title {
    margin: 0.4rem 0 0;
    font-family: 'Cinzel', serif;
    font-size: 0.6rem;
    letter-spacing: 0.1em;
    text-transform: uppercase;
    color: rgba(200, 148, 55, 0.55);
  }

  .info-line {
    margin: 0;
    font-family: 'Fira Code', monospace;
    font-size: 0.78rem;
    color: rgba(228, 216, 191, 0.82);
    display: flex;
    align-items: center;
    gap: 0.6rem;
  }

  .info-line-compact {
    flex-wrap: wrap;
    gap: 0.5rem;
  }

  .info-link {
    color: rgba(232, 200, 122, 0.92);
    text-decoration: none;
    transition: color 0.15s ease;
  }

  .info-link:hover {
    color: rgba(244, 220, 162, 0.98);
  }

  .info-link:focus-visible {
    outline: 2px solid rgba(255, 214, 140, 0.9);
    outline-offset: 2px;
  }

  .info-card-row {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 1rem;
  }

  .info-summary {
    display: grid;
    gap: 0.65rem;
    min-width: 0;
    flex: 1;
  }

  .info-meta-line {
    margin: 0;
    display: flex;
    align-items: center;
    gap: 0.55rem;
    flex-wrap: wrap;
  }

  .info-dot {
    width: 0.22rem;
    height: 0.22rem;
    border-radius: 999px;
    background: rgba(200, 170, 120, 0.38);
    flex-shrink: 0;
  }

  .info-divider {
    width: 1px;
    height: 0.9rem;
    background: linear-gradient(
      180deg,
      transparent,
      rgba(200, 170, 120, 0.45) 20%,
      rgba(200, 170, 120, 0.45) 80%,
      transparent
    );
    flex-shrink: 0;
  }

  .version-label {
    font-family: 'Cinzel', serif;
    font-size: 0.6rem;
    letter-spacing: 0.08em;
    text-transform: uppercase;
    color: rgba(200, 170, 120, 0.5);
  }

  .tag-version {
    font-size: 0.65rem;
    padding: 0.15rem 0.45rem;
    border-radius: 2px;
    background: rgba(80, 180, 120, 0.15);
    color: #6dd9a0;
    border: 1px solid rgba(80, 180, 120, 0.25);
  }

  .info-muted {
    margin: 0;
    font-size: 0.78rem;
    color: rgba(200, 170, 120, 0.5);
  }

  .dep-list {
    margin: 0;
    padding: 0;
    list-style: none;
    display: grid;
    gap: 0.25rem;
  }

  .dep-item {
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: 0.45rem 0.6rem;
    border-radius: 2px;
    background: rgba(200, 148, 55, 0.04);
    border: 1px solid rgba(180, 130, 48, 0.08);
    transition: background 0.15s ease;
  }

  .dep-item:hover {
    background: rgba(200, 148, 55, 0.08);
  }

  .dep-name {
    font-family: 'Fira Code', monospace;
    font-size: 0.75rem;
    color: rgba(228, 216, 191, 0.85);
  }

  .dep-license {
    font-family: 'Fira Code', monospace;
    font-size: 0.62rem;
    color: rgba(200, 170, 120, 0.45);
    flex-shrink: 0;
  }

  .dep-item-link {
    text-decoration: none;
    color: inherit;
    cursor: pointer;
  }

  .supporter-entry {
    min-width: 9.4rem;
    padding: 0.6rem 0.75rem;
    border-radius: 3px;
    border: 1px solid rgba(200, 148, 55, 0.2);
    background:
      radial-gradient(
        circle at top,
        rgba(255, 224, 150, 0.08),
        transparent 58%
      ),
      linear-gradient(180deg, rgba(34, 20, 8, 0.92), rgba(18, 10, 5, 0.94));
    color: inherit;
    display: grid;
    gap: 0.15rem;
    justify-items: center;
    box-shadow: inset 0 0 0 1px rgba(255, 214, 140, 0.04);
    cursor: pointer;
    transition:
      background 0.15s ease,
      border-color 0.15s ease,
      transform 0.15s ease;
    align-self: stretch;
    align-content: center;
  }

  .supporter-entry:hover {
    border-color: rgba(220, 170, 80, 0.34);
    background:
      radial-gradient(
        circle at top,
        rgba(255, 224, 150, 0.12),
        transparent 58%
      ),
      linear-gradient(180deg, rgba(40, 24, 10, 0.94), rgba(20, 12, 6, 0.96));
    transform: translateY(-1px);
  }

  .supporter-entry-title {
    font-family: 'Cinzel', serif;
    font-size: 0.62rem;
    letter-spacing: 0.12em;
    text-transform: uppercase;
    color: rgba(232, 200, 122, 0.88);
  }

  .supporter-entry-subtitle {
    font-family: 'Fira Code', monospace;
    font-size: 0.62rem;
    color: rgba(214, 190, 146, 0.72);
  }

  .payment-launch {
    width: 100%;
    text-align: left;
  }

  .dep-item-link:hover .dep-name {
    color: rgba(220, 180, 100, 0.95);
  }

  .dep-link-label {
    font-family: 'Fira Code', monospace;
    font-size: 0.62rem;
    color: rgba(200, 170, 120, 0.55);
    flex-shrink: 0;
  }

  .dep-role {
    font-family: 'Cinzel', serif;
    font-size: 0.56rem;
    letter-spacing: 0.1em;
    text-transform: uppercase;
    color: rgba(200, 170, 120, 0.45);
    flex-shrink: 0;
  }

  .dep-link {
    font-family: 'Fira Code', monospace;
    font-size: 0.62rem;
    color: rgba(200, 170, 120, 0.55);
    text-decoration: none;
    flex-shrink: 0;
    transition: color 0.15s ease;
  }

  .dep-link:hover {
    color: rgba(220, 180, 100, 0.85);
  }

  .payment-modal-body {
    padding-top: 0.1rem;
  }

  .payment-modal-shell {
    display: grid;
    gap: 0.9rem;
    text-align: left;
  }

  .payment-grid {
    display: grid;
    grid-template-columns: minmax(0, 260px);
    justify-content: center;
    gap: 0.8rem;
  }

  .payment-support-note {
    margin: -0.1rem 0 0;
    text-align: center;
    font-size: 0.76rem;
    line-height: 1.6;
    color: rgba(214, 190, 146, 0.76);
  }

  .payment-support-tip {
    margin: -0.2rem auto 0;
    max-width: 28rem;
    text-align: center;
    font-size: 0.72rem;
    line-height: 1.65;
    color: rgba(240, 220, 184, 0.82);
  }

  .payment-card {
    position: relative;
    padding: 0.75rem;
    background:
      radial-gradient(
        circle at top,
        rgba(255, 232, 174, 0.08),
        transparent 54%
      ),
      linear-gradient(180deg, rgba(34, 20, 8, 0.96), rgba(16, 9, 4, 0.98));
    border: 1px solid rgba(200, 148, 55, 0.16);
    border-radius: 4px;
    display: grid;
    gap: 0.65rem;
    box-shadow: inset 0 0 0 1px rgba(255, 198, 98, 0.05);
  }

  .payment-card::after {
    content: '';
    position: absolute;
    inset: 0.45rem;
    border: 1px solid rgba(255, 220, 155, 0.05);
    border-radius: 2px;
    pointer-events: none;
  }

  .payment-card-wechat {
    box-shadow:
      inset 0 0 0 1px rgba(255, 198, 98, 0.05),
      0 10px 32px rgba(42, 110, 78, 0.14);
  }

  .payment-frame {
    aspect-ratio: 1 / 1;
    padding: 0.8rem;
    background: linear-gradient(
      135deg,
      rgba(255, 248, 231, 0.98),
      rgba(245, 238, 220, 0.98)
    );
    border-radius: 3px;
    box-shadow:
      inset 0 0 0 1px rgba(95, 65, 19, 0.08),
      0 10px 24px rgba(0, 0, 0, 0.22);
  }

  .payment-image,
  .payment-placeholder {
    width: 100%;
    height: 100%;
    border-radius: 2px;
  }

  .payment-image {
    display: block;
    object-fit: contain;
    background: #fff;
  }

  .payment-placeholder {
    background:
      linear-gradient(90deg, rgba(0, 0, 0, 0.05) 1px, transparent 1px),
      linear-gradient(rgba(0, 0, 0, 0.05) 1px, transparent 1px),
      radial-gradient(
        circle at center,
        rgba(0, 0, 0, 0.08),
        rgba(255, 255, 255, 0.94) 62%
      );
    background-size:
      16px 16px,
      16px 16px,
      cover;
    border: 1px dashed rgba(96, 74, 29, 0.28);
  }

  .payment-copy {
    display: grid;
    gap: 0.18rem;
    text-align: center;
  }

  .payment-copy h3 {
    margin: 0;
    font-family: 'Cinzel', serif;
    font-size: 0.82rem;
    letter-spacing: 0.04em;
    color: rgba(238, 220, 182, 0.94);
  }

  .payment-copy p {
    margin: 0;
    font-size: 0.66rem;
    line-height: 1.45;
    color: rgba(200, 170, 120, 0.8);
  }

  .footer {
    text-align: center;
    display: grid;
    gap: 0.4rem;
  }

  .footer p {
    margin: 0;
    font-family: 'Cinzel', serif;
    font-size: 0.5rem;
    letter-spacing: 0.3em;
    text-transform: uppercase;
    color: rgba(140, 110, 60, 0.35);
  }

  button {
    cursor: pointer;
    border: none;
    font: inherit;
  }

  button:focus-visible,
  .dep-item-link:focus-visible,
  .dep-link:focus-visible {
    outline: 2px solid rgba(255, 214, 140, 0.9);
    outline-offset: 2px;
  }

  @media (max-width: 520px) {
    .shell {
      padding: 1rem 0.85rem 1.5rem;
    }
    .header {
      padding: 1.2rem 1rem 1rem;
    }

    .info-card-row {
      flex-direction: column;
      align-items: stretch;
    }

    .supporter-entry {
      width: 100%;
      min-width: 0;
    }

    .back-btn {
      top: 0.7rem;
      left: 0.7rem;
    }

    .locale-toggle {
      top: 0.7rem;
      right: 0.7rem;
    }

    .payment-grid {
      grid-template-columns: 1fr;
    }
  }
</style>

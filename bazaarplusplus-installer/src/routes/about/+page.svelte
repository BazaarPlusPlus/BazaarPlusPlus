<script lang="ts">
  import { getVersion } from '@tauri-apps/api/app';
  import { openUrl } from '@tauri-apps/plugin-opener';
  import { onMount } from 'svelte';
  import LocaleToggle from '$lib/components/LocaleToggle.svelte';
  import PaymentCodeModal from '$lib/components/supporters/PaymentCodeModal.svelte';
  import { formatMessage, messages } from '$lib/i18n';
  import { locale } from '$lib/locale';
  import { hasTauriRuntime } from '$lib/installer/runtime';
  import {
    authors,
    dataSources,
    frontendDependencies,
    inspiredBy,
    paymentMethods,
    projectDependencies,
    rustDependencies,
    supportLinks
  } from '$lib/about/content';
  import {
    createAboutPageModel,
    type AboutPageModel
  } from '$lib/about/page-model';

  const SUPPORTERS_URL_EN = 'https://bazaarplusplus.com/support?lang=en';
  const SUPPORTERS_URL_ZH = 'https://bazaarplusplus.com/support';

  let appVersion = '0.0.0';
  let showPaymentCodes = false;
  let pageModel: AboutPageModel = createAboutPageModel('en');

  function t(
    key: keyof typeof messages.en,
    params?: Record<string, string | number>
  ): string {
    return formatMessage($locale, key, params);
  }

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
    showPaymentCodes = true;
  }

  function closePaymentCodes() {
    showPaymentCodes = false;
  }

  async function openSupporterList() {
    const url = $locale === 'zh' ? SUPPORTERS_URL_ZH : SUPPORTERS_URL_EN;
    if (!hasTauriRuntime()) {
      if (typeof window !== 'undefined') {
        window.open(url, '_blank', 'noopener,noreferrer');
      }
      return;
    }

    try {
      await openUrl(url);
    } catch (error) {
      console.error(error);
    }
  }

  $: pageModel = createAboutPageModel($locale);
  $: paymentModalMethods = paymentMethods.map((method) => ({
    id: method.id,
    src: method.src,
    alt: $locale === 'zh' ? method.name.zh : method.name.en,
    accent: method.accent
  }));
</script>

<svelte:head>
  <title>{t('aboutTitle')} - BazaarPlusPlus</title>
</svelte:head>

<PaymentCodeModal
  open={showPaymentCodes}
  title={pageModel.paymentModalTitle}
  closeLabel={pageModel.paymentModalCloseLabel}
  cardTitle={pageModel.paymentCardTitle}
  cardBody={pageModel.paymentCardBody}
  supportNote={pageModel.paymentSupportNote}
  supportTip={pageModel.paymentSupportTip}
  methods={paymentModalMethods}
  onClose={closePaymentCodes}
/>

<main class="shell">
  <header class="header">
    <a class="back-btn" href="/install">
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

    <LocaleToggle
      label={pageModel.localeButtonLabel}
      badge={pageModel.localeBadge}
    />

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
          >{pageModel.supporterEntryTitle}</span
        >
        <span class="supporter-entry-subtitle"
          >{pageModel.supporterEntrySubtitle}</span
        >
      </button>
    </div>
  </section>

  <section class="card">
    <h2 class="section-title">{t('aboutAuthors')}</h2>
    <ul class="dep-list">
      {#each authors as author}
        <li>
          <a
            class="dep-item dep-item-link"
            href={author.url}
            target="_blank"
            rel="noopener noreferrer"
          >
            <span class="dep-name">{author.name}</span>
            <span class="dep-role">{t(author.roleKey)}</span>
          </a>
        </li>
      {/each}
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
          <span class="dep-name">{pageModel.paymentActionLabel}</span>
          <span class="dep-link-label">{pageModel.paymentActionHint}</span>
        </button>
      </li>
      {#each supportLinks as supportLink}
        <li>
          <a
            class="dep-item dep-item-link"
            href={supportLink.url}
            target="_blank"
            rel="noopener noreferrer"
          >
            <span class="dep-name">{supportLink.name}</span>
            <span class="dep-link-label"
              >{supportLink.url.replace('https://', '')}</span
            >
          </a>
        </li>
      {/each}
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
      {#each projectDependencies as dep}
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
      {#each frontendDependencies as dep}
        <li class="dep-item">
          <span class="dep-name">{dep.name}</span>
          <span class="dep-license">{dep.license}</span>
        </li>
      {/each}
    </ul>

    <h3 class="group-title">Rust / Backend</h3>
    <ul class="dep-list">
      {#each rustDependencies as dep}
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
    border: 1px solid rgba(var(--color-accent-rgb), 0.18);
    border-radius: 3px;
    box-shadow:
      0 0 0 1px rgba(var(--color-accent-rgb), 0.06) inset,
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
    border: 1px solid rgba(var(--color-accent-rgb), 0.24);
    border-radius: 2px;
    background: linear-gradient(
      180deg,
      rgba(var(--color-accent-rgb), 0.12),
      rgba(var(--color-accent-rgb), 0.06)
    );
    color: rgba(var(--color-cream-rgb), 0.82);
    font-family: 'Cinzel', serif;
    font-size: 0.54rem;
    letter-spacing: 0.14em;
    text-transform: uppercase;
    display: inline-flex;
    align-items: center;
    justify-content: center;
    gap: 0.3rem;
    box-shadow: 0 0 0 1px rgba(var(--color-warm-bright-rgb), 0.08) inset;
    z-index: 2;
    text-decoration: none;
    transition:
      background 0.15s ease,
      border-color 0.15s ease;
  }

  .back-btn:hover {
    background: linear-gradient(
      180deg,
      rgba(var(--color-accent-rgb), 0.2),
      rgba(var(--color-accent-rgb), 0.1)
    );
    border-color: rgba(var(--color-accent-rgb), 0.4);
  }

  .back-btn:focus-visible {
    outline: 2px solid rgba(var(--color-warm-rgb), 0.9);
    outline-offset: 2px;
  }

  .back-icon {
    width: 0.9rem;
    height: 0.9rem;
    flex-shrink: 0;
    opacity: 0.9;
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
    background: linear-gradient(
      155deg,
      var(--color-gold-text) 0%,
      var(--color-gold-deep) 55%,
      var(--color-gold-text) 100%
    );
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
    color: rgba(var(--color-accent-rgb), 0.55);
  }

  .info-line {
    margin: 0;
    font-family: 'Fira Code', monospace;
    font-size: 0.78rem;
    color: rgba(var(--color-cream-rgb), 0.82);
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
    outline: 2px solid rgba(var(--color-warm-rgb), 0.9);
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

  .info-divider {
    width: 1px;
    height: 0.9rem;
    background: linear-gradient(
      180deg,
      transparent,
      rgba(var(--color-muted-gold-rgb), 0.45) 20%,
      rgba(var(--color-muted-gold-rgb), 0.45) 80%,
      transparent
    );
    flex-shrink: 0;
  }

  .version-label {
    font-family: 'Cinzel', serif;
    font-size: 0.6rem;
    letter-spacing: 0.08em;
    text-transform: uppercase;
    color: rgba(var(--color-muted-gold-rgb), 0.5);
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
    color: rgba(var(--color-muted-gold-rgb), 0.5);
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
    background: rgba(var(--color-accent-rgb), 0.04);
    border: 1px solid rgba(180, 130, 48, 0.08);
    transition: background 0.15s ease;
  }

  .dep-item:hover {
    background: rgba(var(--color-accent-rgb), 0.08);
  }

  .dep-name {
    font-family: 'Fira Code', monospace;
    font-size: 0.75rem;
    color: rgba(var(--color-cream-rgb), 0.85);
  }

  .dep-license {
    font-family: 'Fira Code', monospace;
    font-size: 0.62rem;
    color: rgba(var(--color-muted-gold-rgb), 0.45);
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
    border: 1px solid rgba(var(--color-accent-rgb), 0.2);
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
    box-shadow: inset 0 0 0 1px rgba(var(--color-warm-rgb), 0.04);
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
    color: rgba(var(--color-muted-gold-rgb), 0.55);
    flex-shrink: 0;
  }

  .dep-role {
    font-family: 'Cinzel', serif;
    font-size: 0.56rem;
    letter-spacing: 0.1em;
    text-transform: uppercase;
    color: rgba(var(--color-muted-gold-rgb), 0.45);
    flex-shrink: 0;
  }

  .dep-link {
    font-family: 'Fira Code', monospace;
    font-size: 0.62rem;
    color: rgba(var(--color-muted-gold-rgb), 0.55);
    text-decoration: none;
    flex-shrink: 0;
    transition: color 0.15s ease;
  }

  .dep-link:hover {
    color: rgba(220, 180, 100, 0.85);
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
    outline: 2px solid rgba(var(--color-warm-rgb), 0.9);
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
  }
</style>

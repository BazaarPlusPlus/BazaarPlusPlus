<script lang="ts">
  import PaymentCodeModal from '$lib/components/supporters/PaymentCodeModal.svelte';
  import { locale } from '$lib/locale';
  import { formatWhatsNewBulletHtml } from '$lib/whats-new-format';
  import type { WhatsNewRelease } from '$lib/whats-new';

  let showSupportQr = false;
  export let release: WhatsNewRelease;

  const supportQrCopy = {
    zh: {
      title: '支持 BazaarPlusPlus',
      cardTitle: '支持项目',
      cardBody: '请 Bazaar++ 喝一杯',
      note: '有你支持，Bazaar++ 会冒出更多好东西',
      tip: '如果愿意，欢迎在备注里留一个支持者 ID',
      close: '关闭'
    },
    en: {
      title: 'Support BazaarPlusPlus',
      cardTitle: 'Support the Project',
      cardBody: 'Buy Bazaar++ a drink.',
      note: 'With your support, Bazaar++ gets to grow more good stuff.',
      tip: 'If you want, you can leave a supporter ID in the payment note.',
      close: 'Close'
    }
  } as const;

  const supportPaymentMethods = [
    {
      id: 'wechat',
      src: '/support/wechat-pay.svg',
      alt: 'WePay',
      accent: 'payment-card-wechat'
    }
  ];

  $: currentSupportQrCopy =
    $locale === 'zh' ? supportQrCopy.zh : supportQrCopy.en;
</script>

<PaymentCodeModal
  open={showSupportQr}
  title={currentSupportQrCopy.title}
  bodyClass="support-modal-body"
  closeLabel={currentSupportQrCopy.close}
  cardTitle={currentSupportQrCopy.cardTitle}
  cardBody={currentSupportQrCopy.cardBody}
  supportNote={currentSupportQrCopy.note}
  supportTip={currentSupportQrCopy.tip}
  methods={supportPaymentMethods}
  onClose={() => {
    showSupportQr = false;
  }}
/>

<section class="update-hero">
  <p class="update-kicker">
    {$locale === 'zh' ? release.kicker.zh : release.kicker.en}
  </p>
  <h2 class="update-title">BazaarPlusPlus</h2>
  <p class="update-version-tag">v{release.version}</p>
  <p class="update-release-label">
    {$locale === 'zh' ? release.releaseLabel.zh : release.releaseLabel.en}
  </p>
  <p class="update-summary">
    {$locale === 'zh' ? release.summary.zh : release.summary.en}
  </p>
</section>

<div class="update-group-list">
  {#each release.sections as section}
    <section class="update-group">
      <header class="update-group-header">
        <p class="update-group-kicker">
          {section.icon}
          {$locale === 'zh'
            ? section.sectionTitle?.zh
            : section.sectionTitle?.en}
        </p>
        {#if section.sectionSummary}
          <p class="update-group-summary">
            {$locale === 'zh'
              ? section.sectionSummary.zh
              : section.sectionSummary.en}
          </p>
        {/if}
      </header>

      <article class={`update-feature-card tone-${section.tone ?? 'default'}`}>
        <div class="update-feature-icon">{section.icon}</div>
        <div class="update-feature-copy">
          {#if section.badge}
            <p class="update-feature-badge">
              {$locale === 'zh' ? section.badge.zh : section.badge.en}
            </p>
          {/if}
          <h3>
            {$locale === 'zh' ? section.title.zh : section.title.en}
          </h3>
          <ul class="update-feature-points">
            {#each section.bullets as bullet}
              <li>
                {@html formatWhatsNewBulletHtml(
                  $locale === 'zh' ? bullet.zh : bullet.en
                )}
              </li>
            {/each}
          </ul>
        </div>
        {#if section.actionLabel && section.actionLead}
          <div class="update-feature-action">
            <button
              class="featured-support-button"
              type="button"
              onclick={() => (showSupportQr = true)}
            >
              <span class="featured-support-lead"
                >{$locale === 'zh'
                  ? section.actionLead.zh
                  : section.actionLead.en}</span
              >
              <span class="featured-support-label"
                >{$locale === 'zh'
                  ? section.actionLabel.zh
                  : section.actionLabel.en}</span
              >
            </button>
          </div>
        {/if}
      </article>
    </section>
  {/each}
</div>

<style>
  .update-hero {
    display: grid;
    gap: 0.5rem;
    padding: 1rem 1.05rem;
    text-align: left;
    border: 1px solid rgba(var(--color-accent-rgb), 0.18);
    border-radius: 4px;
    background:
      radial-gradient(
        circle at top right,
        rgba(232, 200, 122, 0.16),
        transparent 42%
      ),
      linear-gradient(
        180deg,
        rgba(var(--color-accent-rgb), 0.08),
        rgba(var(--color-accent-rgb), 0.02)
      ),
      rgba(12, 8, 4, 0.84);
    box-shadow: inset 0 0 0 1px rgba(var(--color-warm-bright-rgb), 0.04);
  }

  .update-kicker {
    margin: 0;
    font-family: 'Cinzel', serif;
    font-size: 0.58rem;
    letter-spacing: 0.22em;
    text-transform: uppercase;
    color: rgba(232, 200, 122, 0.7);
  }

  .update-title {
    margin: 0;
    font-family: 'Cinzel', serif;
    font-size: clamp(1rem, 2.6vw, 1.4rem);
    letter-spacing: 0.06em;
    color: rgba(239, 223, 188, 0.95);
  }

  .update-summary {
    margin: 0;
    white-space: pre-line;
    font-size: 0.9rem;
    line-height: 1.65;
    color: rgba(233, 222, 198, 0.84);
  }

  .update-version-tag {
    margin: -0.1rem 0 0;
    font-family: 'Fira Code', monospace;
    font-size: 0.72rem;
    color: rgba(232, 200, 122, 0.8);
  }

  .update-release-label {
    margin: 0.1rem 0 -0.05rem;
    font-family: 'Cinzel', serif;
    font-size: 0.62rem;
    letter-spacing: 0.16em;
    text-transform: uppercase;
    color: rgba(214, 182, 126, 0.72);
  }

  .update-group-list {
    display: grid;
    gap: 1rem;
    margin-top: 0.15rem;
  }

  .update-group {
    display: grid;
    gap: 0.55rem;
  }

  .update-group-header {
    display: grid;
    gap: 0.18rem;
    padding: 0 0.1rem;
  }

  .update-group-kicker {
    margin: 0;
    font-family: 'Cinzel', serif;
    font-size: 0.6rem;
    letter-spacing: 0.2em;
    text-transform: uppercase;
    color: rgba(232, 200, 122, 0.72);
  }

  .update-group-summary {
    margin: 0;
    font-size: 0.78rem;
    line-height: 1.55;
    color: rgba(199, 178, 140, 0.72);
  }

  .update-feature-list {
    display: grid;
    gap: 0.7rem;
    text-align: left;
  }

  .update-feature-card {
    display: grid;
    grid-template-columns: 2.25rem 1fr;
    gap: 0.8rem;
    align-items: start;
    padding: 0.9rem;
    border-radius: 3px;
    box-shadow: inset 0 0 0 1px rgba(var(--color-warm-bright-rgb), 0.04);
  }

  .tone-default {
    border: 1px solid rgba(var(--color-accent-rgb), 0.18);
    background:
      linear-gradient(
        180deg,
        rgba(var(--color-accent-rgb), 0.08),
        rgba(var(--color-accent-rgb), 0.02)
      ),
      rgba(12, 8, 4, 0.82);
  }

  .tone-featured {
    grid-template-columns: 2.25rem minmax(0, 1fr) auto;
    border: 1px solid rgba(226, 181, 82, 0.34);
    background:
      radial-gradient(
        circle at top right,
        rgba(255, 218, 120, 0.16),
        transparent 38%
      ),
      linear-gradient(
        180deg,
        rgba(230, 178, 74, 0.14),
        rgba(var(--color-accent-rgb), 0.04)
      ),
      rgba(16, 10, 4, 0.88);
    box-shadow:
      inset 0 0 0 1px rgba(255, 216, 125, 0.08),
      0 10px 28px rgba(0, 0, 0, 0.18);
  }

  .tone-warning {
    border: 1px solid rgba(214, 78, 78, 0.4);
    background:
      radial-gradient(
        circle at top right,
        rgba(214, 78, 78, 0.14),
        transparent 42%
      ),
      linear-gradient(180deg, rgba(165, 44, 44, 0.16), rgba(114, 26, 26, 0.06)),
      rgba(16, 8, 8, 0.88);
    box-shadow: inset 0 0 0 1px rgba(255, 132, 132, 0.05);
  }

  .update-feature-icon {
    width: 2.25rem;
    height: 2.25rem;
    display: grid;
    place-items: center;
    border: 1px solid rgba(214, 169, 84, 0.28);
    border-radius: 999px;
    background: radial-gradient(
      circle at 30% 30%,
      rgba(232, 200, 122, 0.22),
      rgba(158, 92, 30, 0.14)
    );
    color: rgba(232, 200, 122, 0.92);
    font-family: 'Cinzel', serif;
    font-size: 0.66rem;
    letter-spacing: 0.12em;
  }

  .tone-featured .update-feature-icon {
    border-color: rgba(255, 212, 111, 0.42);
    background: radial-gradient(
      circle at 30% 30%,
      rgba(255, 219, 129, 0.34),
      rgba(194, 120, 25, 0.18)
    );
    color: rgba(255, 226, 150, 0.98);
  }

  .tone-warning .update-feature-icon {
    border-color: rgba(223, 110, 110, 0.42);
    background: radial-gradient(
      circle at 30% 30%,
      rgba(224, 112, 112, 0.28),
      rgba(133, 31, 31, 0.16)
    );
    color: rgba(255, 182, 182, 0.94);
  }

  .update-feature-copy h3 {
    margin: 0 0 0.35rem;
    font-family: 'Cinzel', serif;
    font-size: 0.86rem;
    letter-spacing: 0.06em;
    color: rgba(239, 223, 188, 0.92);
  }

  .update-feature-badge {
    display: inline-flex;
    align-items: center;
    margin: 0 0 0.35rem;
    padding: 0.18rem 0.5rem;
    border: 1px solid rgba(255, 216, 124, 0.32);
    border-radius: 999px;
    background: rgba(255, 216, 124, 0.08);
    color: rgba(255, 222, 148, 0.92);
    font-family: 'Cinzel', serif;
    font-size: 0.54rem;
    letter-spacing: 0.16em;
    text-transform: uppercase;
  }

  .tone-warning .update-feature-copy h3 {
    color: rgba(255, 202, 202, 0.95);
  }

  .update-feature-action {
    display: flex;
    align-items: center;
    justify-content: flex-end;
    min-height: 100%;
  }

  .featured-support-button {
    display: grid;
    gap: 0.18rem;
    min-width: 13.5rem;
    padding: 0.9rem 1.1rem;
    border: 1px solid rgba(236, 195, 104, 0.34);
    border-radius: 14px;
    background:
      linear-gradient(
        180deg,
        rgba(255, 221, 146, 0.12),
        rgba(204, 142, 40, 0.08)
      ),
      rgba(28, 18, 8, 0.88);
    color: rgba(248, 230, 185, 0.96);
    text-align: left;
    box-shadow:
      inset 0 0 0 1px rgba(255, 225, 154, 0.05),
      0 10px 24px rgba(0, 0, 0, 0.18);
    transition:
      transform 0.15s ease,
      border-color 0.15s ease,
      background 0.15s ease,
      box-shadow 0.15s ease;
  }

  .featured-support-button:hover {
    transform: translateY(-1px);
    border-color: rgba(255, 214, 118, 0.52);
    background:
      linear-gradient(
        180deg,
        rgba(255, 225, 154, 0.16),
        rgba(214, 152, 48, 0.1)
      ),
      rgba(32, 20, 8, 0.92);
    box-shadow:
      inset 0 0 0 1px rgba(255, 229, 162, 0.06),
      0 14px 28px rgba(0, 0, 0, 0.22);
  }

  .featured-support-lead {
    font-family: 'Cinzel', serif;
    font-size: 0.62rem;
    letter-spacing: 0.18em;
    text-transform: uppercase;
    color: rgba(255, 215, 124, 0.78);
  }

  .featured-support-label {
    font-family: 'Cinzel', serif;
    font-size: 0.92rem;
    letter-spacing: 0.08em;
    color: rgba(255, 235, 190, 0.98);
  }

  .update-feature-points {
    margin: 0;
    padding-left: 1.1rem;
    display: grid;
    gap: 0.35rem;
    color: rgba(233, 222, 198, 0.82);
    line-height: 1.62;
    font-size: 0.88rem;
  }

  .tone-featured .update-feature-points {
    color: rgba(245, 231, 198, 0.9);
  }

  .tone-warning .update-feature-points {
    color: rgba(244, 214, 214, 0.9);
  }

  .update-feature-points li::marker {
    color: rgba(232, 200, 122, 0.72);
  }

  :global(.update-key) {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    min-width: 2.1rem;
    margin: 0 0.15rem;
    padding: 0.08rem 0.42rem;
    border: 1px solid rgba(238, 201, 117, 0.34);
    border-radius: 0.45rem;
    background:
      linear-gradient(
        180deg,
        rgba(255, 228, 160, 0.16),
        rgba(169, 110, 28, 0.1)
      ),
      rgba(28, 18, 8, 0.92);
    box-shadow:
      inset 0 1px 0 rgba(255, 236, 190, 0.18),
      0 2px 8px rgba(0, 0, 0, 0.18);
    color: rgba(255, 231, 180, 0.96);
    font-family: 'Fira Code', monospace;
    font-size: 0.8em;
    font-weight: 600;
    line-height: 1.2;
    vertical-align: baseline;
  }

  .tone-warning .update-feature-points li::marker {
    color: rgba(239, 126, 126, 0.88);
  }

  @media (max-width: 560px) {
    .update-feature-card {
      grid-template-columns: 1fr;
    }

    .tone-featured {
      grid-template-columns: 1fr;
    }

    .update-feature-icon {
      width: 2rem;
      height: 2rem;
    }

    .update-feature-action {
      justify-content: stretch;
    }

    .featured-support-button {
      width: 100%;
      min-width: 0;
    }
  }
</style>

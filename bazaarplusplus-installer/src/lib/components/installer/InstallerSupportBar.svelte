<script lang="ts">
  import { openUrl } from '@tauri-apps/plugin-opener';
  import AppModal from '$lib/components/AppModal.svelte';
  import SupporterListModal from '$lib/components/supporters/SupporterListModal.svelte';
  import { locale } from '$lib/locale';
  import { hasTauriRuntime } from '$lib/installer/runtime';

  type CopyKey =
    | 'title'
    | 'body'
    | 'wechat'
    | 'wechatAction'
    | 'kofi'
    | 'kofiAction'
    | 'supporters'
    | 'supportersAction'
    | 'supportQrTitle'
    | 'supportQrBody'
    | 'supportQrHint'
    | 'close';

  const copy = {
    en: {
      title: 'Support the Project',
      body: "If you've been enjoying BazaarPlusPlus, you can support the project here.",
      wechat: 'WePay',
      wechatAction: 'Open QR',
      kofi: 'Ko-fi',
      kofiAction: 'Buy a coffee',
      supporters: 'Supporters',
      supportersAction: 'Open list',
      supportQrTitle: 'Support BazaarPlusPlus',
      supportQrCardTitle: 'Support the Project',
      supportQrCardBody: 'Buy Bazaar++ a drink.',
      supportQrBody:
        'With your support, Bazaar++ gets to grow more good stuff.',
      supportQrHint:
        'If you want, you can leave a supporter ID in the payment note.',
      close: 'Close'
    },
    zh: {
      title: '\u652f\u6301\u9879\u76ee',
      body: '\u5982\u679c\u4f60\u89c9\u5f97 BazaarPlusPlus \u8fd8\u4e0d\u9519\uff0c\u6b22\u8fce\u5728\u8fd9\u91cc\u652f\u6301\u9879\u76ee\u7ee7\u7eed\u66f4\u65b0\u3002',
      wechat: 'WePay',
      wechatAction: '\u6253\u5f00\u6536\u6b3e\u7801',
      kofi: 'Ko-fi',
      kofiAction: '\u8bf7\u4f5c\u8005\u559d\u676f\u5496\u5561',
      supporters: '\u652f\u6301\u8005\u540d\u5355',
      supportersAction: '\u67e5\u770b\u540d\u5355',
      supportQrTitle: '\u652f\u6301 BazaarPlusPlus',
      supportQrCardTitle: '\u652f\u6301\u9879\u76ee',
      supportQrCardBody: '\u8bf7 Bazaar++ \u559d\u4e00\u676f',
      supportQrBody:
        '\u6709\u4f60\u652f\u6301\uff0cBazaar++ \u4f1a\u5192\u51fa\u66f4\u591a\u597d\u4e1c\u897f',
      supportQrHint:
        '\u5982\u679c\u613f\u610f\uff0c\u6b22\u8fce\u5728\u5907\u6ce8\u91cc\u7559\u4e00\u4e2a\u652f\u6301\u8005 ID',
      close: '\u5173\u95ed'
    }
  } as const;

  const KOFI_URL = 'https://ko-fi.com/cauyxy';

  let showPaymentCodes = false;
  let showSupporterList = false;

  $: currentCopy = $locale === 'zh' ? copy.zh : copy.en;

  function openPaymentCodes() {
    showPaymentCodes = true;
  }

  async function openKoFi() {
    if (!hasTauriRuntime()) {
      if (typeof window !== 'undefined') {
        window.open(KOFI_URL, '_blank', 'noopener,noreferrer');
      }
      return;
    }

    try {
      await openUrl(KOFI_URL);
    } catch (error) {
      console.error(error);
    }
  }

  function openSupporterList() {
    showSupporterList = true;
  }
</script>

<AppModal
  open={showPaymentCodes}
  eyebrow="BazaarPlusPlus"
  title={currentCopy.supportQrTitle}
  bodyClass="support-modal-body"
  confirmText={currentCopy.close}
  onConfirm={() => {
    showPaymentCodes = false;
  }}
>
  <section class="support-modal-shell">
    <div class="payment-grid">
      <article class="payment-card payment-card-wechat">
        <div class="payment-frame">
          <img
            class="payment-image"
            src="/support/wechat-pay.svg"
            alt={currentCopy.wechat}
          />
        </div>

        <div class="payment-copy">
          <h3>{currentCopy.supportQrCardTitle}</h3>
          <p>{currentCopy.supportQrCardBody}</p>
        </div>
      </article>
    </div>

    <p class="payment-support-note">{currentCopy.supportQrBody}</p>
    <p class="payment-support-tip">{currentCopy.supportQrHint}</p>
  </section>
</AppModal>

<SupporterListModal
  open={showSupporterList}
  onClose={() => {
    showSupporterList = false;
  }}
/>

<section class="support-strip" aria-label={currentCopy.title}>
  <div class="support-copy">
    <p class="support-eyebrow">Support</p>
    <h2>{currentCopy.title}</h2>
    <p class="support-body">{currentCopy.body}</p>
  </div>

  <div class="support-actions">
    <button class="support-action" type="button" onclick={openPaymentCodes}>
      <span class="support-action-title">{currentCopy.wechat}</span>
      <span class="support-action-subtitle">{currentCopy.wechatAction}</span>
    </button>

    <button class="support-action" type="button" onclick={openKoFi}>
      <span class="support-action-title">{currentCopy.kofi}</span>
      <span class="support-action-subtitle">{currentCopy.kofiAction}</span>
    </button>

    <button class="support-action" type="button" onclick={openSupporterList}>
      <span class="support-action-title">{currentCopy.supporters}</span>
      <span class="support-action-subtitle">{currentCopy.supportersAction}</span
      >
    </button>
  </div>
</section>

<style>
  .support-strip {
    padding: 0.95rem 1.05rem;
    background:
      radial-gradient(
        circle at top left,
        rgba(255, 214, 140, 0.08),
        transparent 42%
      ),
      linear-gradient(180deg, rgba(20, 12, 6, 0.96), rgba(12, 7, 4, 0.94));
    border: 1px solid rgba(200, 148, 55, 0.15);
    border-radius: 3px;
    box-shadow:
      0 8px 28px rgba(0, 0, 0, 0.3),
      inset 0 0 0 1px rgba(255, 214, 140, 0.04);
    display: grid;
    gap: 0.85rem;
  }

  .support-copy {
    display: grid;
    gap: 0.2rem;
  }

  .support-eyebrow {
    margin: 0;
    font-family: 'Cinzel', serif;
    font-size: 0.5rem;
    letter-spacing: 0.24em;
    text-transform: uppercase;
    color: rgba(200, 148, 55, 0.52);
  }

  .support-copy h2 {
    margin: 0;
    font-family: 'Cinzel', serif;
    font-size: 0.82rem;
    letter-spacing: 0.08em;
    text-transform: uppercase;
    color: rgba(232, 220, 194, 0.92);
  }

  .support-body {
    margin: 0;
    font-size: 0.78rem;
    line-height: 1.55;
    color: rgba(208, 188, 150, 0.74);
  }

  .support-actions {
    display: grid;
    grid-template-columns: repeat(3, minmax(0, 1fr));
    gap: 0.6rem;
  }

  .support-action {
    padding: 0.7rem 0.8rem;
    border: 1px solid rgba(200, 148, 55, 0.14);
    border-radius: 3px;
    background: linear-gradient(
      180deg,
      rgba(200, 148, 55, 0.06),
      rgba(200, 148, 55, 0.02)
    );
    display: grid;
    gap: 0.14rem;
    text-align: left;
    transition:
      border-color 0.15s ease,
      background 0.15s ease,
      transform 0.15s ease;
  }

  .support-action:hover {
    border-color: rgba(220, 168, 76, 0.28);
    background: linear-gradient(
      180deg,
      rgba(200, 148, 55, 0.12),
      rgba(200, 148, 55, 0.05)
    );
    transform: translateY(-1px);
  }

  .support-action:focus-visible {
    outline: 2px solid rgba(255, 214, 140, 0.9);
    outline-offset: 2px;
  }

  .support-action-title {
    font-family: 'Cinzel', serif;
    font-size: 0.66rem;
    letter-spacing: 0.1em;
    text-transform: uppercase;
    color: rgba(236, 224, 196, 0.9);
  }

  .support-action-subtitle {
    font-family: 'Fira Code', monospace;
    font-size: 0.62rem;
    color: rgba(200, 170, 120, 0.58);
  }

  :global(.support-modal-body) {
    padding-top: 0.1rem;
  }

  .support-modal-shell {
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

  .payment-image {
    width: 100%;
    height: 100%;
    display: block;
    object-fit: contain;
    background: #fff;
    border-radius: 2px;
  }

  button {
    cursor: pointer;
    border: none;
    font: inherit;
  }

  @media (max-width: 520px) {
    .support-actions {
      grid-template-columns: 1fr;
    }

    .payment-grid {
      grid-template-columns: 1fr;
    }
  }
</style>

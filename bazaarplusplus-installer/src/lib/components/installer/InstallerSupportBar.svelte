<script lang="ts">
  import { openUrl } from '@tauri-apps/plugin-opener';
  import PaymentCodeModal from '$lib/components/supporters/PaymentCodeModal.svelte';
  import { locale } from '$lib/locale';
  import { hasTauriRuntime } from '$lib/installer/runtime';

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
  const SUPPORTERS_URL_EN = 'https://bazaarplusplus.com/support?lang=en';
  const SUPPORTERS_URL_ZH = 'https://bazaarplusplus.com/support';

  let showPaymentCodes = false;

  $: currentCopy = $locale === 'zh' ? copy.zh : copy.en;
  $: supportPaymentMethods = [
    {
      id: 'wechat',
      src: '/support/wechat-pay.svg',
      alt: currentCopy.wechat,
      accent: 'payment-card-wechat'
    }
  ];

  function openPaymentCodes() {
    showPaymentCodes = true;
  }

  async function openExternal(url: string) {
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

  async function openKoFi() {
    await openExternal(KOFI_URL);
  }

  async function openSupporterList() {
    await openExternal(
      $locale === 'zh' ? SUPPORTERS_URL_ZH : SUPPORTERS_URL_EN
    );
  }
</script>

<PaymentCodeModal
  open={showPaymentCodes}
  title={currentCopy.supportQrTitle}
  bodyClass="support-modal-body"
  closeLabel={currentCopy.close}
  cardTitle={currentCopy.supportQrCardTitle}
  cardBody={currentCopy.supportQrCardBody}
  supportNote={currentCopy.supportQrBody}
  supportTip={currentCopy.supportQrHint}
  methods={supportPaymentMethods}
  onClose={() => {
    showPaymentCodes = false;
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
    padding: 0.78rem 0.92rem;
    background:
      radial-gradient(
        circle at top left,
        rgba(var(--color-warm-rgb), 0.04),
        transparent 42%
      ),
      linear-gradient(180deg, rgba(18, 11, 6, 0.88), rgba(11, 7, 4, 0.86));
    border: 1px solid rgba(var(--color-accent-rgb), 0.11);
    border-radius: 3px;
    box-shadow: inset 0 0 0 1px rgba(var(--color-warm-rgb), 0.03);
    display: grid;
    grid-template-columns: minmax(0, 1.2fr) minmax(0, 1fr);
    gap: 0.85rem 1rem;
    align-items: end;
  }

  .support-copy {
    display: grid;
    gap: 0.14rem;
  }

  .support-eyebrow {
    margin: 0;
    font-family: 'Cinzel', serif;
    font-size: 0.46rem;
    letter-spacing: 0.24em;
    text-transform: uppercase;
    color: rgba(var(--color-accent-rgb), 0.44);
  }

  .support-copy h2 {
    margin: 0;
    font-family: 'Cinzel', serif;
    font-size: 0.9rem;
    letter-spacing: 0.03em;
    color: rgba(232, 220, 194, 0.9);
  }

  .support-body {
    margin: 0;
    max-width: 36rem;
    font-size: 0.75rem;
    line-height: 1.5;
    color: rgba(208, 188, 150, 0.62);
  }

  .support-actions {
    display: grid;
    grid-template-columns: repeat(3, minmax(0, 1fr));
    gap: 0.42rem;
  }

  .support-action {
    padding: 0.58rem 0.68rem;
    border: 1px solid rgba(var(--color-accent-rgb), 0.11);
    border-radius: 3px;
    background: linear-gradient(
      180deg,
      rgba(var(--color-accent-rgb), 0.04),
      rgba(var(--color-accent-rgb), 0.01)
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
    border-color: rgba(220, 168, 76, 0.22);
    background: linear-gradient(
      180deg,
      rgba(var(--color-accent-rgb), 0.09),
      rgba(var(--color-accent-rgb), 0.03)
    );
    transform: translateY(-1px);
  }

  .support-action:focus-visible {
    outline: 2px solid rgba(var(--color-warm-rgb), 0.9);
    outline-offset: 2px;
  }

  .support-action-title {
    font-family: 'Cinzel', serif;
    font-size: 0.6rem;
    letter-spacing: 0.08em;
    text-transform: uppercase;
    color: rgba(236, 224, 196, 0.86);
  }

  .support-action-subtitle {
    font-family: 'Fira Code', monospace;
    font-size: 0.58rem;
    color: rgba(var(--color-muted-gold-rgb), 0.5);
  }

  @media (max-width: 760px) {
    .support-strip {
      grid-template-columns: 1fr;
      align-items: stretch;
    }
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
  }
</style>

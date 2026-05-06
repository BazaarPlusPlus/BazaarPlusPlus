<script lang="ts">
  import AppModal from '$lib/components/AppModal.svelte';

  interface PaymentCodeMethod {
    id: string;
    src: string;
    alt: string;
    accent?: string;
  }

  export let open = false;
  export let title = '';
  export let closeLabel = 'Close';
  export let cardTitle = '';
  export let cardBody = '';
  export let supportNote = '';
  export let supportTip = '';
  export let bodyClass = 'payment-modal-body';
  export let methods: PaymentCodeMethod[] = [];
  export let onClose: () => void = () => {};

  let hiddenImages: Record<string, boolean> = {};
  let wasOpen = false;

  $: {
    if (open && !wasOpen) {
      hiddenImages = {};
    }
    wasOpen = open;
  }

  function handleImageError(methodId: string) {
    hiddenImages = {
      ...hiddenImages,
      [methodId]: true
    };
  }
</script>

<AppModal
  {open}
  eyebrow="BazaarPlusPlus"
  {title}
  {bodyClass}
  confirmText={closeLabel}
  onConfirm={onClose}
>
  <section class="payment-modal-shell">
    <div class="payment-grid">
      {#each methods as method}
        <article class={`payment-card ${method.accent ?? ''}`.trim()}>
          <div class="payment-frame">
            {#if !hiddenImages[method.id]}
              <img
                class="payment-image"
                src={method.src}
                alt={method.alt}
                onerror={() => handleImageError(method.id)}
              />
            {:else}
              <div class="payment-placeholder" aria-hidden="true"></div>
            {/if}
          </div>

          <div class="payment-copy">
            <h3>{cardTitle}</h3>
            <p>{cardBody}</p>
          </div>
        </article>
      {/each}
    </div>

    <p class="payment-support-note">{supportNote}</p>
    <p class="payment-support-tip">{supportTip}</p>
  </section>
</AppModal>

<style>
  :global(.payment-modal-body),
  :global(.support-modal-body) {
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
    border: 1px solid rgba(var(--color-accent-rgb), 0.16);
    border-radius: 4px;
    display: grid;
    gap: 0.65rem;
    box-shadow: inset 0 0 0 1px rgba(var(--color-warm-bright-rgb), 0.05);
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
      inset 0 0 0 1px rgba(var(--color-warm-bright-rgb), 0.05),
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
    color: rgba(var(--color-muted-gold-rgb), 0.8);
  }

  @media (max-width: 560px) {
    .payment-grid {
      grid-template-columns: 1fr;
    }
  }
</style>

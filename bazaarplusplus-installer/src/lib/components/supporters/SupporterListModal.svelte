<script lang="ts">
  import AppModal from '$lib/components/AppModal.svelte';
  import { locale } from '$lib/locale';
  import { loadSupportersData } from '$lib/supporters';
  import type { SupporterEntry } from '$lib/types';

  export let open = false;
  export let onClose: () => void = () => {};

  const copy = {
    en: {
      title: 'Supporters',
      close: 'Close',
      intro:
        'Thank you for backing Bazaar++. \nYour support keeps the project moving further.',
      errorPrefix: 'Failed to load supporter list:',
      empty: 'The supporter list has not been compiled yet.',
      thanks:
        'Thanks as well to everyone who supported Bazaar++ without leaving a name.',
      ariaLabel: 'Supporter list'
    },
    zh: {
      title: '支持者名单',
      close: '关闭',
      intro: '有你的支持，让 Bazaar++ 走的更远',
      errorPrefix: '无法读取支持者名单：',
      empty: '暂时还没有整理支持者名单。',
      thanks: '也感谢所有未署名的支持者。',
      ariaLabel: '支持者名单'
    }
  } as const;

  let supporters: SupporterEntry[] = [];
  let supportersLoadError = '';
  let supportersLoadPromise: Promise<void> | null = null;

  $: currentCopy = $locale === 'zh' ? copy.zh : copy.en;
  $: sortedSupporters = supporters;
  $: if (open) {
    void loadSupporters();
  }

  async function loadSupporters() {
    if (supportersLoadPromise) return supportersLoadPromise;

    supportersLoadPromise = (async () => {
      supportersLoadError = '';

      try {
        const payload = await loadSupportersData();
        supporters = payload.entries;
      } catch (error) {
        supporters = [];
        supportersLoadError =
          error instanceof Error ? error.message : String(error);
      } finally {
        supportersLoadPromise = null;
      }
    })();

    return supportersLoadPromise;
  }
</script>

<AppModal
  {open}
  eyebrow="BazaarPlusPlus"
  title={currentCopy.title}
  bodyClass="supporter-modal-body"
  confirmText={currentCopy.close}
  onConfirm={onClose}
>
  <section class="supporter-modal-shell">
    <div class="supporter-hero">
      <p class="supporter-intro">{currentCopy.intro}</p>
    </div>

    {#if supportersLoadError}
      <p class="supporter-state">
        {currentCopy.errorPrefix}
        {supportersLoadError}
      </p>
    {:else if sortedSupporters.length > 0}
      <ul
        class="supporter-list supporter-list-mixed"
        aria-label={currentCopy.ariaLabel}
      >
        {#each sortedSupporters as supporter}
          <li class={`supporter-item supporter-item-tier-${supporter.tier}`}>
            {supporter.name}
          </li>
        {/each}
      </ul>
    {:else}
      <p class="support-tier-empty">{currentCopy.empty}</p>
    {/if}

    <p class="supporter-unnamed-note">{currentCopy.thanks}</p>
  </section>
</AppModal>

<style>
  .supporter-modal-body {
    padding-top: 0.1rem;
  }

  .supporter-modal-shell {
    display: grid;
    gap: 0.75rem;
    text-align: center;
  }

  .supporter-hero {
    position: relative;
    padding: 0.85rem 0.9rem 0.8rem;
    border-radius: 4px;
    background:
      radial-gradient(
        circle at top,
        rgba(255, 224, 150, 0.12),
        transparent 60%
      ),
      linear-gradient(180deg, rgba(36, 21, 8, 0.92), rgba(21, 12, 6, 0.94));
    border: 1px solid rgba(200, 148, 55, 0.16);
    box-shadow: inset 0 0 0 1px rgba(255, 214, 140, 0.04);
  }

  .supporter-intro {
    margin: 0;
    color: rgba(228, 216, 191, 0.84);
    font-size: 0.8rem;
    line-height: 1.65;
    white-space: pre-line;
  }

  .supporter-state {
    margin: 0;
    color: rgba(214, 190, 146, 0.76);
    font-size: 0.8rem;
    line-height: 1.6;
    text-align: center;
  }

  .supporter-unnamed-note {
    margin: 0;
    padding-top: 0.15rem;
    color: rgba(200, 170, 120, 0.72);
    font-size: 0.74rem;
    line-height: 1.6;
    text-align: center;
    font-style: italic;
  }

  .support-tier-empty {
    margin: 0;
    padding: 0.55rem 0.7rem;
    border-radius: 999px;
    border: 1px dashed rgba(200, 170, 120, 0.24);
    background: rgba(200, 148, 55, 0.04);
    color: rgba(214, 190, 146, 0.68);
    font-size: 0.68rem;
    line-height: 1.45;
    text-align: center;
  }

  .supporter-list {
    margin: 0;
    padding: 0;
    list-style: none;
    display: flex;
    flex-wrap: wrap;
    gap: 0.45rem;
    align-content: flex-start;
  }

  .supporter-list-mixed {
    justify-content: center;
  }

  .supporter-item {
    --pill-border: rgba(255, 232, 174, 0.18);
    --pill-top: rgba(255, 248, 231, 0.12);
    --pill-bottom: rgba(200, 148, 55, 0.08);
    --pill-shadow: rgba(255, 214, 140, 0.04);
    --pill-glow: transparent;
    padding: 0.38rem 0.72rem;
    border-radius: 999px;
    background:
      radial-gradient(circle at top, var(--pill-glow), transparent 70%),
      linear-gradient(180deg, var(--pill-top), var(--pill-bottom));
    border: 1px solid var(--pill-border);
    color: rgba(236, 224, 198, 0.9);
    font-family: 'Fira Code', monospace;
    font-size: 0.68rem;
    line-height: 1.3;
    box-shadow:
      inset 0 0 0 1px var(--pill-shadow),
      0 6px 16px rgba(0, 0, 0, 0.12);
  }

  .supporter-item-tier-1 {
    --pill-border: rgba(111, 166, 224, 0.3);
    --pill-top: rgba(216, 235, 255, 0.12);
    --pill-bottom: rgba(111, 166, 224, 0.08);
    --pill-shadow: rgba(141, 198, 255, 0.06);
    --pill-glow: rgba(141, 198, 255, 0.14);
  }

  .supporter-item-tier-2 {
    --pill-border: rgba(220, 156, 76, 0.3);
    --pill-top: rgba(255, 232, 178, 0.12);
    --pill-bottom: rgba(220, 156, 76, 0.08);
    --pill-shadow: rgba(255, 187, 104, 0.06);
    --pill-glow: rgba(255, 187, 104, 0.14);
  }

  .supporter-item-tier-3 {
    --pill-border: rgba(219, 102, 86, 0.32);
    --pill-top: rgba(255, 218, 208, 0.12);
    --pill-bottom: rgba(219, 102, 86, 0.08);
    --pill-shadow: rgba(255, 132, 118, 0.06);
    --pill-glow: rgba(255, 110, 92, 0.14);
  }

  .supporter-item-tier-4 {
    --pill-border: rgba(172, 138, 219, 0.34);
    --pill-top: rgba(240, 228, 255, 0.14);
    --pill-bottom: rgba(172, 138, 219, 0.1);
    --pill-shadow: rgba(210, 177, 255, 0.07);
    --pill-glow: rgba(210, 177, 255, 0.16);
  }
</style>

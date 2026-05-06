<script lang="ts">
  import { getVersion } from '@tauri-apps/api/app';
  import { onMount } from 'svelte';
  import InstallerUpdateHighlights from '$lib/components/InstallerUpdateHighlights.svelte';
  import { locale } from '$lib/locale';
  import { hasTauriRuntime } from '$lib/installer/runtime';
  import { resolveWhatsNewRelease } from '$lib/whats-new';

  let displayVersion = '';
  $: release = resolveWhatsNewRelease(displayVersion);

  onMount(() => {
    locale.init();
    void loadReleaseVersion();
  });

  async function loadReleaseVersion() {
    if (!hasTauriRuntime()) {
      displayVersion = '';
      return;
    }

    try {
      const version = await getVersion();
      displayVersion = version?.trim() ?? '';
    } catch {
      displayVersion = '';
    }
  }
</script>

<svelte:head>
  <title>{$locale === 'zh' ? '更新记录' : 'Changelog'} - BazaarPlusPlus</title>
</svelte:head>

<main class="shell">
  <a class="back-link" href="/install">
    {$locale === 'zh' ? '返回安装器' : 'Back to Installer'}
  </a>

  <header class="header">
    <p class="kicker">BazaarPlusPlus</p>
    <h1>{$locale === 'zh' ? '更新记录' : 'Changelog'}</h1>
  </header>

  <section class="content-card">
    <InstallerUpdateHighlights {release} />
  </section>
</main>

<style>
  .shell {
    width: min(100%, 760px);
    margin: 0 auto;
    padding: 1.2rem 1rem 2rem;
    display: grid;
    gap: 1rem;
  }

  .content-card {
    border-radius: 3px;
  }

  .back-link {
    justify-self: start;
    display: inline-flex;
    align-items: center;
    min-height: 2rem;
    padding: 0 0.65rem;
    border: 1px solid rgba(180, 130, 48, 0.22);
    border-radius: 2px;
    background: linear-gradient(
      180deg,
      rgba(var(--color-accent-rgb), 0.12),
      rgba(var(--color-accent-rgb), 0.06)
    );
    color: rgba(var(--color-cream-rgb), 0.82);
    text-decoration: none;
    font-family: 'Cinzel', serif;
    font-size: 0.56rem;
    letter-spacing: 0.12em;
    text-transform: uppercase;
    box-shadow: 0 0 0 1px rgba(var(--color-warm-bright-rgb), 0.08) inset;
  }

  .back-link:hover {
    border-color: rgba(216, 163, 81, 0.3);
  }

  .header {
    display: grid;
    gap: 0.35rem;
    margin-bottom: 1rem;
    text-align: center;
  }

  .kicker,
  h1 {
    margin: 0;
  }

  .kicker {
    font-family: 'Cinzel', serif;
    font-size: 0.58rem;
    letter-spacing: 0.28em;
    text-transform: uppercase;
    color: rgba(205, 150, 60, 0.58);
  }

  h1 {
    font-family: 'Cinzel Decorative', serif;
    font-size: clamp(1.6rem, 4vw, 2.4rem);
    color: var(--color-gold-text);
  }

  .content-card {
    padding: 1.1rem;
    border: 1px solid rgba(180, 130, 48, 0.16);
    background: rgba(18, 11, 5, 0.82);
    box-shadow: 0 16px 50px rgba(0, 0, 0, 0.34);
  }
</style>

<script lang="ts">
  import InstallerSupportBar from "$lib/components/installer/InstallerSupportBar.svelte";
  import { loadPersistedCustomGamePath } from "$lib/installer/storage";
  import StreamModePanel from "$lib/components/stream/StreamModePanel.svelte";
  import StreamRecordLibrary from "$lib/components/stream/StreamRecordLibrary.svelte";
  import { locale } from "$lib/locale";
  import { formatMessage, messages } from "$lib/i18n";

  $: t = (
    key: keyof typeof messages.en,
    params?: Record<string, string | number>
  ): string => formatMessage($locale, key, params);
  $: persistedGamePath = loadPersistedCustomGamePath() || null;

  locale.init();
</script>

<svelte:head>
  <title>{t('streamTitle')} - BazaarPlusPlus</title>
</svelte:head>

<main class="stream-shell">
  <a class="back-link" href="/install">{t('navInstall')}</a>

  <StreamModePanel gamePath={persistedGamePath} />

  <StreamRecordLibrary gamePath={persistedGamePath} />

  <InstallerSupportBar />
</main>

<style>
  .stream-shell {
    width: min(100%, 980px);
    margin: 0 auto;
    padding: 1.15rem 1rem 2rem;
    display: grid;
    gap: 1rem;
  }

  .back-link {
    justify-self: start;
    display: inline-flex;
    align-items: center;
    min-height: 2rem;
    padding: 0 0.65rem;
    border: 1px solid rgba(188, 136, 58, 0.24);
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
    background: linear-gradient(
      180deg,
      rgba(var(--color-accent-rgb), 0.18),
      rgba(var(--color-accent-rgb), 0.1)
    );
  }

  @media (max-width: 720px) {
    .stream-shell {
      padding: 0.95rem 0.85rem 1.6rem;
    }
  }
</style>

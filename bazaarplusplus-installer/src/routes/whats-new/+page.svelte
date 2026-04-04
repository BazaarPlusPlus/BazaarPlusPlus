<script lang="ts">
  import InstallerUpdateHighlights from '$lib/components/InstallerUpdateHighlights.svelte';
  import { locale, handleLocaleToggle } from '$lib/locale';

  locale.init();
</script>

<svelte:head>
  <title>{$locale === 'zh' ? '更新亮点' : "What's New"} - BazaarPlusPlus</title>
</svelte:head>

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
      {$locale === 'zh' ? '返回首页' : 'Back Home'}
    </a>

    <button
      class="locale-toggle"
      onclick={handleLocaleToggle}
      type="button"
      aria-label={$locale === 'zh' ? 'Switch to English' : '切换到中文'}
      title={$locale === 'zh' ? 'Switch to English' : '切换到中文'}
    >
      <span class="locale-badge">{$locale === 'zh' ? '中' : 'EN'}</span>
    </button>

    <p class="kicker">BazaarPlusPlus</p>
    <h1>{$locale === 'zh' ? '更新亮点' : "What's New"}</h1>
  </header>

  <section class="content-card">
    <InstallerUpdateHighlights />
  </section>
</main>

<style>
  .shell {
    min-height: 100vh;
    padding: 1.4rem 1rem 2rem;
    background:
      radial-gradient(circle at top, rgba(76, 46, 17, 0.5), transparent 36%),
      linear-gradient(180deg, #120b05 0%, #0a0603 100%);
    color: rgba(235, 224, 203, 0.88);
  }

  .header {
    width: min(100%, 760px);
    margin: 0 auto 1rem;
    display: grid;
    gap: 0.45rem;
    position: relative;
    text-align: center;
  }

  .back-btn {
    position: absolute;
    left: 0;
    top: 0;
    display: inline-flex;
    align-items: center;
    gap: 0.35rem;
    padding: 0.48rem 0.7rem;
    color: rgba(220, 185, 115, 0.86);
    text-decoration: none;
    font-family: 'Cinzel', serif;
    font-size: 0.58rem;
    letter-spacing: 0.16em;
    text-transform: uppercase;
    border: 1px solid rgba(180, 130, 48, 0.18);
    border-radius: 999px;
    background: rgba(200, 148, 55, 0.06);
  }

  .back-icon {
    width: 0.9rem;
    height: 0.9rem;
  }

  .locale-toggle {
    position: absolute;
    right: 0;
    top: 0;
    min-width: 3rem;
    height: 2.1rem;
    padding: 0 0.9rem;
    border: 1px solid rgba(180, 130, 48, 0.18);
    border-radius: 999px;
    background: rgba(200, 148, 55, 0.06);
    color: rgba(220, 185, 115, 0.86);
    font-family: 'Cinzel', serif;
    font-size: 0.58rem;
    letter-spacing: 0.16em;
    text-transform: uppercase;
  }

  .locale-badge {
    display: inline-flex;
    align-items: center;
    justify-content: center;
  }

  .kicker {
    margin: 3.3rem 0 0;
    font-family: 'Cinzel', serif;
    font-size: 0.56rem;
    letter-spacing: 0.34em;
    text-transform: uppercase;
    color: rgba(205, 150, 60, 0.58);
  }

  h1 {
    margin: 0;
    font-family: 'Cinzel Decorative', serif;
    font-size: clamp(1.5rem, 4vw, 2.4rem);
    color: #e8c87a;
  }

  .content-card {
    width: min(100%, 760px);
    margin: 0 auto;
    padding: 1.1rem;
    border: 1px solid rgba(180, 130, 48, 0.16);
    border-radius: 8px;
    background: rgba(18, 11, 5, 0.82);
    box-shadow: 0 16px 50px rgba(0, 0, 0, 0.34);
  }

  button,
  a {
    transition:
      background 0.15s ease,
      border-color 0.15s ease,
      color 0.15s ease;
  }

  .back-btn:hover,
  .locale-toggle:hover {
    background: rgba(200, 148, 55, 0.12);
    border-color: rgba(200, 148, 55, 0.34);
    color: rgba(235, 204, 142, 0.95);
  }

  @media (max-width: 640px) {
    .shell {
      padding: 1rem 0.85rem 1.5rem;
    }

    .header {
      padding-top: 3rem;
    }

    .back-btn,
    .locale-toggle {
      top: 0;
    }

    .content-card {
      padding: 0.85rem;
    }
  }
</style>

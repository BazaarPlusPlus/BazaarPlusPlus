<svelte:head>
  <link rel="stylesheet" href="/fonts/fonts.css" />
</svelte:head>

<div class="grain" aria-hidden="true"></div>

<slot />

<style>
  :global(:root) {
    /* RGB triplets — combine with rgba() for alpha variations. */
    --color-accent-rgb: 200, 148, 55;
    --color-cream-rgb: 228, 216, 191;
    --color-warm-rgb: 255, 214, 140;
    --color-warm-bright-rgb: 255, 198, 98;
    --color-muted-gold-rgb: 200, 170, 120;

    /* Solid named tokens. */
    --color-bg: #0b0906;
    --color-text: #e8dcc8;
    --color-gold-text: #e8c87a;
    --color-soft-gold: #f3d38d;
    --color-gold-button: #d4a040;
    --color-pale-gold: #f6ebd1;
    --color-gold-deep: #bf852e;
    --color-gold-dark: #9e5c1e;
  }

  :global(*, *::before, *::after) {
    box-sizing: border-box;
  }

  :global(html) {
    height: 100%;
    overflow: hidden;
    overscroll-behavior: none;
  }

  :global(body) {
    margin: 0;
    height: 100%;
    overflow-y: auto;
    overscroll-behavior: none;
    background-color: var(--color-bg);
    background-image:
      radial-gradient(
        ellipse 70% 42% at 50% -4%,
        rgba(200, 130, 40, 0.18) 0%,
        transparent 70%
      ),
      radial-gradient(
        ellipse 40% 28% at 80% 85%,
        rgba(120, 60, 20, 0.1) 0%,
        transparent 60%
      );
    color: var(--color-text);
    font-family:
      'IM Fell English', 'Noto Serif SC', 'Songti SC', Georgia, serif;
    -webkit-font-smoothing: antialiased;
    user-select: none;
  }

  .grain {
    position: fixed;
    inset: 0;
    pointer-events: none;
    z-index: 100;
    opacity: 0.025;
    background-image: url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='300' height='300'%3E%3Cfilter id='g'%3E%3CfeTurbulence type='turbulence' baseFrequency='0.9' numOctaves='4' stitchTiles='stitch'/%3E%3C/filter%3E%3Crect width='300' height='300' filter='url(%23g)'/%3E%3C/svg%3E");
    background-repeat: repeat;
  }
</style>

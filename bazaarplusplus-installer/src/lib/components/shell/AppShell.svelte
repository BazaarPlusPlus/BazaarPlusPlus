<script lang="ts">
  import { page } from '$app/state';

  export let title = 'BazaarPlusPlus';
  export let navItems: { href: string; label: string }[] = [];

  function isActive(href: string, pathname: string) {
    if (href === '/') {
      return pathname === '/';
    }

    return pathname === href || pathname.startsWith(`${href}/`);
  }
</script>

<div class="app-shell">
  <header class="masthead">
    <a class="brand" href="/">
      <span class="brand-kicker">BPP</span>
      <span class="brand-title">{title}</span>
    </a>

    <nav class="nav" aria-label="Primary">
      {#each navItems as item}
        <a
          class:active={isActive(item.href, page.url.pathname)}
          href={item.href}>{item.label}</a
        >
      {/each}
    </nav>
  </header>

  <div class="page-slot">
    <slot />
  </div>
</div>

<style>
  .app-shell {
    min-height: 100vh;
    display: grid;
    grid-template-rows: auto 1fr;
  }

  .masthead {
    position: sticky;
    top: 0;
    z-index: 30;
    display: grid;
    gap: 0.9rem;
    padding: 0.9rem 1rem 0.7rem;
    backdrop-filter: blur(18px);
    background:
      linear-gradient(180deg, rgba(10, 6, 3, 0.94), rgba(10, 6, 3, 0.78)),
      radial-gradient(circle at top, rgba(184, 129, 48, 0.2), transparent 55%);
    border-bottom: 1px solid rgba(180, 130, 48, 0.14);
    box-shadow: 0 12px 36px rgba(0, 0, 0, 0.2);
  }

  .brand {
    display: inline-flex;
    align-items: baseline;
    gap: 0.55rem;
    width: fit-content;
    text-decoration: none;
  }

  .brand-kicker {
    font-family: 'Cinzel', serif;
    font-size: 0.62rem;
    letter-spacing: 0.34em;
    text-transform: uppercase;
    color: rgba(205, 150, 60, 0.62);
  }

  .brand-title {
    font-family: 'Cinzel Decorative', serif;
    font-size: clamp(1.1rem, 2vw, 1.45rem);
    color: #ebcd87;
  }

  .nav {
    display: flex;
    flex-wrap: wrap;
    gap: 0.45rem;
  }

  .nav a {
    padding: 0.58rem 0.82rem;
    border-radius: 999px;
    border: 1px solid rgba(192, 140, 54, 0.14);
    text-decoration: none;
    color: rgba(236, 223, 196, 0.72);
    background: rgba(190, 139, 58, 0.05);
    font-family: 'Cinzel', serif;
    font-size: 0.62rem;
    letter-spacing: 0.14em;
    text-transform: uppercase;
    transition:
      color 0.16s ease,
      background 0.16s ease,
      border-color 0.16s ease,
      transform 0.16s ease;
  }

  .nav a:hover,
  .nav a.active {
    color: #f4dfb7;
    border-color: rgba(219, 168, 85, 0.34);
    background: linear-gradient(
      180deg,
      rgba(197, 144, 56, 0.17),
      rgba(95, 54, 17, 0.22)
    );
    transform: translateY(-1px);
  }

  .page-slot {
    min-height: 0;
  }

  @media (max-width: 720px) {
    .masthead {
      gap: 0.75rem;
      padding: 0.8rem 0.85rem 0.65rem;
    }

    .nav {
      gap: 0.38rem;
    }

    .nav a {
      font-size: 0.56rem;
      letter-spacing: 0.12em;
      padding: 0.5rem 0.7rem;
    }
  }
</style>

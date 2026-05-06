<script lang="ts">
  import { page } from '$app/state';
  import { formatMessage, messages } from '$lib/i18n';
  import { locale } from '$lib/locale';

  export let compact = false;

  $: t = (key: keyof typeof messages.en): string => formatMessage($locale, key);
  $: navItems = [
    { href: '/', label: t('navHome') },
    { href: '/install', label: t('navInstall') },
    { href: '/stream', label: t('navStream') },
    { href: '/changelog', label: t('navChangelog') }
  ];

  function isActive(href: string, pathname: string) {
    if (href === '/') {
      return pathname === '/';
    }

    return pathname === href || pathname.startsWith(`${href}/`);
  }
</script>

<nav class:compact class="nav-strip" aria-label="Primary">
  {#each navItems as item}
    <a
      href={item.href}
      class:active={isActive(item.href, page.url.pathname)}
      >{item.label}</a
    >
  {/each}
</nav>

<style>
  .nav-strip {
    display: flex;
    flex-wrap: wrap;
    justify-content: center;
    gap: 0.42rem;
  }

  .nav-strip a {
    min-height: 2rem;
    padding: 0.4rem 0.85rem;
    display: inline-flex;
    align-items: center;
    justify-content: center;
    border: 1px solid rgba(var(--color-accent-rgb), 0.22);
    border-radius: 2px;
    background: linear-gradient(
      180deg,
      rgba(var(--color-accent-rgb), 0.12),
      rgba(var(--color-accent-rgb), 0.06)
    );
    box-shadow: 0 0 0 1px rgba(var(--color-warm-bright-rgb), 0.08) inset;
    color: rgba(var(--color-cream-rgb), 0.8);
    text-decoration: none;
    font-family: 'Cinzel', serif;
    font-size: 0.56rem;
    letter-spacing: 0.16em;
    text-transform: uppercase;
    transition:
      border-color 0.15s ease,
      background 0.15s ease,
      color 0.15s ease;
  }

  .nav-strip a:hover,
  .nav-strip a.active {
    border-color: rgba(232, 200, 122, 0.46);
    background: linear-gradient(
      180deg,
      rgba(232, 200, 122, 0.24),
      rgba(var(--color-accent-rgb), 0.12)
    );
    color: var(--color-soft-gold);
    box-shadow:
      0 0 0 1px rgba(var(--color-warm-rgb), 0.12) inset,
      0 0 14px rgba(232, 200, 122, 0.08);
  }

  .nav-strip.compact {
    justify-content: flex-start;
  }

  .nav-strip.compact a {
    min-height: 1.9rem;
    padding-inline: 0.72rem;
    font-size: 0.52rem;
    letter-spacing: 0.14em;
  }

  @media (max-width: 720px) {
    .nav-strip {
      justify-content: flex-start;
    }
  }
</style>

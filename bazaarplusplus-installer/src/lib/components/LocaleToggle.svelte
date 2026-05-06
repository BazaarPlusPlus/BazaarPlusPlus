<script lang="ts">
  import { handleLocaleToggle } from '$lib/locale';

  export let label: string;
  export let badge: string;
</script>

<button
  class="locale-toggle"
  onclick={handleLocaleToggle}
  type="button"
  aria-label={label}
  title={label}
>
  <svg class="locale-icon" viewBox="0 0 24 24" aria-hidden="true">
    <path
      d="M12 3a9 9 0 1 0 9 9a9 9 0 0 0-9-9Zm5.9 8h-2.2a14.3 14.3 0 0 0-1.2-4A7.1 7.1 0 0 1 17.9 11Zm-5.9-5.8c.7.9 1.7 2.9 2.1 5.8H9.9c.4-2.9 1.4-4.9 2.1-5.8ZM6.5 7a14.3 14.3 0 0 0-1.2 4H3.1A7.1 7.1 0 0 1 6.5 7ZM3.1 13h2.2a14.3 14.3 0 0 0 1.2 4A7.1 7.1 0 0 1 3.1 13Zm8.9 5.8c-.7-.9-1.7-2.9-2.1-5.8h4.2c-.4 2.9-1.4 4.9-2.1 5.8Zm2.5-1.8a14.3 14.3 0 0 0 1.2-4h2.2a7.1 7.1 0 0 1-3.4 4Zm-8.1-4c.1 1.4.4 2.7.9 4A12.4 12.4 0 0 1 5.9 13Zm.8-2a12.4 12.4 0 0 1 1.4-4c-.5 1.3-.8 2.6-.9 4Zm8.8 0c-.1-1.4-.4-2.7-.9-4a12.4 12.4 0 0 1 1.4 4Zm-1.4 2c.5-1.3.8-2.6.9-4a12.4 12.4 0 0 1-1.4 4Z"
      fill="currentColor"
    />
  </svg>
  <span class="locale-badge">{badge}</span>
</button>

<style>
  .locale-toggle {
    position: absolute;
    top: var(--locale-toggle-top, 0.72rem);
    right: var(--locale-toggle-right, 0.72rem);
    min-width: 3rem;
    height: 1.9rem;
    padding: 0.28rem 0.5rem;
    border: 1px solid rgba(var(--color-accent-rgb), 0.24);
    border-radius: 2px;
    background: linear-gradient(
      180deg,
      rgba(var(--color-accent-rgb), 0.12),
      rgba(var(--color-accent-rgb), 0.06)
    );
    color: rgba(var(--color-cream-rgb), 0.82);
    font-family: 'Cinzel', serif;
    font-size: 0.5rem;
    letter-spacing: 0.12em;
    text-transform: uppercase;
    display: inline-flex;
    align-items: center;
    justify-content: center;
    gap: 0.35rem;
    box-shadow: 0 0 0 1px rgba(var(--color-warm-bright-rgb), 0.08) inset;
    z-index: 2;
    cursor: pointer;
  }

  .locale-toggle:hover {
    background: linear-gradient(
      180deg,
      rgba(var(--color-accent-rgb), 0.2),
      rgba(var(--color-accent-rgb), 0.1)
    );
    border-color: rgba(var(--color-accent-rgb), 0.4);
  }

  .locale-toggle:focus-visible {
    outline: 2px solid rgba(var(--color-warm-rgb), 0.9);
    outline-offset: 2px;
  }

  .locale-icon {
    width: 0.9rem;
    height: 0.9rem;
    flex-shrink: 0;
    opacity: 0.9;
  }

  .locale-badge {
    min-width: 1.1rem;
    text-align: center;
    font-family: 'Fira Code', monospace;
    font-size: 0.62rem;
    letter-spacing: 0.05em;
  }

  @media (max-width: 720px) {
    .locale-toggle {
      top: var(--locale-toggle-top-sm, 0.58rem);
      right: var(--locale-toggle-right-sm, 0.58rem);
    }
  }
</style>

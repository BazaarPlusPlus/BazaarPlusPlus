<script lang="ts">
  import type { HomeCardTone } from '$lib/home/summary';

  export let href = '/';
  export let title = '';
  export let detail = '';
  export let tone: HomeCardTone = 'idle';
</script>

<a class:tone-active={tone === 'active'} class="status-card" {href}>
  <p class="label">{tone === 'active' ? 'Ready' : 'Standby'}</p>
  <h2>{title}</h2>
  <p class="detail">{detail}</p>
</a>

<style>
  .status-card {
    display: grid;
    gap: 0.45rem;
    padding: 1rem 1rem 1.05rem;
    text-decoration: none;
    color: inherit;
    border-radius: 3px;
    border: 1px solid rgba(184, 133, 58, 0.14);
    background:
      linear-gradient(180deg, rgba(29, 17, 9, 0.95), rgba(12, 8, 4, 0.92)),
      radial-gradient(
        circle at top right,
        rgba(211, 155, 69, 0.08),
        transparent 42%
      );
    box-shadow:
      0 0 0 1px rgba(var(--color-accent-rgb), 0.05) inset,
      0 16px 40px rgba(0, 0, 0, 0.24);
    transition:
      transform 0.16s ease,
      border-color 0.16s ease,
      box-shadow 0.16s ease;
  }

  .status-card:hover {
    transform: translateY(-2px);
    border-color: rgba(212, 159, 77, 0.28);
    box-shadow: 0 22px 48px rgba(0, 0, 0, 0.3);
  }

  .tone-active {
    border-color: rgba(212, 159, 77, 0.3);
    background:
      linear-gradient(180deg, rgba(40, 24, 12, 0.96), rgba(14, 9, 5, 0.94)),
      radial-gradient(
        circle at top right,
        rgba(226, 177, 96, 0.16),
        transparent 48%
      );
  }

  .label {
    margin: 0;
    font-family: 'Cinzel', serif;
    font-size: 0.58rem;
    letter-spacing: 0.24em;
    text-transform: uppercase;
    color: rgba(211, 158, 74, 0.72);
  }

  h2 {
    margin: 0;
    font-size: 1.15rem;
    line-height: 1.15;
    color: #f1e2be;
  }

  .detail {
    margin: 0;
    font-size: 0.92rem;
    line-height: 1.5;
    color: rgba(231, 220, 196, 0.72);
  }
</style>

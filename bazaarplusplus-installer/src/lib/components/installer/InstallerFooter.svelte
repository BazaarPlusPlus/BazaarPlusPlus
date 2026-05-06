<script lang="ts">
  export let text: string;
</script>

<footer class="footer" aria-hidden="true">
  <div class="rule">
    <span></span><span class="diamond small">+</span><span></span>
  </div>
  <p>{text}</p>
</footer>

<style>
  .rule {
    width: 100%;
    display: flex;
    align-items: center;
    gap: 0.65rem;
    color: rgba(var(--color-accent-rgb), 0.35);
  }

  .rule span:first-child,
  .rule span:last-child {
    flex: 1;
    height: 1px;
    background: linear-gradient(
      90deg,
      transparent,
      rgba(var(--color-accent-rgb), 0.3) 40%,
      rgba(var(--color-accent-rgb), 0.3) 60%,
      transparent
    );
  }

  .diamond {
    font-size: 0.55rem;
    color: rgba(205, 150, 60, 0.55);
  }

  .diamond.small {
    font-size: 0.42rem;
  }

  .footer {
    text-align: center;
    display: grid;
    gap: 0.4rem;
  }

  .footer p {
    margin: 0;
    font-family: 'Cinzel', serif;
    font-size: 0.5rem;
    letter-spacing: 0.3em;
    text-transform: uppercase;
    color: rgba(140, 110, 60, 0.35);
  }
</style>

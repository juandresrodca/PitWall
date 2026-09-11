<script lang="ts">
  import type { Snippet } from 'svelte';

  interface Props {
    n: string;
    title: string;
    area: string;
    note?: string;
    actions?: Snippet;
    children: Snippet;
  }

  let { n, title, area, note, actions, children }: Props = $props();
</script>

<section class="panel" style:grid-area={area} aria-label={title}>
  <header>
    <h2><b>{n}</b><span>{title}</span></h2>
    {#if note}<p class="note">{note}</p>{/if}
    {#if actions}<div class="actions">{@render actions()}</div>{/if}
  </header>
  <div class="body">
    {@render children()}
  </div>
</section>

<style>
  .panel {
    position: relative;
    display: flex;
    flex-direction: column;
    min-width: 0;
    min-height: 0;
    background: var(--panel);
    border: 1px solid var(--line);
    border-radius: var(--r-panel);
    overflow: hidden;
  }

  header {
    display: flex;
    align-items: center;
    gap: .75rem;
    height: 2.1rem;
    padding: 0 .85rem;
    border-bottom: 1px solid var(--line);
    flex: none;
  }

  h2 {
    display: flex;
    align-items: baseline;
    gap: .5rem;
    margin: 0;
    font-size: .8rem;
    font-weight: 700;
    letter-spacing: .12em;
    text-transform: uppercase;
    color: var(--ink);
    white-space: nowrap;
  }

  /* Numbered kickers: a navigation system, not decoration. */
  h2 b {
    font-family: var(--font-num);
    font-weight: 500;
    color: var(--cyan);
    letter-spacing: 0;
  }

  .note {
    margin: 0;
    font-size: .75rem;
    color: var(--ink-3);
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
    min-width: 0;
  }

  .actions {
    margin-left: auto;
    display: flex;
    gap: .25rem;
  }

  .body {
    position: relative;
    flex: 1;
    min-height: 0;
  }
</style>

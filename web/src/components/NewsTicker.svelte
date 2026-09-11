<script lang="ts">
  import { openExternal } from '../lib/host';
  import { weekend } from '../lib/weekend.svelte';

  const items = $derived(weekend.news.slice(0, 18));
  // Speed is constant per headline, so a longer strip takes proportionally longer.
  const duration = $derived(Math.max(60, items.reduce((s, i) => s + i.title.length, 0) * 0.22));
</script>

<footer class="ticker" aria-label="Latest Formula 1 news">
  <a class="label" href="/weekend">News</a>
  <div class="viewport">
    {#if items.length}
      <div class="strip" style:animation-duration="{duration}s">
        {#each [0, 1] as copy}
          <ul aria-hidden={copy === 1}>
            {#each items as item (item.url + copy)}
              <li>
                <button tabindex={copy === 1 ? -1 : 0} onclick={() => openExternal(item.url)}>
                  <em>{item.source}</em>{item.title}
                </button>
              </li>
            {/each}
          </ul>
        {/each}
      </div>
    {:else}
      <p>Loading headlines…</p>
    {/if}
  </div>
</footer>

<style>
  .ticker {
    display: flex;
    align-items: stretch;
    height: 2.5rem;
    border-top: 1px solid var(--line);
    background: #0e1014;
    overflow: hidden;
  }

  .label {
    display: flex;
    align-items: center;
    padding: 0 1.2rem 0 1rem;
    margin-right: .6rem;
    background: var(--red);
    color: #fff;
    font-weight: 700;
    font-size: .8rem;
    letter-spacing: .16em;
    text-transform: uppercase;
    text-decoration: none;
    clip-path: polygon(0 0, 100% 0, calc(100% - .7rem) 100%, 0 100%);
  }

  .viewport {
    position: relative;
    flex: 1;
    min-width: 0;
    overflow: hidden;
    mask-image: linear-gradient(90deg, transparent, #000 2rem, #000 calc(100% - 3rem), transparent);
  }

  .strip {
    display: flex;
    width: max-content;
    height: 100%;
    animation: scroll linear infinite;
  }

  .viewport:hover .strip { animation-play-state: paused; }

  @keyframes scroll { to { transform: translateX(-50%); } }

  ul {
    display: flex;
    align-items: center;
    margin: 0;
    padding: 0;
    list-style: none;
  }

  li { display: flex; align-items: center; }

  li::after {
    content: "";
    width: .35rem;
    height: .9rem;
    margin: 0 1.2rem;
    background: var(--line-2);
    transform: skewX(var(--skew));
  }

  button {
    padding: 0;
    border: 0;
    background: none;
    font-size: .9rem;
    color: var(--ink);
    white-space: nowrap;
    transition: color .15s ease;
  }

  button:hover { color: var(--cyan); }

  em {
    font-style: normal;
    font-size: .66rem;
    font-weight: 700;
    letter-spacing: .1em;
    text-transform: uppercase;
    color: var(--ink-3);
    margin-right: .55rem;
  }

  p { margin: 0; line-height: 2.5rem; color: var(--ink-3); font-size: .85rem; }

  /* No scrolling for reduced motion: the strip becomes a static, scrollable row. */
  @media (prefers-reduced-motion: reduce) {
    .viewport { overflow-x: auto; mask-image: none; }
    .strip ul:last-child { display: none; }
  }
</style>

<script lang="ts">
  import { feed } from '../lib/feed.svelte';
  import { drivers, prediction } from '../lib/model';
  import { weekend } from '../lib/weekend.svelte';

  /** Live prediction while a race runs (F1 TV data); otherwise the official standings. */
  const live = $derived.by(() => {
    feed.version;
    const list = drivers(feed.state);
    // Drivers missing from this session's entry list (injured, replaced) still score:
    // name them from the championship table instead.
    const byNumber = new Map((weekend.standings?.drivers ?? []).map((d) => [d.number, d.code]));
    return prediction(feed.state)
      .slice(0, 10)
      .map((p) => ({ ...p, driver: list[p.num], code: list[p.num]?.tla ?? byNumber.get(p.num) ?? `#${p.num}` }));
  });

  const standings = $derived(weekend.standings?.drivers.slice(0, 10) ?? []);
  const leader = $derived(standings[0]?.points ?? 0);
</script>

<div class="champ">
  {#if live.length}
    <p class="cap">Live projection if the race ended now</p>
    <ol>
      {#each live as p (p.num)}
        <li>
          <span class="pos num">{p.predicted}</span>
          <span class="who"><i style:background={p.driver?.color ?? 'var(--ink-3)'}></i>{p.code}</span>
          <span class="move num" class:up={p.predicted < p.current} class:down={p.predicted > p.current}>
            {#if p.predicted !== p.current}{p.predicted < p.current ? '▲' : '▼'}{Math.abs(p.current - p.predicted)}{/if}
          </span>
          <span class="pts num">{p.predictedPoints}<small>+{p.predictedPoints - p.points}</small></span>
        </li>
      {/each}
    </ol>
  {:else if standings.length}
    <p class="cap">Drivers' championship after round {weekend.standings?.round}</p>
    <ol>
      {#each standings as d (d.code)}
        <li>
          <span class="pos num">{d.position}</span>
          <span class="who">{d.code}<small>{d.team}</small></span>
          <span class="gap num">{d.position === 1 ? '' : `−${leader - d.points}`}</span>
          <span class="pts num">{d.points}</span>
        </li>
      {/each}
    </ol>
  {:else}
    <p class="cap">Standings load from the Jolpica F1 API.</p>
  {/if}
</div>

<style>
  .champ {
    position: absolute;
    inset: 0;
    padding: .5rem .85rem;
    overflow-y: auto;
  }

  .cap {
    margin: 0 0 .3rem;
    font-size: .66rem;
    color: var(--ink-3);
    text-transform: uppercase;
    letter-spacing: .1em;
  }

  ol { margin: 0; padding: 0; list-style: none; }

  li {
    display: grid;
    grid-template-columns: 1.6rem 1fr 3rem 4.4rem;
    align-items: center;
    gap: .5rem;
    padding: .2rem 0;
    border-bottom: 1px solid var(--line);
    font-size: .88rem;
  }

  .pos { font-weight: 700; color: var(--ink-2); }

  .who {
    display: flex;
    align-items: baseline;
    gap: .4rem;
    font-weight: 700;
    min-width: 0;
  }

  .who i { align-self: center; width: .22rem; height: .8rem; transform: skewX(var(--skew)); flex: none; }

  .who small {
    font-weight: 400;
    font-size: .7rem;
    color: var(--ink-3);
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }

  .gap, .move { font-size: .74rem; color: var(--ink-3); text-align: right; }
  .move.up { color: var(--green); }
  .move.down { color: var(--soft); }

  .pts { text-align: right; font-weight: 700; }
  .pts small { font-size: .65rem; color: var(--green); margin-left: .2rem; font-weight: 500; }
</style>

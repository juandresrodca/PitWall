<script lang="ts">
  import { flip } from 'svelte/animate';
  import { cubicOut } from 'svelte/easing';
  import { feed } from '../lib/feed.svelte';
  import { rows, sessionKind, type Row } from '../lib/model';
  import DriverTag from './DriverTag.svelte';
  import TyreBadge from './TyreBadge.svelte';

  interface Props {
    selected: string | null;
    onSelect: (num: string | null) => void;
  }

  let { selected, onSelect }: Props = $props();

  const kind = $derived.by(() => {
    feed.version;
    return sessionKind(feed.state);
  });

  const list = $derived.by(() => {
    feed.version;
    return rows(feed.state);
  });

  /** Qualifying: the last safe position in the current part (drivers below it are in the drop zone). */
  const cutoff = $derived.by(() => {
    feed.version;
    const td = feed.state.TimingData;
    const part = Number(td?.SessionPart ?? 0);
    const entries = td?.NoEntries;
    if (kind !== 'Qualifying' || !Array.isArray(entries) || part < 1 || part >= entries.length) return 0;
    return Number(entries[part]) || 0;
  });

  const race = $derived(kind === 'Race');

  function segmentClass(status: number): string {
    if (status === 2051) return 'purple';
    if (status === 2049) return 'green';
    if (status === 2064) return 'pit';
    if (status >= 2048) return 'yellow';
    return '';
  }

  /** Within a second of the car ahead: close enough to use overtake mode. */
  function close(row: Row): boolean {
    const v = parseFloat(row.interval.replace('+', ''));
    return race && Number.isFinite(v) && v > 0 && v < 1;
  }

  const motionOk = typeof window !== 'undefined' && matchMedia('(prefers-reduced-motion: no-preference)').matches;
</script>

<div class="tower" class:race role="table" aria-label="Timing">
  <div class="row head" role="row">
    <span role="columnheader">Pos</span>
    <span role="columnheader">Driver</span>
    {#if race}
      <span role="columnheader" title="Places gained or lost since the start">+/−</span>
      <span role="columnheader" class="r">Int</span>
      <span role="columnheader" class="r">Gap</span>
      <span role="columnheader" class="r">Last</span>
      <span role="columnheader" class="r">Best</span>
    {:else}
      <span role="columnheader" class="r">Best</span>
      <span role="columnheader" class="r">Gap</span>
      <span role="columnheader" class="r">Int</span>
      <span role="columnheader" class="r">Last</span>
    {/if}
    <span role="columnheader">S1</span>
    <span role="columnheader">S2</span>
    <span role="columnheader">S3</span>
    <span role="columnheader">Tyre</span>
    <span role="columnheader" class="r">{race ? 'Pit' : 'Laps'}</span>
  </div>

  {#if list.length === 0}
    <div class="empty">
      <p>Waiting for timing data</p>
      <span>The tower fills in as soon as Formula 1's feed publishes the session.</span>
    </div>
  {/if}

  <div class="rows">
    {#each list as row (row.num)}
      <div
        class="row"
        class:sel={selected === row.num}
        class:out={row.out || row.knockedOut}
        class:cut={cutoff > 0 && row.line === cutoff}
        role="row"
        tabindex="0"
        animate:flip={{ duration: motionOk ? 450 : 0, easing: cubicOut }}
        onclick={() => onSelect(selected === row.num ? null : row.num)}
        onkeydown={(e) => (e.key === 'Enter' || e.key === ' ') && onSelect(selected === row.num ? null : row.num)}
      >
        <span class="pos num">{row.position || '–'}</span>
        <span class="drv"><DriverTag driver={row.driver} /></span>

        {#if race}
          <span class="delta num" class:up={(row.gridDelta ?? 0) > 0} class:down={(row.gridDelta ?? 0) < 0}>
            {#if row.gridDelta}{row.gridDelta > 0 ? '▲' : '▼'}{Math.abs(row.gridDelta)}{/if}
          </span>
          <span class="t num r" class:close={close(row)}>{row.interval}</span>
          <span class="t num r dim">{row.gap}</span>
          {#key row.last.value}
            <span class="t num r lap {row.last.mark}">{row.last.value}</span>
          {/key}
          <span class="t num r {row.best.mark}">{row.best.value}</span>
        {:else}
          {#key row.best.value}
            <span class="t num r lap strong {row.best.mark}">{row.best.value}</span>
          {/key}
          <span class="t num r">{row.gap}</span>
          <span class="t num r dim">{row.interval}</span>
          <span class="t num r {row.last.mark}">{row.last.value}</span>
        {/if}

        {#each row.sectors as sector}
          <span class="sector">
            <span class="sv num {sector.mark}">{sector.value}</span>
            <span class="segs" aria-hidden="true">
              {#each sector.segments as seg}<i class={segmentClass(seg)}></i>{/each}
            </span>
          </span>
        {/each}

        <span class="tyre">
          {#if row.tyre.compound !== 'UNKNOWN'}
            <TyreBadge compound={row.tyre.compound} used={!row.tyre.isNew} />
            <span class="age num">{row.tyre.age}</span>
          {/if}
        </span>

        <span class="pit r">
          {#if row.out}
            <em class="flagchip ret">{row.knockedOut ? 'KO' : 'Out'}</em>
          {:else if row.inPit}
            <em class="flagchip in">In pit</em>
          {:else if row.pitOut}
            <em class="flagchip exit">Pit exit</em>
          {:else if race}
            <span class="num dim">{row.stops || ''}</span>
          {:else}
            <span class="num dim">{row.laps || ''}</span>
          {/if}
        </span>
      </div>
    {/each}
  </div>
</div>

<style>
  .tower {
    --cols: 2rem 6.6rem 1.2fr 1fr 1fr 1.2fr .9fr .9fr .9fr 3.3rem 3.3rem;
    display: flex;
    flex-direction: column;
    height: 100%;
  }

  .tower.race {
    --cols: 2rem 6.6rem 2rem 1fr 1fr 1.2fr 1.2fr .9fr .9fr .9fr 3.3rem 3.3rem;
  }

  .rows {
    flex: 1;
    min-height: 0;
    display: flex;
    flex-direction: column;
  }

  .row {
    display: grid;
    grid-template-columns: var(--cols);
    align-items: center;
    column-gap: .5rem;
    flex: 1 1 0;
    min-height: 1.9rem;
    max-height: 2.9rem;
    padding: 0 .85rem;
    border-bottom: 1px solid var(--line);
    font-size: .98rem;
    cursor: pointer;
    background: var(--panel);
    transition: background-color .2s ease;
  }

  .row:nth-child(even) { background: #14161c; }
  .row:hover { background: var(--panel-3); }

  .row.sel {
    background: color-mix(in oklab, var(--cyan) 12%, var(--panel));
    box-shadow: inset 3px 0 0 var(--cyan);
  }

  .row.out { opacity: .45; }

  /* Qualifying drop zone: a red rule under the last safe position. */
  .row.cut { border-bottom: 2px solid var(--red); }

  .head {
    flex: none;
    height: 1.9rem;
    min-height: 1.9rem;
    font-size: .68rem;
    font-weight: 700;
    letter-spacing: .12em;
    text-transform: uppercase;
    color: var(--ink-3);
    cursor: default;
    background: var(--panel) !important;
  }

  .r { text-align: right; justify-self: end; }

  .pos {
    font-weight: 700;
    font-size: 1.05rem;
    text-align: center;
  }

  .drv { min-width: 0; }

  .delta {
    font-size: .75rem;
    color: var(--ink-3);
  }

  .delta.up { color: var(--green); }
  .delta.down { color: var(--soft); }

  .t { font-size: .95rem; white-space: nowrap; }
  .dim { color: var(--ink-2); }
  .strong { font-weight: 700; }

  .t.close {
    color: var(--ink);
    box-shadow: inset -3px 0 0 var(--cyan);
    padding-right: .4rem;
  }

  .purple { color: var(--purple); }
  .green { color: var(--green); }
  .yellow { color: var(--ink); }

  /* A new lap time lights up once: purple and green are worth a glance, yellow is not. */
  .lap.purple, .lap.green { animation: flash 1.4s var(--ease-out); }

  @keyframes flash {
    from { background: color-mix(in oklab, currentColor 28%, transparent); }
    to { background: transparent; }
  }

  .sector {
    display: flex;
    flex-direction: column;
    gap: .18rem;
    min-width: 0;
  }

  .sv {
    font-size: .8rem;
    line-height: 1;
    height: .85rem;
  }

  .sv.yellow { color: var(--yellow); }

  .segs {
    display: flex;
    gap: 2px;
    height: .32rem;
  }

  .segs i {
    flex: 1;
    border-radius: 1px;
    background: var(--line-2);
  }

  .segs i.yellow { background: var(--yellow); }
  .segs i.green { background: var(--green); }
  .segs i.purple { background: var(--purple); }
  .segs i.pit { background: #3b6e8f; }

  .tyre {
    display: flex;
    align-items: center;
    gap: .35rem;
  }

  .age {
    font-size: .8rem;
    color: var(--ink-2);
  }

  .flagchip {
    font-style: normal;
    font-size: .64rem;
    font-weight: 700;
    letter-spacing: .08em;
    text-transform: uppercase;
    padding: .12rem .35rem;
    border-radius: 2px;
    white-space: nowrap;
  }

  .flagchip.in { background: #3b6e8f; color: #fff; }
  .flagchip.exit { background: color-mix(in oklab, var(--cyan) 22%, transparent); color: var(--cyan); }
  .flagchip.ret { background: var(--panel-3); color: var(--ink-2); }

  .empty {
    position: absolute;
    inset: 3rem 1rem auto;
    text-align: center;
    color: var(--ink-2);
  }

  .empty p { margin: 0 0 .3rem; font-size: 1.1rem; font-weight: 700; color: var(--ink); }
</style>

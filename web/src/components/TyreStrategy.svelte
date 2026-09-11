<script lang="ts">
  import { feed } from '../lib/feed.svelte';
  import { compoundLetter, rows, stints, stintTrend, type Compound } from '../lib/model';
  import TyreBadge from './TyreBadge.svelte';

  interface Props {
    selected: string | null;
    onSelect: (num: string | null) => void;
  }

  let { selected, onSelect }: Props = $props();

  const names: Record<Compound, string> = {
    SOFT: 'Soft', MEDIUM: 'Medium', HARD: 'Hard', INTERMEDIATE: 'Inter', WET: 'Wet', UNKNOWN: '?',
  };

  const view = $derived.by(() => {
    feed.version;
    const order = rows(feed.state);
    const lines = order.map((r) => ({
      row: r,
      stints: stints(feed.state, r.num),
      trend: stintTrend(feed.laps[r.num], r.tyre.stintLaps),
    }));
    const raceLaps = Number(feed.state.LapCount?.TotalLaps) || 0;
    const longest = Math.max(1, ...lines.map((l) => l.stints.reduce((s, x) => s + x.laps, 0)));
    const total = Math.max(raceLaps, longest);
    const used = new Set<Compound>();
    for (const l of lines) for (const s of l.stints) used.add(s.compound);
    const step = total > 40 ? 10 : 5;
    const ticks: number[] = [];
    for (let t = step; t < total; t += step) ticks.push(t);
    return { lines, total, ticks, used: [...used].filter((c) => c !== 'UNKNOWN') };
  });

  function trendClass(slope: number): string {
    if (slope < 0.03) return 'good';
    if (slope < 0.1) return 'warn';
    return 'bad';
  }
</script>

<div class="strategy">
  <div class="legend" aria-label="Compounds">
    {#each view.used as c}
      <span><TyreBadge compound={c} size=".95rem" />{names[c]}</span>
    {/each}
    <span class="hint">Dimmed stints started on used tyres · trend = s/lap over clean laps</span>
  </div>

  <div class="grid">
    <div class="axis" aria-hidden="true">
      {#each view.ticks as t}
        <span style:left="{(t / view.total) * 100}%">{t}</span>
      {/each}
    </div>

    {#each view.lines as line (line.row.num)}
      <button
        class="line"
        class:sel={selected === line.row.num}
        class:out={line.row.out}
        onclick={() => onSelect(selected === line.row.num ? null : line.row.num)}
      >
        <span class="who">
          <i style:background={line.row.driver.color} aria-hidden="true"></i>{line.row.driver.tla}
        </span>
        <span class="track">
          {#each view.ticks as t}<b style:left="{(t / view.total) * 100}%" aria-hidden="true"></b>{/each}
          {#each line.stints as s, i}
            <span
              class="stint {s.compound.toLowerCase()}"
              class:used={!s.isNew}
              style:width="{(Math.max(s.laps, 0.6) / view.total) * 100}%"
              title="Stint {i + 1}: {names[s.compound]}, {s.laps} laps{s.isNew ? '' : ', used set'}"
            >
              {#if s.laps / view.total > 0.07}<em>{compoundLetter[s.compound]}{s.laps}</em>{/if}
            </span>
          {/each}
        </span>
        <span class="now num">
          {#if line.row.tyre.compound !== 'UNKNOWN'}{line.row.tyre.age}L{/if}
        </span>
        <span class="trend num">
          {#if line.trend}
            <i class={trendClass(line.trend.slope)} aria-hidden="true"></i>{line.trend.slope >= 0 ? '+' : ''}{line.trend.slope.toFixed(2)}
          {/if}
        </span>
      </button>
    {/each}
  </div>
</div>

<style>
  .strategy {
    position: absolute;
    inset: 0;
    display: flex;
    flex-direction: column;
    padding: .4rem .8rem .5rem;
  }

  .legend {
    display: flex;
    align-items: center;
    gap: .9rem;
    font-size: .72rem;
    color: var(--ink-2);
    height: 1.4rem;
    flex: none;
  }

  .legend span { display: inline-flex; align-items: center; gap: .3rem; }
  .legend .hint { margin-left: auto; color: var(--ink-3); }

  .grid {
    --who: 3.4rem;
    --tail: 6rem;
    position: relative;
    flex: 1;
    min-height: 0;
    display: flex;
    flex-direction: column;
    padding-top: 1rem;
  }

  .axis {
    position: absolute;
    top: 0;
    left: calc(var(--who) + .5rem);
    right: calc(var(--tail) + .5rem);
    height: 1rem;
    font-family: var(--font-num);
    font-size: .62rem;
    color: var(--ink-3);
  }

  .axis span { position: absolute; transform: translateX(-50%); }

  .line {
    display: grid;
    grid-template-columns: var(--who) 1fr 2.4rem 3.6rem;
    align-items: center;
    column-gap: .5rem;
    flex: 1 1 0;
    min-height: 0;
    max-height: 1.6rem;
    padding: 0;
    background: none;
    border: 0;
    text-align: left;
    border-radius: 2px;
    transition: background-color .2s ease;
  }

  .line:hover { background: var(--panel-2); }
  .line.sel { background: color-mix(in oklab, var(--cyan) 12%, transparent); }
  .line.out { opacity: .45; }

  .who {
    display: flex;
    align-items: center;
    gap: .35rem;
    font-size: .74rem;
    font-weight: 700;
    letter-spacing: .04em;
    line-height: 1;
  }

  .who i { width: .22rem; height: .8rem; transform: skewX(var(--skew)); }

  .track {
    position: relative;
    display: flex;
    gap: 2px;
    height: 58%;
    min-height: .5rem;
    max-height: .95rem;
  }

  .track b {
    position: absolute;
    top: -2px;
    bottom: -2px;
    width: 1px;
    background: var(--line);
  }

  .stint {
    --c: var(--ink-3);
    position: relative;
    display: flex;
    align-items: center;
    padding-left: .3rem;
    background: var(--c);
    border-radius: 1px 3px 3px 1px;
    min-width: 3px;
  }

  .stint.used { background: color-mix(in oklab, var(--c) 55%, var(--panel)); }

  .stint em {
    font-style: normal;
    font-family: var(--font-num);
    font-size: .6rem;
    font-weight: 700;
    color: #0b0c0f;
    line-height: 1;
  }

  .soft { --c: var(--soft); }
  .medium { --c: var(--medium); }
  .hard { --c: var(--hard); }
  .intermediate { --c: var(--inter); }
  .wet { --c: var(--wet); }
  .soft em, .wet em { color: #fff; }

  .now { font-size: .75rem; color: var(--ink-2); text-align: right; }

  .trend {
    display: flex;
    align-items: center;
    justify-content: flex-end;
    gap: .3rem;
    font-size: .75rem;
    color: var(--ink);
  }

  .trend i { width: .45rem; height: .45rem; border-radius: 50%; }
  .trend .good { background: var(--green); }
  .trend .warn { background: var(--yellow); }
  .trend .bad { background: var(--soft); }
</style>

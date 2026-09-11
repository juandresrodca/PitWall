<script lang="ts">
  import { feed } from '../lib/feed.svelte';
  import { lapTime, rows, stintTrend } from '../lib/model';
  import DriverTag from './DriverTag.svelte';
  import TyreBadge from './TyreBadge.svelte';

  interface Props {
    num: string;
    onClose: () => void;
  }

  let { num, onClose }: Props = $props();

  const row = $derived.by(() => {
    feed.version;
    return rows(feed.state).find((r) => r.num === num) ?? null;
  });

  const tel = $derived.by(() => {
    feed.motion;
    return feed.telemetry.cars[num] ?? null; // [rpm, speed, gear, throttle, brake]
  });

  const laps = $derived.by(() => {
    feed.version;
    return (feed.laps[num] ?? []).filter((l) => !(l.f & 1)).slice(-15);
  });

  const trend = $derived.by(() => {
    feed.version;
    return row ? stintTrend(feed.laps[num], row.tyre.stintLaps) : null;
  });

  /** Lap-time sparkline: one series, so no legend; the newest lap is labelled at the end. */
  const spark = $derived.by(() => {
    if (laps.length < 2) return null;
    const w = 200;
    const h = 40;
    const times = laps.map((l) => l.t);
    const min = Math.min(...times);
    const max = Math.max(...times);
    const span = Math.max(max - min, 200);
    const pts = laps.map((l, i) => [4 + (i / (laps.length - 1)) * (w - 8), 4 + (1 - (l.t - min) / span) * (h - 8)]);
    return {
      w,
      h,
      d: pts.map(([x, y], i) => `${i ? 'L' : 'M'}${x.toFixed(1)} ${y.toFixed(1)}`).join(''),
      end: pts[pts.length - 1],
      last: laps[laps.length - 1],
      best: min,
    };
  });
</script>

{#if row}
  <aside class="focus" aria-label="Driver focus: {row.driver.name}">
    <header>
      <DriverTag driver={row.driver} />
      <span class="name">{row.driver.last || row.driver.name}</span>
      <span class="p num">P{row.position}</span>
      <button class="x" onclick={onClose} aria-label="Close driver focus">×</button>
    </header>

    {#if tel}
      <div class="tel">
        <div class="speed">
          <span class="big num">{tel[1]}</span><span class="u">km/h</span>
        </div>
        <div class="gear"><span class="u">Gear</span><span class="num">{tel[2] || 'N'}</span></div>
        <div class="bars">
          <label><span>Throttle</span><i class="thr" style:transform="scaleX({Math.min(100, tel[3]) / 100})"></i></label>
          <label><span>Brake</span><i class="brk" style:transform="scaleX({tel[4] > 0 ? 1 : 0})"></i></label>
          <label><span>RPM</span><i class="rpm" style:transform="scaleX({Math.min(1, tel[0] / 13000)})"></i></label>
        </div>
      </div>
    {/if}

    <dl class="facts">
      <div><dt>Last</dt><dd class="num">{row.last.value || '—'}</dd></div>
      <div><dt>Best</dt><dd class="num">{row.best.value || '—'}</dd></div>
      <div><dt>{row.interval ? 'Interval' : 'Gap'}</dt><dd class="num">{row.interval || row.gap || '—'}</dd></div>
      <div><dt>Speed trap</dt><dd class="num">{row.speedTrap ? `${row.speedTrap} km/h` : '—'}</dd></div>
      <div class="tyre">
        <dt>Tyre</dt>
        <dd>
          {#if row.tyre.compound !== 'UNKNOWN'}
            <TyreBadge compound={row.tyre.compound} size="1.1rem" used={!row.tyre.isNew} />
            <span class="num">{row.tyre.age} laps · stint {row.tyre.stint}</span>
          {:else}—{/if}
        </dd>
      </div>
      <div>
        <dt title="Least-squares lap-time trend over the clean laps of this stint; fuel burn not removed">Pace trend</dt>
        <dd class="num">{trend ? `${trend.slope >= 0 ? '+' : ''}${trend.slope.toFixed(2)} s/lap` : '—'}</dd>
      </div>
    </dl>

    {#if spark}
      <figure>
        <figcaption>Last {laps.length} laps</figcaption>
        <div class="plot">
          <svg viewBox="0 0 {spark.w} {spark.h}" preserveAspectRatio="none" role="img"
            aria-label="Lap times, latest {lapTime(spark.last.t)}">
            <path d={spark.d} />
          </svg>
          <!-- End marker drawn in HTML so the stretched SVG cannot squash it into an ellipse. -->
          <i class="dot" style:left="{(spark.end[0] / spark.w) * 100}%" style:top="{(spark.end[1] / spark.h) * 100}%"></i>
        </div>
        <span class="end num">{lapTime(spark.last.t)}</span>
      </figure>
    {/if}
  </aside>
{/if}

<style>
  .focus {
    position: absolute;
    left: .8rem;
    bottom: 1.8rem;
    width: 15.5rem;
    padding: .7rem .8rem .8rem;
    background: var(--panel-2);
    box-shadow: 0 12px 32px rgba(0, 0, 0, .45);
    border: 1px solid var(--line-2);
    border-top: 2px solid var(--cyan);
    border-radius: var(--r-ctl);
    animation: rise .25s var(--ease-out);
    z-index: 2;
  }

  @keyframes rise { from { opacity: 0; transform: translateY(8px); } }

  header {
    display: flex;
    align-items: center;
    gap: .5rem;
  }

  .name {
    font-weight: 600;
    color: var(--ink-2);
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  .p {
    margin-left: auto;
    font-weight: 700;
  }

  .x {
    width: 1.5rem;
    height: 1.5rem;
    border: 1px solid var(--line-2);
    border-radius: var(--r-ctl);
    background: transparent;
    line-height: 1;
    transition: transform .16s var(--ease-out);
  }

  .x:active { transform: scale(.97); }

  .tel {
    display: grid;
    grid-template-columns: auto auto 1fr;
    gap: .5rem;
    align-items: center;
    margin-top: .6rem;
    padding: .5rem 0;
    border-top: 1px solid var(--line);
    border-bottom: 1px solid var(--line);
  }

  .big { font-size: 1.7rem; font-weight: 700; line-height: 1; }
  .u { display: block; font-size: .62rem; color: var(--ink-3); text-transform: uppercase; letter-spacing: .1em; }
  .gear .num { font-size: 1.6rem; font-weight: 700; line-height: 1; }

  .bars { display: grid; gap: .25rem; }
  .bars label { display: grid; grid-template-columns: 3.5rem 1fr; align-items: center; font-size: .62rem; color: var(--ink-3); text-transform: uppercase; letter-spacing: .06em; }
  .bars i {
    display: block;
    height: .4rem;
    border-radius: 0 2px 2px 0;
    transform-origin: 0 50%;
    transition: transform .2s linear;
    background: var(--ink-2);
  }
  .bars .thr { background: var(--green); }
  .bars .brk { background: var(--soft); }
  .bars .rpm { background: var(--cyan); }

  .facts {
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: .45rem .8rem;
    margin: .6rem 0 0;
  }

  .facts div { min-width: 0; }
  .facts .tyre { grid-column: span 2; }
  dt { font-size: .62rem; color: var(--ink-3); text-transform: uppercase; letter-spacing: .1em; }
  dd { margin: 0; display: flex; align-items: center; gap: .4rem; font-size: .92rem; }

  figure {
    position: relative;
    margin: .6rem 0 0;
    padding-top: .5rem;
    border-top: 1px solid var(--line);
  }

  figcaption { font-size: .62rem; color: var(--ink-3); text-transform: uppercase; letter-spacing: .1em; }

  svg { display: block; width: 100%; height: 2.5rem; overflow: visible; }
  .plot { margin-top: .2rem; }
  svg path { vector-effect: non-scaling-stroke; }
  svg path { fill: none; stroke: var(--cyan); stroke-width: 2; stroke-linejoin: round; stroke-linecap: round; }
  .plot { position: relative; }

  .dot {
    position: absolute;
    width: 8px;
    height: 8px;
    margin: -4px 0 0 -4px;
    border-radius: 50%;
    background: var(--cyan);
    box-shadow: 0 0 0 2px var(--panel-2);
  }

  .end {
    position: absolute;
    right: 0;
    top: .45rem;
    font-size: .8rem;
    color: var(--ink);
  }
</style>

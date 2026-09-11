<script lang="ts">
  import { feed } from '../lib/feed.svelte';
  import { getJson } from '../lib/api';
  import { drivers, rows, trackState } from '../lib/model';
  import { authUi } from '../lib/auth.svelte';
  import type { TrackMap } from '../lib/types';

  interface Props {
    selected: string | null;
    onSelect: (num: string | null) => void;
  }

  let { selected, onSelect }: Props = $props();

  let box = $state({ w: 0, h: 0 });
  let map = $state<TrackMap | null>(null);
  let loadedFor = '';

  // Fetch the outline whenever the session changes.
  $effect(() => {
    const path = feed.status.sessionPath ?? '';
    if (!path || path === loadedFor) return;
    loadedFor = path;
    map = null;
    let attempts = 0;
    const load = async () => {
      const result = await getJson<TrackMap>(`/api/track?path=${encodeURIComponent(path)}`);
      if (loadedFor !== path) return;
      if (result) map = result;
      else if (++attempts < 6) setTimeout(load, 5000); // the server may still be building it
    };
    load();
  });

  /**
   * Pick the rotation that lets the circuit fill the panel, then map feed coordinates
   * (Y up) to panel pixels (Y down).
   */
  const frame = $derived.by(() => {
    if (!map || box.w < 10 || box.h < 10) return null;
    const pts = map.points;
    // With a driver selected, the focus card sits on the left: fit the circuit beside it.
    const rem = parseFloat(getComputedStyle(document.documentElement).fontSize) || 16;
    const reserve = selected && box.w > rem * 26 ? rem * 16.6 : 0;
    const width = box.w - reserve;
    const pad = Math.min(width, box.h) * 0.08;
    const labelRoom = rem * 2.4; // TLA labels sit to the right of each dot
    let best = { angle: 0, scale: 0, minX: 0, minY: 0, w: 0, h: 0 };
    for (let deg = 0; deg < 180; deg += 3) {
      const a = (deg * Math.PI) / 180;
      const c = Math.cos(a);
      const s = Math.sin(a);
      let minX = Infinity, maxX = -Infinity, minY = Infinity, maxY = -Infinity;
      for (const [x, y] of pts) {
        const rx = x * c - y * s;
        const ry = -(x * s + y * c);
        if (rx < minX) minX = rx;
        if (rx > maxX) maxX = rx;
        if (ry < minY) minY = ry;
        if (ry > maxY) maxY = ry;
      }
      const w = maxX - minX;
      const h = maxY - minY;
      const scale = Math.min((width - pad * 2 - labelRoom) / w, (box.h - pad * 2) / h);
      if (scale > best.scale) best = { angle: a, scale, minX, minY, w, h };
    }
    const { angle, scale, minX, minY, w, h } = best;
    const c = Math.cos(angle);
    const s = Math.sin(angle);
    const ox = reserve + (width - labelRoom - w * scale) / 2;
    const oy = (box.h - h * scale) / 2;
    const project = (x: number, y: number): [number, number] => [
      ox + (x * c - y * s - minX) * scale,
      oy + (-(x * s + y * c) - minY) * scale,
    ];
    const path = pts.map(([x, y], i) => {
      const [px, py] = project(x, y);
      return `${i ? 'L' : 'M'}${px.toFixed(1)} ${py.toFixed(1)}`;
    }).join('') + 'Z';
    return { project, path };
  });

  const tone = $derived.by(() => {
    feed.version;
    return trackState(feed.state).tone;
  });

  const cars = $derived.by(() => {
    feed.motion;
    feed.version;
    if (!frame) return [];
    const list = drivers(feed.state);
    const order = rows(feed.state);
    const place: Record<string, { pos: number; inPit: boolean; out: boolean }> = {};
    for (const r of order) place[r.num] = { pos: parseInt(r.position, 10) || 99, inPit: r.inPit, out: r.out };

    return Object.entries(feed.positions.cars)
      .filter(([n, v]) => list[n] && v && !(v[0] === 0 && v[1] === 0) && !place[n]?.out)
      .map(([n, v]) => {
        const [x, y] = frame.project(v[0], v[1]);
        return { num: n, x, y, color: list[n].color, tla: list[n].tla, pos: place[n]?.pos ?? 99, pit: place[n]?.inPit ?? false };
      })
      .sort((a, b) => b.pos - a.pos); // leader drawn last, on top
  });

  const hasPositions = $derived(feed.status.subscriberData || cars.length > 0);
</script>

<div class="map" bind:clientWidth={box.w} bind:clientHeight={box.h}>
  {#if frame}
    <svg width={box.w} height={box.h} viewBox="0 0 {box.w} {box.h}" role="img" aria-label="Track map">
      <path class="edge" d={frame.path} />
      <path class="road {tone}" d={frame.path} />
      {#each cars as car (car.num)}
        <g
          class="car"
          class:lead={car.pos === 1}
          class:sel={selected === car.num}
          class:pit={car.pit}
          style:transform="translate({car.x}px, {car.y}px)"
          style:--team={car.color}
          onclick={() => onSelect(selected === car.num ? null : car.num)}
          role="button"
          tabindex="-1"
          aria-label="{car.tla}, P{car.pos}"
        >
          <circle r={selected === car.num ? 11 : 8} />
          <text x="13" y="4">{car.tla}</text>
        </g>
      {/each}
    </svg>
  {:else}
    <p class="placeholder">
      {feed.status.sessionPath ? 'Drawing the circuit from archived car positions…' : 'The circuit appears once a session is loaded.'}
    </p>
  {/if}

  {#if frame && !hasPositions}
    <div class="notice">
      <strong>Live car positions need F1 TV</strong>
      <span>Formula 1 only streams the driver tracker to subscribers.</span>
      <button onclick={() => authUi.show('manual')}>Connect F1 TV</button>
    </div>
  {/if}

  {#if map}
    <p class="credit">Outline built from {map.builtFrom} positions</p>
  {/if}
</div>

<style>
  .map {
    position: absolute;
    inset: 0;
    overflow: hidden;
    background:
      radial-gradient(circle at 50% 45%, #161920 0%, transparent 70%),
      var(--panel);
  }

  svg { display: block; }

  .edge {
    fill: none;
    stroke: #0b0c0f;
    stroke-width: 14;
    stroke-linejoin: round;
  }

  .road {
    --tone: #3a404c;
    fill: none;
    stroke: var(--tone);
    stroke-width: 6;
    stroke-linejoin: round;
    transition: stroke .5s ease;
  }

  .road.yellow { --tone: var(--flag-yellow); }
  .road.sc { --tone: var(--flag-sc); }
  .road.red { --tone: var(--flag-red); }

  .car {
    transition: transform .45s linear;
    cursor: pointer;
  }

  .car circle {
    fill: var(--team);
    stroke: #0b0c0f;
    stroke-width: 2;
  }

  .car text {
    font-family: var(--font-ui);
    font-size: 12px;
    font-weight: 700;
    fill: var(--ink);
    paint-order: stroke;
    stroke: #0b0c0f;
    stroke-width: 3px;
    letter-spacing: .04em;
  }

  .car.pit { opacity: .5; }
  .car.lead circle { stroke: #fff; }
  .car.sel circle { stroke: var(--cyan); stroke-width: 3; }
  .car.sel text { font-size: 14px; }

  .placeholder {
    position: absolute;
    inset: 0;
    display: grid;
    place-items: center;
    margin: 0;
    color: var(--ink-3);
  }

  .notice {
    position: absolute;
    right: .8rem;
    top: .8rem;
    display: flex;
    flex-direction: column;
    gap: .2rem;
    max-width: 17rem;
    padding: .7rem .8rem;
    background: color-mix(in oklab, var(--panel-2) 92%, transparent);
    border: 1px solid var(--line-2);
    border-left: 3px solid var(--red);
    border-radius: var(--r-ctl);
    font-size: .8rem;
    color: var(--ink-2);
  }

  .notice strong { color: var(--ink); font-size: .85rem; }

  .notice button {
    align-self: flex-start;
    margin-top: .4rem;
    padding: .3rem .7rem;
    background: var(--red);
    border: 0;
    border-radius: var(--r-ctl);
    color: #fff;
    font-weight: 700;
    font-size: .78rem;
    transition: transform .16s var(--ease-out);
  }

  .notice button:active { transform: scale(.97); }

  .credit {
    position: absolute;
    left: .8rem;
    bottom: .5rem;
    margin: 0;
    font-size: .66rem;
    color: var(--ink-3);
  }
</style>

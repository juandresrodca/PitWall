<script lang="ts">
  import { feed } from '../lib/feed.svelte';
  import { poll } from '../lib/api';
  import { weather } from '../lib/model';
  import { weekend } from '../lib/weekend.svelte';
  import type { Forecast } from '../lib/types';

  let forecast = $state<Forecast | null>(null);
  $effect(() => poll<Forecast>('/api/forecast', 20 * 60_000, (v) => (forecast = v)));

  const live = $derived.by(() => {
    feed.version;
    return weather(feed.state);
  });

  const HOURS = 18;

  /** Next 18 hours: rain probability as bars; temperature as labels (never a second axis). */
  const hours = $derived.by(() => {
    const h = forecast?.hourly;
    if (!h?.time?.length) return [];
    const nowSec = weekend.now / 1000;
    const start = h.time.findIndex((t) => t + 3600 > nowSec);
    if (start < 0) return [];
    return h.time.slice(start, start + HOURS).map((t, i) => ({
      t: t * 1000,
      rain: h.precipitation_probability[start + i] ?? 0,
      mm: h.precipitation[start + i] ?? 0,
      temp: h.temperature_2m[start + i],
      code: h.weather_code[start + i],
    }));
  });

  const markers = $derived.by(() => {
    if (!hours.length) return [];
    const from = hours[0].t;
    const span = hours.length * 3_600_000;
    return (weekend.current?.sessions ?? [])
      .map((s) => ({ kind: s.kind, at: (Date.parse(s.start) - from) / span }))
      .filter((m) => m.at >= 0 && m.at <= 1);
  });

  const peak = $derived(hours.reduce((m, h) => Math.max(m, h.rain), 0));

  function describe(code: number | undefined): string {
    if (code === undefined) return '';
    if (code === 0) return 'Clear';
    if (code <= 3) return 'Cloudy';
    if (code <= 48) return 'Fog';
    if (code <= 57) return 'Drizzle';
    if (code <= 67) return 'Rain';
    if (code <= 77) return 'Snow';
    if (code <= 82) return 'Showers';
    return 'Storms';
  }

  const hourLabel = (t: number) => new Date(t).toLocaleTimeString([], { hour: '2-digit' });
  const compass = ['N', 'NE', 'E', 'SE', 'S', 'SW', 'W', 'NW'];
</script>

<div class="wx">
  {#if live}
    <div class="tiles">
      <div class="tile"><span class="k">Air</span><span class="v">{live.air.toFixed(1)}°</span></div>
      <div class="tile"><span class="k">Track</span><span class="v">{live.track.toFixed(1)}°</span></div>
      <div class="tile"><span class="k">Humidity</span><span class="v">{live.humidity.toFixed(0)}%</span></div>
      <div class="tile">
        <span class="k">Wind</span>
        <span class="v">
          <svg viewBox="0 0 12 12" style:transform="rotate({live.windDir + 180}deg)" aria-hidden="true"><path d="M6 1 9.5 10 6 8 2.5 10z" /></svg>
          {live.wind.toFixed(1)}<small>m/s {compass[Math.round(live.windDir / 45) % 8]}</small>
        </span>
      </div>
      <div class="tile" class:wet={live.rain}>
        <span class="k">Track</span><span class="v">{live.rain ? 'Rain' : 'Dry'}</span>
      </div>
    </div>
  {:else}
    <p class="muted">Track weather appears with the session feed.</p>
  {/if}

  {#if hours.length}
    <figure>
      <figcaption>
        {#if forecast?.weekend}{forecast.weekend.locality} forecast ·{/if} chance of rain, next {hours.length} h
        <b class="num">peak {peak}%</b>
      </figcaption>
      <div class="plot">
        <div class="bars">
          {#each hours as h}
            <span class="col" title="{hourLabel(h.t)} · {h.rain}% rain · {h.mm} mm · {h.temp?.toFixed(0)}° · {describe(h.code)}">
              <i style:height="{Math.max(h.rain, 1)}%"></i>
            </span>
          {/each}
          {#if peak < 5}<span class="dry">No rain expected</span>{/if}
          {#each markers as m}
            <span class="marker" style:left="{m.at * 100}%"><em>{m.kind}</em></span>
          {/each}
        </div>
        <div class="labels num">
          {#each hours as h, i}
            <span>{#if i % 3 === 0}{hourLabel(h.t)}<small>{h.temp?.toFixed(0)}°</small>{/if}</span>
          {/each}
        </div>
      </div>
    </figure>
  {/if}
</div>

<style>
  .wx {
    position: absolute;
    inset: 0;
    display: flex;
    flex-direction: column;
    gap: .6rem;
    padding: .6rem .85rem;
  }

  .tiles {
    display: grid;
    grid-template-columns: repeat(5, 1fr);
    gap: .4rem;
  }

  .tile {
    display: flex;
    flex-direction: column;
    padding: .35rem .5rem;
    background: var(--panel-2);
    border-radius: var(--r-ctl);
    min-width: 0;
  }

  .tile.wet { box-shadow: inset 0 -2px 0 var(--wet); }

  .k { font-size: .6rem; color: var(--ink-3); text-transform: uppercase; letter-spacing: .1em; }
  .v { display: flex; align-items: center; gap: .2rem; font-size: 1.05rem; font-weight: 700; white-space: nowrap; }
  .v small { font-size: .62rem; color: var(--ink-2); font-weight: 600; margin-left: .15rem; }
  .v svg { width: .8rem; height: .8rem; fill: var(--ink-2); flex: none; }

  figure { margin: 0; flex: 1; min-height: 0; display: flex; flex-direction: column; }

  figcaption {
    display: flex;
    gap: .35rem;
    font-size: .68rem;
    color: var(--ink-2);
    text-transform: uppercase;
    letter-spacing: .08em;
  }

  figcaption b { margin-left: auto; color: var(--ink); font-weight: 500; }

  .plot { flex: 1; min-height: 0; display: flex; flex-direction: column; margin-top: .3rem; }

  .bars {
    position: relative;
    flex: 1;
    min-height: 2.5rem;
    display: flex;
    align-items: flex-end;
    gap: 2px;
    border-bottom: 1px solid var(--line-2);
  }

  .col {
    flex: 1;
    height: 100%;
    display: flex;
    align-items: flex-end;
  }

  .col i {
    display: block;
    width: 100%;
    max-width: 24px;
    margin: 0 auto;
    background: var(--cyan);
    border-radius: 3px 3px 0 0;
  }

  .marker {
    position: absolute;
    top: 0;
    bottom: 0;
    width: 1px;
    background: var(--ink-3);
  }

  .marker em {
    position: absolute;
    top: 0;
    left: 3px;
    font-style: normal;
    font-size: .6rem;
    font-weight: 700;
    color: var(--ink);
    background: var(--panel);
    padding: 0 2px;
  }

  .dry {
    position: absolute;
    inset: 0;
    display: grid;
    place-items: center;
    font-size: .8rem;
    color: var(--ink-3);
  }

  .labels {
    display: flex;
    gap: 2px;
    font-size: .6rem;
    color: var(--ink-3);
    margin-top: .2rem;
  }

  .labels span { flex: 1; white-space: nowrap; overflow: visible; }
  .labels small { display: block; color: var(--ink-2); }

  .muted { margin: 0; color: var(--ink-3); font-size: .85rem; }
</style>

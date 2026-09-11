<script lang="ts">
  import { feed } from '../lib/feed.svelte';
  import { mmss, remaining, sessionKind, sessionTitle, trackState, weather } from '../lib/model';
  import { countdown, weekend } from '../lib/weekend.svelte';

  interface Props {
    onMenu: () => void;
  }

  let { onMenu }: Props = $props();

  let tick = $state(0);
  $effect(() => {
    const t = setInterval(() => tick++, 250);
    return () => clearInterval(t);
  });

  const view = $derived.by(() => {
    feed.version;
    const s = feed.state;
    return {
      title: sessionTitle(s),
      kind: sessionKind(s),
      track: trackState(s),
      lap: s.LapCount ? { current: s.LapCount.CurrentLap ?? 0, total: s.LapCount.TotalLaps ?? 0 } : null,
      weather: weather(s),
    };
  });

  const clock = $derived.by(() => {
    tick;
    feed.version;
    return mmss(remaining(feed.state, feed.now()));
  });

  const next = $derived(weekend.nextSession);
  const status = $derived(feed.status);
  const live = $derived(status.source === 'Live' && status.connection === 'Connected');
</script>

<header class="top">
  <div class="brand">
    <img src="/favicon.svg" alt="" width="28" height="28" />
    <span>PitWall</span>
  </div>

  <div class="session">
    <span class="meeting">{view.title.meeting}</span>
    <span class="sub">
      {view.title.session || 'Waiting for session'}{#if view.title.circuit}<i>·</i>{view.title.circuit}{/if}
    </span>
  </div>

  <div class="flag {view.track.tone}" role="status" aria-live="polite">
    <span class="flag-mark" aria-hidden="true"></span>
    <span class="flag-label">{view.track.label}</span>
  </div>

  <div class="metric clock">
    <span class="k">Remaining</span>
    <span class="v num">{clock}</span>
  </div>

  {#if view.kind === 'Race' && view.lap && view.lap.total > 0}
    <div class="metric">
      <span class="k">Lap</span>
      <span class="v num">{view.lap.current}<small>/{view.lap.total}</small></span>
    </div>
  {/if}

  {#if view.weather}
    <div class="metric wx">
      <span class="k">Air · Track</span>
      <span class="v num">{view.weather.air.toFixed(0)}°<small> · </small>{view.weather.track.toFixed(0)}°</span>
    </div>
  {/if}

  {#if next}
    <div class="metric next">
      <span class="k">{next.live ? 'On now' : 'Next'} · {next.name}</span>
      <span class="v num">{next.live ? 'LIVE' : countdown(next.start - weekend.now)}</span>
    </div>
  {/if}

  <div class="state">
    {#if status.source === 'Replay'}
      <span class="chip replay">Replay{#if status.replay?.speed && status.replay.speed !== 1} ×{status.replay.speed}{/if}</span>
    {:else if live}
      <span class="chip live"><span class="ping" aria-hidden="true"></span>Live</span>
    {:else}
      <span class="chip warn">{status.connection === 'Error' ? 'Offline' : status.connection}</span>
    {/if}
    {#if feed.delay > 0}<span class="chip">+{feed.delay}s</span>{/if}
    <span class="chip" class:ok={status.auth.connected} title={status.auth.problem ?? ''}>
      F1 TV {status.auth.connected ? 'on' : 'off'}
    </span>
  </div>

  <button class="menu" onclick={onMenu} aria-label="Open controls (D)">
    <span></span><span></span><span></span>
  </button>
</header>

<style>
  .top {
    display: flex;
    align-items: stretch;
    gap: 1.25rem;
    height: 4rem;
    padding: 0 1rem;
    border-bottom: 1px solid var(--line);
    background: linear-gradient(180deg, #111318, var(--bg));
  }

  .brand {
    display: flex;
    align-items: center;
    gap: .55rem;
    font-weight: 700;
    font-size: 1.05rem;
    letter-spacing: .14em;
    text-transform: uppercase;
  }

  .brand img { width: 1.75rem; height: 1.75rem; }

  .session {
    display: flex;
    flex-direction: column;
    justify-content: center;
    min-width: 0;
    padding-left: 1.25rem;
    border-left: 1px solid var(--line);
  }

  .meeting {
    font-size: 1.2rem;
    font-weight: 700;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
    max-width: 22rem;
  }

  .sub {
    font-size: .85rem;
    color: var(--ink-2);
    text-transform: uppercase;
    letter-spacing: .08em;
  }

  .sub i { font-style: normal; margin: 0 .4rem; color: var(--ink-3); }

  /* Track status: the loudest element on screen, because it is the most important. */
  .flag {
    --tone: var(--ink-3);
    align-self: center;
    display: flex;
    align-items: center;
    gap: .7rem;
    height: 2.5rem;
    min-width: 15rem;
    padding: 0 1.2rem 0 .9rem;
    background: color-mix(in oklab, var(--tone) 16%, transparent);
    border: 1px solid color-mix(in oklab, var(--tone) 55%, transparent);
    transform: skewX(var(--skew));
    transition: background-color .4s ease, border-color .4s ease;
  }

  .flag > * { transform: skewX(12deg); }

  .flag-mark {
    width: .8rem;
    height: 1.4rem;
    background: var(--tone);
  }

  .flag-label {
    font-weight: 700;
    font-size: 1.05rem;
    letter-spacing: .1em;
    text-transform: uppercase;
    white-space: nowrap;
  }

  .flag.green { --tone: var(--flag-green); }
  .flag.yellow { --tone: var(--flag-yellow); }
  .flag.sc { --tone: var(--flag-sc); }
  .flag.red { --tone: var(--flag-red); }
  .flag.red, .flag.sc, .flag.yellow { animation: pulse 1.6s ease-in-out infinite; }

  @keyframes pulse {
    50% { background: color-mix(in oklab, var(--tone) 30%, transparent); }
  }

  .metric {
    display: flex;
    flex-direction: column;
    justify-content: center;
    white-space: nowrap;
  }

  .k {
    font-size: .68rem;
    color: var(--ink-3);
    text-transform: uppercase;
    letter-spacing: .12em;
  }

  .v {
    font-size: 1.45rem;
    font-weight: 700;
    line-height: 1.1;
  }

  .v small { font-size: .9rem; color: var(--ink-2); font-weight: 500; }

  .clock .v { font-size: 1.75rem; }

  .next { margin-left: auto; }
  .next .v { font-size: 1.15rem; color: var(--ink-2); }

  .state {
    display: flex;
    align-items: center;
    gap: .35rem;
  }

  .chip {
    display: inline-flex;
    align-items: center;
    gap: .4rem;
    height: 1.6rem;
    padding: 0 .6rem;
    border: 1px solid var(--line-2);
    border-radius: var(--r-ctl);
    font-size: .72rem;
    font-weight: 700;
    letter-spacing: .1em;
    text-transform: uppercase;
    color: var(--ink-2);
    white-space: nowrap;
  }

  .chip.live { color: #fff; background: var(--red); border-color: var(--red); }
  .chip.replay { color: var(--cyan); border-color: color-mix(in oklab, var(--cyan) 50%, transparent); }
  .chip.warn { color: var(--flag-sc); border-color: color-mix(in oklab, var(--flag-sc) 50%, transparent); }
  .chip.ok { color: var(--green); border-color: color-mix(in oklab, var(--green) 45%, transparent); }

  .ping {
    position: relative;
    width: .45rem;
    height: .45rem;
    border-radius: 50%;
    background: #fff;
  }

  .ping::before {
    content: "";
    position: absolute;
    inset: 0;
    border-radius: 50%;
    background: #fff;
    animation: ping 1.9s cubic-bezier(0, 0, .2, 1) infinite;
  }

  @keyframes ping { to { transform: scale(2.6); opacity: 0; } }

  .menu {
    align-self: center;
    display: grid;
    gap: .22rem;
    width: 2.4rem;
    height: 2.4rem;
    place-content: center;
    background: var(--panel-2);
    border: 1px solid var(--line-2);
    border-radius: var(--r-ctl);
    transition: transform .16s var(--ease-out), border-color .15s ease;
  }

  .menu span { display: block; width: 1rem; height: 2px; background: var(--ink); }
  .menu:hover { border-color: var(--cyan); }
  .menu:active { transform: scale(.97); }
</style>

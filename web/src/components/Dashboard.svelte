<script lang="ts">
  import { onMount } from 'svelte';
  import { feed } from '../lib/feed.svelte';
  import { command } from '../lib/api';
  import { inHost, sendHost } from '../lib/host';
  import { sessionKind } from '../lib/model';
  import { weekend } from '../lib/weekend.svelte';
  import Panel from './Panel.svelte';
  import TopBar from './TopBar.svelte';
  import TimingTower from './TimingTower.svelte';
  import TrackMap from './TrackMap.svelte';
  import DriverFocus from './DriverFocus.svelte';
  import TyreStrategy from './TyreStrategy.svelte';
  import RaceControl from './RaceControl.svelte';
  import TeamRadio from './TeamRadio.svelte';
  import WeatherPanel from './WeatherPanel.svelte';
  import Championship from './Championship.svelte';
  import NewsTicker from './NewsTicker.svelte';
  import ControlDrawer from './ControlDrawer.svelte';
  import F1TvConnect from './F1TvConnect.svelte';

  let selected = $state<string | null>(null);
  let drawer = $state(false);
  let tab = $state<'weather' | 'champ'>('weather');
  let pinned = $state(false);

  const select = (num: string | null) => (selected = num);

  onMount(() => {
    feed.connect();
    weekend.start();

    // Alternate weather and championship every 20 s until the viewer picks one.
    const rotate = setInterval(() => {
      if (!pinned) tab = tab === 'weather' ? 'champ' : 'weather';
    }, 20_000);
    return () => clearInterval(rotate);
  });

  const kindLabel = $derived.by(() => {
    feed.version;
    const kind = sessionKind(feed.state);
    return kind === 'Race' ? 'Race order' : kind === 'Qualifying' ? 'Qualifying order' : kind === 'Practice' ? 'Fastest laps' : '';
  });

  function onKey(e: KeyboardEvent) {
    const target = e.target as HTMLElement;
    if (target.closest('input, select, textarea')) return;
    const replay = feed.status.source === 'Replay' ? feed.status.replay : undefined;

    if (e.key === 'F11') {
      e.preventDefault();
      if (inHost()) sendHost('fullscreen');
      else if (document.fullscreenElement) document.exitFullscreen();
      else document.documentElement.requestFullscreen();
    } else if (e.key === 'd' || e.key === 'D') {
      drawer = !drawer;
    } else if (e.key === 'w' || e.key === 'W') {
      location.href = '/weekend';
    } else if (e.key === 'Escape') {
      selected = null;
    } else if (replay && e.key === ' ') {
      e.preventDefault();
      command('/api/replay', { action: replay.playing ? 'pause' : 'play' });
    } else if (replay && (e.key === 'ArrowLeft' || e.key === 'ArrowRight')) {
      command('/api/replay', { action: 'seek', value: Math.max(0, replay.offset + (e.key === 'ArrowLeft' ? -10 : 10)) });
    }
  }
</script>

<svelte:window onkeydown={onKey} />

<div class="dash">
  <TopBar onMenu={() => (drawer = true)} />

  <main>
    <Panel n="01" title="Timing" area="timing" note={kindLabel}>
      <TimingTower {selected} onSelect={select} />
    </Panel>

    <Panel n="02" title="Track" area="track">
      <TrackMap {selected} onSelect={select} />
      {#if selected}
        <DriverFocus num={selected} onClose={() => (selected = null)} />
      {/if}
    </Panel>

    <Panel n="03" title="Tyres & strategy" area="tyres">
      <TyreStrategy {selected} onSelect={select} />
    </Panel>

    <Panel n="04" title="Race control" area="rc">
      <RaceControl />
    </Panel>

    <Panel n="05" title="Team radio" area="radio">
      <TeamRadio />
    </Panel>

    <Panel n="06" title={tab === 'weather' ? 'Weather' : 'Championship'} area="side">
      {#snippet actions()}
        <div class="tabs" role="tablist">
          <button role="tab" aria-selected={tab === 'weather'} class:on={tab === 'weather'} onclick={() => { tab = 'weather'; pinned = true; }}>Weather</button>
          <button role="tab" aria-selected={tab === 'champ'} class:on={tab === 'champ'} onclick={() => { tab = 'champ'; pinned = true; }}>Standings</button>
        </div>
      {/snippet}
      {#if tab === 'weather'}<WeatherPanel />{:else}<Championship />{/if}
    </Panel>
  </main>

  <NewsTicker />
</div>

<ControlDrawer open={drawer} onClose={() => (drawer = false)} />
<F1TvConnect />

<style>
  .dash {
    display: flex;
    flex-direction: column;
    height: 100vh;
    overflow: hidden;
  }

  main {
    flex: 1;
    min-height: 0;
    display: grid;
    gap: .5rem;
    padding: .5rem;
    grid-template-columns: 47fr 29fr 24fr;
    grid-template-rows: 36fr 20fr 44fr;
    grid-template-areas:
      'timing track rc'
      'timing track radio'
      'timing tyres side';
  }

  .tabs { display: flex; gap: 2px; }

  .tabs button {
    padding: .15rem .5rem;
    border: 1px solid var(--line-2);
    border-radius: var(--r-ctl);
    background: transparent;
    font-size: .66rem;
    font-weight: 700;
    letter-spacing: .08em;
    text-transform: uppercase;
    color: var(--ink-3);
    transition: transform .16s var(--ease-out), color .15s ease;
  }

  .tabs button.on { color: var(--ink); border-color: var(--cyan); }
  .tabs button:active { transform: scale(.97); }

  @media (max-aspect-ratio: 4/3) {
    .dash { height: auto; min-height: 100vh; overflow: visible; }
    main {
      grid-template-columns: 1fr;
      grid-template-rows: 60rem 34rem 24rem 22rem 14rem 18rem;
      grid-template-areas: 'timing' 'track' 'tyres' 'rc' 'radio' 'side';
    }
  }
</style>

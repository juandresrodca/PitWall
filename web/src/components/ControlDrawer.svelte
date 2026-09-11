<script lang="ts">
  import { feed } from '../lib/feed.svelte';
  import { command, getJson } from '../lib/api';
  import { inHost, sendHost } from '../lib/host';
  import { mmss } from '../lib/model';
  import { authUi } from '../lib/auth.svelte';
  import type { ArchiveMeeting } from '../lib/types';

  interface Props {
    open: boolean;
    onClose: () => void;
  }

  let { open, onClose }: Props = $props();

  const host = inHost();
  const status = $derived(feed.status);
  const replay = $derived(status.replay);

  const thisYear = new Date().getFullYear();
  let year = $state(thisYear);
  let meetings = $state<ArchiveMeeting[]>([]);
  let meetingKey = $state<number | null>(null);
  let sessionPath = $state('');
  let loadingList = $state(false);

  $effect(() => {
    if (!open) return;
    const y = year;
    loadingList = true;
    getJson<ArchiveMeeting[]>(`/api/archive/${y}`).then((list) => {
      if (y !== year) return;
      meetings = list ?? [];
      meetingKey = meetings[0]?.key ?? null;
      loadingList = false;
    });
  });

  const meeting = $derived(meetings.find((m) => m.key === meetingKey) ?? null);

  $effect(() => {
    const sessions = meeting?.sessions ?? [];
    sessionPath = (sessions.find((s) => s.type === 'Race') ?? sessions[sessions.length - 1])?.path ?? '';
  });

  // While dragging the seek bar, show the dragged position rather than the live one.
  let scrub = $state<number | null>(null);

  const speeds = [1, 2, 5, 10, 30];

  const startReplay = () => sessionPath && command('/api/source/replay', { path: sessionPath });
  const goLive = () => command('/api/source/live');
  const control = (action: string, value?: number) => command('/api/replay', { action, value });

  function onKey(e: KeyboardEvent) {
    if (open && e.key === 'Escape') onClose();
  }

  const authText = $derived.by(() => {
    const a = status.auth;
    if (a.connected || a.signedIn) {
      const until = a.expires ? new Date(a.expires).toLocaleString([], { weekday: 'short', day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' }) : '';
      return `${a.product ?? 'F1 TV'} connected${until ? ` · until ${until}` : ''}`;
    }
    return a.problem ?? 'Not connected. Timing works without it; car positions and telemetry need it.';
  });
</script>

<svelte:window onkeydown={onKey} />

<div class="scrim" class:open onclick={onClose} aria-hidden="true"></div>

<aside class="drawer" class:open aria-label="Controls" aria-hidden={!open} inert={!open}>
  <header>
    <h2>Controls</h2>
    <button class="ghost" onclick={onClose} aria-label="Close controls">Close</button>
  </header>

  <section>
    <h3><b>A</b> Data source</h3>
    <div class="seg">
      <button class:on={status.source === 'Live'} onclick={goLive}>Live timing</button>
      <button class:on={status.source === 'Replay'} disabled>Replay</button>
    </div>
    <p class="hint">{status.message ?? ''}</p>

    <div class="pick">
      <label>
        <span>Season</span>
        <select bind:value={year}>
          {#each [thisYear, thisYear - 1, thisYear - 2] as y}<option value={y}>{y}</option>{/each}
        </select>
      </label>
      <label>
        <span>Grand Prix</span>
        <select bind:value={meetingKey} disabled={loadingList || !meetings.length}>
          {#each meetings as m (m.key)}<option value={m.key}>{m.name}</option>{/each}
        </select>
      </label>
      <label>
        <span>Session</span>
        <select bind:value={sessionPath} disabled={!meeting}>
          {#each meeting?.sessions ?? [] as s (s.path)}<option value={s.path}>{s.name}</option>{/each}
        </select>
      </label>
    </div>
    <button class="primary" onclick={startReplay} disabled={!sessionPath}>Replay this session</button>

    {#if status.source === 'Replay' && replay}
      <div class="transport">
        <p class="title">{replay.title}</p>
        {#if status.connection === 'Loading'}
          <div class="load"><i style:transform="scaleX({replay.loadProgress})"></i></div>
        {:else}
          <input
            type="range"
            min="0"
            max={replay.duration}
            step="1"
            value={scrub ?? replay.offset}
            oninput={(e) => (scrub = Number((e.target as HTMLInputElement).value))}
            onchange={(e) => {
              control('seek', Number((e.target as HTMLInputElement).value));
              scrub = null;
            }}
            aria-label="Replay position"
          />
          <div class="times num">
            <span>{mmss((scrub ?? replay.offset) * 1000)}</span>
            <span>{mmss(replay.duration * 1000)}</span>
          </div>
          <div class="row">
            <button onclick={() => control(replay.playing ? 'pause' : 'play')}>{replay.playing ? 'Pause' : 'Play'}</button>
            <button onclick={() => control('seek', Math.max(0, replay.offset - 30))}>−30 s</button>
            <button onclick={() => control('seek', replay.offset + 30)}>+30 s</button>
            {#if replay.greenFlag}
              <button onclick={() => control('seek', Math.max(0, (replay.greenFlag ?? 0) - 60))}>To start</button>
            {/if}
          </div>
          <div class="seg speeds">
            {#each speeds as s}
              <button class:on={replay.speed === s} onclick={() => control('speed', s)}>{s}×</button>
            {/each}
          </div>
        {/if}
      </div>
    {/if}
  </section>

  <section>
    <h3><b>B</b> Broadcast delay</h3>
    <p class="hint">Hold the timing back so it matches the video. F1 TV usually runs 20–60 s behind the feed.</p>
    <div class="delay">
      <button onclick={() => feed.setDelay(feed.delay - 1)} aria-label="One second less">−</button>
      <input type="range" min="0" max="90" value={feed.delay} oninput={(e) => feed.setDelay(Number((e.target as HTMLInputElement).value))} aria-label="Delay in seconds" />
      <button onclick={() => feed.setDelay(feed.delay + 1)} aria-label="One second more">+</button>
      <span class="num">{feed.delay}s</span>
    </div>
  </section>

  <section>
    <h3><b>C</b> F1 TV account</h3>
    <p class="hint" class:ok={status.auth.connected}>{authText}</p>
    <button class="primary" onclick={() => { onClose(); authUi.show('manual'); }}>
      {status.auth.connected || status.auth.signedIn ? 'Manage F1 TV sign-in' : 'Connect F1 TV'}
    </button>
  </section>

  {#if host}
    <section>
      <h3><b>D</b> Screens</h3>
      <div class="row wrap">
        <button onclick={() => sendHost('open-video')}>Open live video</button>
        <button onclick={() => sendHost('swap-screens')}>Swap screens</button>
        <button onclick={() => sendHost('screen-setup')}>Screen setup…</button>
        <button onclick={() => sendHost('fullscreen')}>Full screen (F11)</button>
      </div>
    </section>
  {/if}

  <footer>
    <a href="/weekend">Weekend hub →</a>
    <span class="hint">Keys: D controls · W weekend · Space play/pause · ← → seek</span>
  </footer>
</aside>

<style>
  .scrim {
    position: fixed;
    inset: 0;
    z-index: 40;
    background: rgba(5, 6, 8, .55);
    opacity: 0;
    pointer-events: none;
    transition: opacity .25s ease;
  }

  .scrim.open { opacity: 1; pointer-events: auto; }

  .drawer {
    position: fixed;
    top: 0;
    right: 0;
    bottom: 0;
    z-index: 50;
    width: min(30rem, 92vw);
    display: flex;
    flex-direction: column;
    gap: 1.1rem;
    padding: 1.1rem 1.3rem;
    background: var(--panel);
    border-left: 1px solid var(--line-2);
    overflow-y: auto;
    transform: translateX(102%);
    transition: transform .35s cubic-bezier(.32, .72, 0, 1);
  }

  .drawer.open { transform: none; }

  header { display: flex; align-items: center; justify-content: space-between; }
  h2 { margin: 0; font-size: 1.2rem; letter-spacing: .12em; text-transform: uppercase; }

  h3 {
    margin: 0 0 .5rem;
    font-size: .78rem;
    letter-spacing: .14em;
    text-transform: uppercase;
    color: var(--ink);
  }

  h3 b { font-family: var(--font-num); color: var(--cyan); font-weight: 500; margin-right: .35rem; }

  section { padding-bottom: 1rem; border-bottom: 1px solid var(--line); }

  .hint { margin: .35rem 0; font-size: .8rem; color: var(--ink-3); }
  .hint.ok { color: var(--green); }

  button {
    padding: .45rem .8rem;
    border: 1px solid var(--line-2);
    border-radius: var(--r-ctl);
    background: var(--panel-2);
    font-size: .85rem;
    font-weight: 600;
    transition: transform .16s var(--ease-out), border-color .15s ease, background-color .15s ease;
  }

  button:hover:not(:disabled) { border-color: var(--cyan); }
  button:disabled { opacity: .45; cursor: default; }

  .ghost { background: transparent; }

  .primary {
    margin-top: .5rem;
    background: var(--red);
    border-color: var(--red);
    color: #fff;
  }

  .primary:hover:not(:disabled) { border-color: var(--red); filter: brightness(1.1); }

  .seg { display: inline-flex; border: 1px solid var(--line-2); border-radius: var(--r-ctl); overflow: hidden; }
  .seg button { border: 0; border-radius: 0; background: transparent; }
  .seg button + button { border-left: 1px solid var(--line-2); }
  .seg button.on { background: var(--cyan); color: #04141a; }
  .seg button.on:disabled { opacity: 1; }

  .pick { display: grid; grid-template-columns: 5rem 1fr; gap: .5rem; margin-top: .6rem; }
  .pick label:nth-child(3) { grid-column: span 2; }
  label { display: flex; flex-direction: column; gap: .25rem; font-size: .7rem; color: var(--ink-3); text-transform: uppercase; letter-spacing: .08em; }

  select {
    padding: .4rem .5rem;
    border: 1px solid var(--line-2);
    border-radius: var(--r-ctl);
    background: var(--panel-2);
    color: var(--ink);
    font: inherit;
    font-size: .88rem;
    text-transform: none;
    letter-spacing: 0;
  }

  .transport {
    margin-top: .9rem;
    padding: .7rem;
    background: var(--panel-2);
    border-radius: var(--r-ctl);
  }

  .title { margin: 0 0 .4rem; font-weight: 700; }

  input[type='range'] { width: 100%; accent-color: var(--cyan); }

  .times { display: flex; justify-content: space-between; font-size: .75rem; color: var(--ink-3); }

  .row { display: flex; gap: .4rem; margin-top: .5rem; }
  .row.wrap { flex-wrap: wrap; }
  .speeds { margin-top: .5rem; }

  .load { height: 4px; background: var(--line-2); border-radius: 2px; overflow: hidden; }
  .load i { display: block; height: 100%; background: var(--cyan); transform-origin: 0 50%; transition: transform .3s ease; }

  .delay { display: grid; grid-template-columns: 2rem 1fr 2rem 3rem; gap: .5rem; align-items: center; }
  .delay button { padding: .3rem 0; }
  .delay span { text-align: right; font-weight: 700; }

  footer { margin-top: auto; display: flex; flex-direction: column; gap: .3rem; }
  footer a { color: var(--cyan); text-decoration: none; font-weight: 600; }

  button:active:not(:disabled) { transform: scale(.97); }
</style>

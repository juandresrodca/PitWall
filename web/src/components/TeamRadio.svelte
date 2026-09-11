<script lang="ts">
  import { feed } from '../lib/feed.svelte';
  import { drivers, localTime, teamRadio } from '../lib/model';
  import DriverTag from './DriverTag.svelte';

  const clips = $derived.by(() => {
    feed.version;
    const list = drivers(feed.state);
    return teamRadio(feed.state).slice(0, 30).map((c) => ({ ...c, driver: list[c.num] }));
  });

  let playing = $state<string | null>(null);
  let progress = $state(0);
  let audio: HTMLAudioElement | null = null;

  function toggle(url: string) {
    if (playing === url) {
      audio?.pause();
      playing = null;
      return;
    }
    audio?.pause();
    audio = new Audio(url);
    audio.ontimeupdate = () => (progress = audio && audio.duration ? audio.currentTime / audio.duration : 0);
    audio.onended = () => {
      playing = null;
      progress = 0;
    };
    audio.onerror = () => (playing = null);
    progress = 0;
    playing = url;
    audio.play().catch(() => (playing = null));
  }

  $effect(() => () => audio?.pause());
</script>

<ul class="radio">
  {#each clips as clip (clip.url)}
    <li class:on={playing === clip.url}>
      <button onclick={() => toggle(clip.url)} aria-label="{playing === clip.url ? 'Pause' : 'Play'} team radio">
        {#if playing === clip.url}
          <svg viewBox="0 0 12 12" aria-hidden="true"><rect x="2" y="2" width="3" height="8" /><rect x="7" y="2" width="3" height="8" /></svg>
        {:else}
          <svg viewBox="0 0 12 12" aria-hidden="true"><path d="M3 1.8v8.4L10.2 6z" /></svg>
        {/if}
      </button>
      {#if clip.driver}<DriverTag driver={clip.driver} compact />{:else}<span>#{clip.num}</span>{/if}
      <span class="bar" aria-hidden="true">
        <i style:transform="scaleX({playing === clip.url ? progress : 0})"></i>
      </span>
      <span class="when num">{localTime(clip.utc)}</span>
    </li>
  {:else}
    <li class="empty">No team radio published yet.</li>
  {/each}
</ul>

<style>
  .radio {
    position: absolute;
    inset: 0;
    margin: 0;
    padding: .2rem 0;
    list-style: none;
    overflow-y: auto;
  }

  li {
    display: grid;
    grid-template-columns: 1.7rem 5rem 1fr auto;
    align-items: center;
    gap: .6rem;
    padding: .3rem .85rem;
    border-bottom: 1px solid var(--line);
  }

  li.on { background: color-mix(in oklab, var(--cyan) 10%, transparent); }

  button {
    display: grid;
    place-items: center;
    width: 1.7rem;
    height: 1.7rem;
    padding: 0;
    border: 1px solid var(--line-2);
    border-radius: 50%;
    background: var(--panel-2);
    transition: transform .16s var(--ease-out), border-color .15s ease;
  }

  button:hover { border-color: var(--cyan); }
  button:active { transform: scale(.94); }
  svg { width: .7rem; height: .7rem; fill: var(--ink); }

  .bar {
    height: 3px;
    background: var(--line-2);
    border-radius: 2px;
    overflow: hidden;
  }

  .bar i {
    display: block;
    height: 100%;
    background: var(--cyan);
    transform-origin: 0 50%;
    transition: transform .25s linear;
  }

  .when { font-size: .72rem; color: var(--ink-3); }

  .empty {
    display: block;
    color: var(--ink-3);
    border: 0;
    font-size: .82rem;
  }
</style>

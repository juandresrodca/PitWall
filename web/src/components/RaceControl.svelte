<script lang="ts">
  import { feed } from '../lib/feed.svelte';
  import { localTime, raceControl, type RaceMessage } from '../lib/model';

  const messages = $derived.by(() => {
    feed.version;
    return raceControl(feed.state).slice(0, 60);
  });

  function tone(m: RaceMessage): string {
    const flag = (m.flag ?? '').toUpperCase();
    if (flag.includes('RED')) return 'red';
    if (flag.includes('YELLOW')) return 'yellow';
    if (flag === 'GREEN' || flag === 'CLEAR') return 'green';
    if (flag === 'BLUE') return 'blue';
    if (flag === 'CHEQUERED') return 'chequered';
    if (flag.includes('BLACK')) return 'black';
    if (m.category === 'SafetyCar') return 'sc';
    const text = m.message.toUpperCase();
    if (/PENALTY|DISQUALIFIED|INVESTIGATION|DELETED|NOTED|REPRIMAND/.test(text)) return 'steward';
    return 'info';
  }

  function label(m: RaceMessage): string {
    if (m.flag) return m.flag.replace('DOUBLE ', '2× ');
    if (m.category === 'SafetyCar') return 'SC';
    if (m.category === 'Drs') return 'DRS';
    const text = m.message.toUpperCase();
    if (text.includes('PENALTY')) return 'Penalty';
    if (text.includes('INVESTIGATION')) return 'Investigation';
    if (text.includes('DELETED')) return 'Deleted';
    if (text.includes('NOTED')) return 'Noted';
    return m.category === 'Other' ? 'Info' : m.category;
  }
</script>

<ol class="feed" aria-live="polite">
  {#each messages as m (m.id)}
    <li class={tone(m)}>
      <span class="when num">
        {localTime(m.utc)}
        {#if m.lap}<small>L{m.lap}</small>{/if}
      </span>
      <span class="chip">{label(m)}</span>
      <span class="msg">{m.message}</span>
    </li>
  {:else}
    <li class="empty">No race control messages yet.</li>
  {/each}
</ol>

<style>
  .feed {
    position: absolute;
    inset: 0;
    margin: 0;
    padding: .2rem 0;
    list-style: none;
    overflow-y: auto;
  }

  li {
    --tone: var(--ink-3);
    display: grid;
    grid-template-columns: 3.3rem 6.9rem 1fr;
    gap: .55rem;
    align-items: start;
    padding: .45rem .85rem;
    border-bottom: 1px solid var(--line);
    font-size: .82rem;
    line-height: 1.3;
    animation: slide .35s var(--ease-out);
  }

  @keyframes slide { from { opacity: 0; transform: translateY(-6px); } }

  .when { color: var(--ink-3); font-size: .74rem; }
  .when small { display: block; font-size: .66rem; }

  .chip {
    justify-self: start;
    padding: .1rem .4rem;
    border-left: 3px solid var(--tone);
    background: color-mix(in oklab, var(--tone) 14%, transparent);
    font-size: .64rem;
    font-weight: 700;
    letter-spacing: .08em;
    text-transform: uppercase;
    white-space: nowrap;
    max-width: 100%;
    overflow: hidden;
    text-overflow: ellipsis;
  }

  .msg { color: var(--ink); }

  .red { --tone: var(--flag-red); }
  .yellow { --tone: var(--flag-yellow); }
  .green { --tone: var(--flag-green); }
  .blue { --tone: var(--flag-blue); }
  .sc { --tone: var(--flag-sc); }
  .steward { --tone: var(--purple); }
  .chequered { --tone: var(--ink); }
  .black { --tone: var(--ink-2); }
  .info .msg { color: var(--ink-2); }

  .empty {
    display: block;
    color: var(--ink-3);
    border: 0;
  }
</style>

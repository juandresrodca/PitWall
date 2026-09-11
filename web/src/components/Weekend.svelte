<script lang="ts">
  import { onMount } from 'svelte';
  import { openExternal } from '../lib/host';
  import { countdown, weekend } from '../lib/weekend.svelte';

  onMount(() => weekend.start());

  const current = $derived(weekend.current);
  const next = $derived(weekend.nextSession);
  const rounds = $derived(weekend.season?.rounds ?? []);
  const news = $derived(weekend.news);
  const lead = $derived(news.find((n) => n.image) ?? news[0]);
  const rest = $derived(news.filter((n) => n !== lead).slice(0, 23));

  function dayLabel(iso: string): string {
    return new Date(iso).toLocaleDateString([], { weekday: 'short', day: 'numeric', month: 'short' });
  }

  function timeLabel(iso: string): string {
    return new Date(iso).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
  }

  function state(start: string, end: string): 'done' | 'live' | 'next' | 'later' {
    const now = weekend.now;
    if (Date.parse(end) <= now) return 'done';
    if (Date.parse(start) <= now) return 'live';
    return next && Date.parse(start) === next.start ? 'next' : 'later';
  }

  function ago(iso?: string): string {
    if (!iso) return '';
    const minutes = Math.round((weekend.now - Date.parse(iso)) / 60000);
    if (minutes < 60) return `${Math.max(1, minutes)} min ago`;
    if (minutes < 1440) return `${Math.round(minutes / 60)} h ago`;
    return new Date(iso).toLocaleDateString([], { day: 'numeric', month: 'short' });
  }

  function onKey(e: KeyboardEvent) {
    if (e.key === 'Escape' || e.key === 'd' || e.key === 'D') location.href = '/';
  }
</script>

<svelte:window onkeydown={onKey} />

<div class="page">
  <nav>
    <a class="brand" href="/"><img src="/favicon.svg" alt="" width="26" height="26" />PitWall</a>
    <a class="back" href="/">← Command centre</a>
  </nav>

  <header class="hero">
    {#if current}
      <p class="round num">Round {current.round} · {current.season}</p>
      <h1>{current.name}</h1>
      <p class="where">{current.circuit} · {current.locality}, {current.country}</p>
    {:else}
      <h1>Race weekend</h1>
      <p class="where">Loading the calendar…</p>
    {/if}

    {#if next}
      <div class="count">
        <span class="k">{next.live ? 'On track now' : `Next · ${next.name}`}</span>
        <span class="v num">{next.live ? next.name : countdown(next.start - weekend.now)}</span>
      </div>
    {/if}
  </header>

  <div class="layout">
    <section class="schedule" aria-labelledby="sched">
      <h2 id="sched"><b>01</b> Weekend schedule</h2>
      {#if current}
        <ol>
          {#each current.sessions as s}
            {@const st = state(s.start, s.end)}
            <li class={st}>
              <span class="kind">{s.kind}</span>
              <span class="name">{s.name}</span>
              <span class="when num">{dayLabel(s.start)} · {timeLabel(s.start)}</span>
              <span class="badge">{st === 'done' ? 'Done' : st === 'live' ? 'Live' : st === 'next' ? countdown(Date.parse(s.start) - weekend.now) : ''}</span>
            </li>
          {/each}
        </ol>
        <p class="note">Times in your time zone.</p>
      {/if}
    </section>

    <section class="standings" aria-labelledby="stand">
      <h2 id="stand"><b>02</b> Championship</h2>
      {#if weekend.standings}
        <div class="tables">
          <table>
            <caption>Drivers · after round {weekend.standings.round}</caption>
            <thead><tr><th>Pos</th><th>Driver</th><th class="r">Wins</th><th class="r">Pts</th></tr></thead>
            <tbody>
              {#each weekend.standings.drivers.slice(0, 12) as d (d.code)}
                <tr>
                  <td class="num">{d.position}</td>
                  <td><strong>{d.firstName} {d.lastName}</strong><small>{d.team}</small></td>
                  <td class="num r">{d.wins}</td>
                  <td class="num r strong">{d.points}</td>
                </tr>
              {/each}
            </tbody>
          </table>
          <table>
            <caption>Constructors</caption>
            <thead><tr><th>Pos</th><th>Team</th><th class="r">Wins</th><th class="r">Pts</th></tr></thead>
            <tbody>
              {#each weekend.standings.teams as t (t.name)}
                <tr>
                  <td class="num">{t.position}</td>
                  <td><strong>{t.name}</strong></td>
                  <td class="num r">{t.wins}</td>
                  <td class="num r strong">{t.points}</td>
                </tr>
              {/each}
            </tbody>
          </table>
        </div>
      {:else}
        <p class="note">Loading standings…</p>
      {/if}
    </section>

    <section class="news" aria-labelledby="news">
      <h2 id="news"><b>03</b> Latest news</h2>
      {#if lead}
        <button class="lead" onclick={() => openExternal(lead.url)}>
          {#if lead.image}<img src={lead.image} alt="" loading="lazy" referrerpolicy="no-referrer" />{/if}
          <span class="meta">{lead.source}{#if lead.published} · {ago(lead.published)}{/if}</span>
          <strong>{lead.title}</strong>
          {#if lead.summary}<span class="sum">{lead.summary}</span>{/if}
        </button>
      {/if}
      <ul>
        {#each rest as item (item.url)}
          <li>
            <button onclick={() => openExternal(item.url)}>
              <span class="meta">{item.source}{#if item.published} · {ago(item.published)}{/if}</span>
              <span class="title">{item.title}</span>
            </button>
          </li>
        {/each}
      </ul>
    </section>

    <section class="calendar" aria-labelledby="cal">
      <h2 id="cal"><b>04</b> {weekend.season?.year ?? ''} calendar</h2>
      <ol>
        {#each rounds as r (r.round)}
          {@const done = Date.parse(r.sessions[r.sessions.length - 1].end) < weekend.now}
          <li class:done class:current={current?.round === r.round}>
            <span class="num rn">{String(r.round).padStart(2, '0')}</span>
            <span class="gp">{r.name.replace(' Grand Prix', ' GP')}</span>
            <span class="num date">{dayLabel(r.sessions[r.sessions.length - 1].start)}</span>
          </li>
        {/each}
      </ol>
    </section>
  </div>
</div>

<style>
  .page {
    max-width: 120rem;
    margin: 0 auto;
    padding: 1rem 1.5rem 2rem;
  }

  nav {
    display: flex;
    align-items: center;
    justify-content: space-between;
    height: 3rem;
  }

  .brand {
    display: flex;
    align-items: center;
    gap: .5rem;
    font-weight: 700;
    letter-spacing: .14em;
    text-transform: uppercase;
    text-decoration: none;
  }

  .back { color: var(--cyan); text-decoration: none; font-weight: 600; }

  .hero {
    display: grid;
    grid-template-columns: 1fr auto;
    align-items: end;
    gap: 0 2rem;
    padding: 2rem 0 1.6rem;
    border-bottom: 1px solid var(--line);
  }

  .round {
    grid-column: 1;
    margin: 0;
    color: var(--red-ink);
    font-size: .9rem;
    letter-spacing: .08em;
    text-transform: uppercase;
  }

  h1 {
    grid-column: 1;
    margin: .2rem 0;
    font-size: 3.6rem;
    line-height: 1;
    font-weight: 700;
    letter-spacing: -.01em;
  }

  .where { grid-column: 1; margin: .3rem 0 0; color: var(--ink-2); font-size: 1.1rem; }

  .count {
    grid-column: 2;
    grid-row: 1 / span 3;
    display: flex;
    flex-direction: column;
    align-items: flex-end;
    padding: .8rem 1.2rem;
    border: 1px solid var(--line-2);
    border-right: 4px solid var(--red);
    border-radius: var(--r-panel);
    background: var(--panel);
  }

  .count .k { font-size: .75rem; color: var(--ink-3); text-transform: uppercase; letter-spacing: .12em; }
  .count .v { font-size: 2.4rem; font-weight: 700; line-height: 1.1; }

  .layout {
    display: grid;
    grid-template-columns: 1.1fr 1.4fr 1fr;
    grid-template-areas:
      'schedule news calendar'
      'standings news calendar';
    gap: 1.4rem;
    margin-top: 1.4rem;
    align-items: start;
  }

  section { min-width: 0; }
  .schedule { grid-area: schedule; }
  .standings { grid-area: standings; }
  .news { grid-area: news; }
  .calendar { grid-area: calendar; }

  h2 {
    margin: 0 0 .8rem;
    font-size: .85rem;
    letter-spacing: .14em;
    text-transform: uppercase;
  }

  h2 b { font-family: var(--font-num); color: var(--cyan); font-weight: 500; margin-right: .4rem; }

  .schedule ol, .calendar ol, .news ul { margin: 0; padding: 0; list-style: none; }

  .schedule li {
    display: grid;
    grid-template-columns: 2.8rem 1fr auto;
    grid-template-areas: 'kind name badge' 'kind when badge';
    column-gap: .8rem;
    padding: .6rem .8rem;
    margin-bottom: .35rem;
    background: var(--panel);
    border: 1px solid var(--line);
    border-radius: var(--r-panel);
  }

  .kind {
    grid-area: kind;
    align-self: center;
    font-weight: 700;
    font-size: .9rem;
    color: var(--ink-2);
  }

  .name { grid-area: name; font-weight: 700; font-size: 1.05rem; }
  .when { grid-area: when; font-size: .8rem; color: var(--ink-3); }

  .badge {
    grid-area: badge;
    align-self: center;
    font-size: .75rem;
    font-weight: 700;
    letter-spacing: .08em;
    text-transform: uppercase;
    color: var(--ink-3);
  }

  .schedule li.done { opacity: .55; }
  .schedule li.live { border-color: var(--red); }
  .schedule li.live .badge { color: #fff; background: var(--red); padding: .15rem .5rem; border-radius: var(--r-ctl); }
  .schedule li.next { border-color: color-mix(in oklab, var(--cyan) 55%, transparent); }
  .schedule li.next .badge { color: var(--cyan); font-family: var(--font-num); font-size: .85rem; letter-spacing: 0; }

  .note { font-size: .78rem; color: var(--ink-3); }

  .tables { display: grid; gap: 1.2rem; }

  table { width: 100%; border-collapse: collapse; font-size: .9rem; }
  caption { text-align: left; font-size: .72rem; color: var(--ink-3); text-transform: uppercase; letter-spacing: .1em; padding-bottom: .4rem; }
  th { text-align: left; font-size: .68rem; color: var(--ink-3); font-weight: 700; letter-spacing: .1em; text-transform: uppercase; padding: .3rem .4rem; border-bottom: 1px solid var(--line-2); }
  td { padding: .4rem; border-bottom: 1px solid var(--line); vertical-align: middle; }
  td small { display: block; font-size: .72rem; color: var(--ink-3); }
  .r { text-align: right; }
  .strong { font-weight: 700; }

  .lead {
    display: flex;
    flex-direction: column;
    gap: .4rem;
    width: 100%;
    padding: 0 0 1rem;
    margin-bottom: .6rem;
    border: 0;
    border-bottom: 1px solid var(--line);
    background: none;
    text-align: left;
  }

  .lead img {
    width: 100%;
    aspect-ratio: 16 / 9;
    object-fit: cover;
    border-radius: var(--r-panel);
    background: var(--panel-2);
    margin-bottom: .3rem;
  }

  .lead strong { font-size: 1.5rem; line-height: 1.15; }
  .sum { color: var(--ink-2); font-size: .95rem; }

  .meta {
    font-size: .7rem;
    font-weight: 700;
    letter-spacing: .1em;
    text-transform: uppercase;
    color: var(--ink-3);
  }

  .news li button {
    display: flex;
    flex-direction: column;
    gap: .15rem;
    width: 100%;
    padding: .6rem 0;
    border: 0;
    border-bottom: 1px solid var(--line);
    background: none;
    text-align: left;
  }

  .title { font-size: 1rem; font-weight: 600; line-height: 1.25; transition: color .15s ease; }
  .news button:hover .title, .lead:hover strong { color: var(--cyan); }

  .calendar li {
    display: grid;
    grid-template-columns: 2rem 1fr auto;
    gap: .6rem;
    padding: .45rem .3rem;
    border-bottom: 1px solid var(--line);
    font-size: .92rem;
  }

  .calendar li.done { color: var(--ink-3); }
  .calendar li.current { background: color-mix(in oklab, var(--red) 12%, transparent); box-shadow: inset 3px 0 0 var(--red); }
  .rn { color: var(--ink-3); }
  .date { color: var(--ink-2); font-size: .82rem; }

  @media (max-width: 1100px) {
    .layout { grid-template-columns: 1fr; grid-template-areas: 'schedule' 'news' 'standings' 'calendar'; }
    .hero { grid-template-columns: 1fr; }
    .count { grid-column: 1; grid-row: auto; align-items: flex-start; margin-top: 1rem; }
    h1 { font-size: 2.6rem; }
  }
</style>

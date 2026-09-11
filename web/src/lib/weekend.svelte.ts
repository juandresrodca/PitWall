import { poll } from './api';
import type { NewsItem, RaceWeekend, Season, Standings } from './types';

/**
 * Calendar, standings and news, shared by the dashboard and the weekend page.
 * Polled rather than streamed: they change on the scale of minutes.
 */
class WeekendData {
  season = $state<Season | null>(null);
  standings = $state<Standings | null>(null);
  news = $state<NewsItem[]>([]);
  now = $state(Date.now());

  private started = false;

  start(): void {
    if (this.started || typeof window === 'undefined') return;
    this.started = true;
    poll<Season>('/api/schedule', 30 * 60_000, (v) => (this.season = v));
    poll<Standings>('/api/standings', 15 * 60_000, (v) => (this.standings = v));
    poll<NewsItem[]>('/api/news', 5 * 60_000, (v) => (this.news = v));
    setInterval(() => (this.now = Date.now()), 1000);
  }

  /** The weekend in progress, or the next one (weekends end four hours after the race). */
  get current(): RaceWeekend | null {
    const rounds = this.season?.rounds ?? [];
    return rounds.find((r) => Date.parse(r.sessions[r.sessions.length - 1].end) + 4 * 3_600_000 > this.now) ?? null;
  }

  /** The next session to start, or the one running now. */
  get nextSession(): { weekend: RaceWeekend; name: string; kind: string; start: number; end: number; live: boolean } | null {
    for (const weekend of this.season?.rounds ?? []) {
      for (const s of weekend.sessions) {
        const start = Date.parse(s.start);
        const end = Date.parse(s.end);
        if (end > this.now) {
          return { weekend, name: s.name, kind: s.kind, start, end, live: start <= this.now };
        }
      }
    }
    return null;
  }
}

export const weekend = new WeekendData();

export function countdown(ms: number): string {
  if (ms <= 0) return 'now';
  const total = Math.floor(ms / 1000);
  const d = Math.floor(total / 86400);
  const h = Math.floor((total % 86400) / 3600);
  const m = Math.floor((total % 3600) / 60);
  const s = total % 60;
  const pad = (x: number) => String(x).padStart(2, '0');
  return d > 0 ? `${d}d ${pad(h)}:${pad(m)}:${pad(s)}` : `${pad(h)}:${pad(m)}:${pad(s)}`;
}

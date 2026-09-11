import { merge } from './merge';
import type { CompactCars, FeedStatus, Lap } from './types';

type Raw = Record<string, any>;

const DELAY_KEY = 'pitwall.delay';

function readDelay(): number {
  try {
    return Math.max(0, Number(localStorage.getItem(DELAY_KEY)) || 0);
  } catch {
    return 0;
  }
}

/**
 * The browser end of the event stream.
 *
 * Timing state is kept as plain objects (merging hundreds of deltas a minute through deep
 * reactive proxies would be wasteful); components depend on `version`, which is bumped at
 * most once per animation frame after deltas land.
 *
 * Deltas can be held back by `delay` seconds so the dashboard matches a broadcast that runs
 * behind the timing feed — the F1 TV stream is typically 20–60 s behind.
 */
class Feed {
  state: Raw = {};
  positions: CompactCars = { utc: null, cars: {} };
  telemetry: CompactCars = { utc: null, cars: {} };
  laps: Record<string, Lap[]> = {};

  version = $state(0);
  motion = $state(0);
  status = $state<FeedStatus>({ source: 'Idle', connection: 'Connecting', subscriberData: false, auth: { connected: false } });
  online = $state(false);
  /** True once the server has sent its first status (the default above is only a placeholder). */
  statusSeen = $state(false);
  delay = $state(0);

  private clock = { utc: Date.now(), at: 0, speed: 1, running: true };
  private queue: { due: number; apply: () => void }[] = [];
  private dirty = false;
  private moved = false;
  private source: EventSource | null = null;
  private frame = 0;

  connect(): void {
    if (this.source || typeof window === 'undefined') return;
    this.delay = readDelay();
    this.clock.at = performance.now();

    const es = new EventSource('/api/stream');
    this.source = es;

    es.onopen = () => (this.online = true);
    es.onerror = () => (this.online = false);

    es.addEventListener('status', (e) => {
      this.status = JSON.parse((e as MessageEvent).data);
      this.statusSeen = true;
    });

    es.addEventListener('snapshot', (e) => {
      const snap = JSON.parse((e as MessageEvent).data);
      this.queue = [];
      this.state = snap.state ?? {};
      this.positions = snap.pos ?? { utc: null, cars: {} };
      this.telemetry = snap.tel ?? { utc: null, cars: {} };
      this.laps = snap.laps ?? {};
      this.dirty = this.moved = true;
    });

    es.addEventListener('patch', (e) => {
      const { t, d } = JSON.parse((e as MessageEvent).data);
      this.enqueue(() => {
        this.state[t] = merge(this.state[t], d);
        this.dirty = true;
      });
    });

    es.addEventListener('pos', (e) => {
      const value = JSON.parse((e as MessageEvent).data) as CompactCars;
      this.enqueue(() => {
        this.positions = value;
        this.moved = true;
      });
    });

    es.addEventListener('tel', (e) => {
      const value = JSON.parse((e as MessageEvent).data) as CompactCars;
      this.enqueue(() => {
        this.telemetry = value;
        this.moved = true;
      });
    });

    es.addEventListener('lap', (e) => {
      const { n, lap } = JSON.parse((e as MessageEvent).data) as { n: string; lap: Lap };
      this.enqueue(() => {
        const list = (this.laps[n] ??= []);
        if (list.length && list[list.length - 1].l === lap.l) list[list.length - 1] = lap;
        else list.push(lap);
        this.dirty = true;
      });
    });

    es.addEventListener('clock', (e) => {
      const { utc, speed, running } = JSON.parse((e as MessageEvent).data);
      this.clock = { utc: Date.parse(utc), at: performance.now(), speed, running };
    });

    const loop = () => {
      const now = performance.now();
      let i = 0;
      while (i < this.queue.length && this.queue[i].due <= now) {
        this.queue[i].apply();
        i++;
      }
      if (i) this.queue.splice(0, i);
      if (this.dirty) {
        this.dirty = false;
        this.version++;
      }
      if (this.moved) {
        this.moved = false;
        this.motion++;
      }
      this.frame = requestAnimationFrame(loop);
    };
    this.frame = requestAnimationFrame(loop);
  }

  setDelay(seconds: number): void {
    this.delay = Math.max(0, Math.min(120, Math.round(seconds)));
    try {
      localStorage.setItem(DELAY_KEY, String(this.delay));
    } catch {
      /* private mode: the delay just won't persist */
    }
  }

  /** Session wall-clock time in ms, as of what the dashboard is currently showing. */
  now(): number {
    const elapsed = this.clock.running ? (performance.now() - this.clock.at) * this.clock.speed : 0;
    return this.clock.utc + elapsed - this.delay * 1000;
  }

  private enqueue(apply: () => void): void {
    if (this.delay <= 0 && this.queue.length === 0) {
      apply();
      return;
    }
    this.queue.push({ due: performance.now() + this.delay * 1000, apply });
  }
}

export const feed = new Feed();

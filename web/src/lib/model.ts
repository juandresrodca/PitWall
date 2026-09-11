/**
 * Turns raw live-timing topics into what the panels render. Pure functions of the state,
 * so every panel recomputes from the same snapshot in the same frame.
 */
import type { Lap } from './types';

type Raw = Record<string, any>;

export type SessionKind = 'Race' | 'Qualifying' | 'Practice' | 'Unknown';
export type Mark = 'purple' | 'green' | 'yellow' | 'none';
export type Compound = 'SOFT' | 'MEDIUM' | 'HARD' | 'INTERMEDIATE' | 'WET' | 'UNKNOWN';

export interface Driver {
  num: string;
  tla: string;
  name: string;
  first: string;
  last: string;
  team: string;
  color: string;
  headshot?: string;
}

export interface Sector {
  value: string;
  mark: Mark;
  segments: number[];
}

export interface Tyre {
  compound: Compound;
  age: number;
  isNew: boolean;
  stint: number;
  /** Laps completed on this set in the current stint (age minus the laps it had when fitted). */
  stintLaps: number;
}

export interface Row {
  num: string;
  line: number;
  position: string;
  driver: Driver;
  gap: string;
  interval: string;
  catching: boolean;
  last: { value: string; mark: Mark };
  best: { value: string; lap?: number; mark: Mark };
  sectors: Sector[];
  tyre: Tyre;
  stops: number;
  inPit: boolean;
  pitOut: boolean;
  out: boolean;
  knockedOut: boolean;
  laps: number;
  gridDelta: number | null;
  speedTrap: string;
}

export interface Stint {
  compound: Compound;
  isNew: boolean;
  laps: number;
  startAge: number;
}

const str = (v: unknown): string => (v === null || v === undefined ? '' : String(v));
const num = (v: unknown, fallback = 0): number => {
  const n = typeof v === 'number' ? v : parseFloat(str(v));
  return Number.isFinite(n) ? n : fallback;
};
const truthy = (v: unknown): boolean => v === true || v === 'true' || v === 1;

export function sessionKind(state: Raw): SessionKind {
  const type = str(state.SessionInfo?.Type);
  return type === 'Race' || type === 'Qualifying' || type === 'Practice' ? type : 'Unknown';
}

export function sessionTitle(state: Raw): { meeting: string; session: string; circuit: string; country: string } {
  const info = state.SessionInfo ?? {};
  return {
    meeting: str(info.Meeting?.Name) || 'Formula 1',
    session: str(info.Name),
    circuit: str(info.Meeting?.Circuit?.ShortName),
    country: str(info.Meeting?.Country?.Code),
  };
}

export function drivers(state: Raw): Record<string, Driver> {
  const out: Record<string, Driver> = {};
  for (const [n, d] of Object.entries<any>(state.DriverList ?? {})) {
    if (!d || typeof d !== 'object' || !d.Tla) continue;
    out[n] = {
      num: n,
      tla: str(d.Tla),
      name: str(d.FullName) || `${str(d.FirstName)} ${str(d.LastName)}`,
      first: str(d.FirstName),
      last: str(d.LastName),
      team: str(d.TeamName),
      color: d.TeamColour ? `#${d.TeamColour}` : '#6e7584',
      headshot: d.HeadshotUrl || undefined,
    };
  }
  return out;
}

const markOf = (o: any): Mark =>
  !o || !str(o.Value) ? 'none' : o.OverallFastest ? 'purple' : o.PersonalFastest ? 'green' : 'yellow';

export function compoundOf(v: unknown): Compound {
  const c = str(v).toUpperCase();
  return (['SOFT', 'MEDIUM', 'HARD', 'INTERMEDIATE', 'WET'] as const).includes(c as Compound) ? (c as Compound) : 'UNKNOWN';
}

export const compoundLetter: Record<Compound, string> = {
  SOFT: 'S', MEDIUM: 'M', HARD: 'H', INTERMEDIATE: 'I', WET: 'W', UNKNOWN: '?',
};

export function stints(state: Raw, n: string): Stint[] {
  const list = state.TimingAppData?.Lines?.[n]?.Stints;
  if (!Array.isArray(list)) return [];
  return list
    .filter((s: any) => s && s.Compound)
    .map((s: any) => ({
      compound: compoundOf(s.Compound),
      isNew: truthy(s.New),
      laps: Math.max(0, num(s.TotalLaps) - num(s.StartLaps)),
      startAge: num(s.StartLaps),
    }));
}

export function rows(state: Raw): Row[] {
  const lines: Record<string, any> = state.TimingData?.Lines ?? {};
  const app: Record<string, any> = state.TimingAppData?.Lines ?? {};
  const stats: Record<string, any> = state.TimingStats?.Lines ?? {};
  const list = drivers(state);
  const kind = sessionKind(state);
  const part = num(state.TimingData?.SessionPart, 0);

  const result: Row[] = [];
  for (const [n, line] of Object.entries<any>(lines)) {
    if (!line || typeof line !== 'object') continue;
    const driver = list[n] ?? { num: n, tla: n, name: `Car ${n}`, first: '', last: '', team: '', color: '#6e7584' };

    const stintList = Array.isArray(app[n]?.Stints) ? app[n].Stints.filter((s: any) => s && s.Compound) : [];
    const current = stintList[stintList.length - 1];

    const sectors: Sector[] = (Array.isArray(line.Sectors) ? line.Sectors : []).slice(0, 3).map((s: any) => ({
      value: str(s?.Value),
      mark: markOf(s),
      segments: Array.isArray(s?.Segments) ? s.Segments.map((g: any) => num(g?.Status)) : [],
    }));
    while (sectors.length < 3) sectors.push({ value: '', mark: 'none', segments: [] });

    // Qualifying keeps the best lap per part; show the one for the part in progress.
    let best = { value: str(line.BestLapTime?.Value), lap: line.BestLapTime?.Lap, mark: 'none' as Mark };
    if (kind === 'Qualifying' && Array.isArray(line.BestLapTimes) && part > 0) {
      const partBest = line.BestLapTimes[part - 1];
      best = { value: str(partBest?.Value) || best.value, lap: partBest?.Lap, mark: 'none' };
    }
    const pb = stats[n]?.PersonalBestLapTime;
    if (best.value && num(pb?.Position) === 1) best.mark = 'purple';

    let gap = '';
    let interval = '';
    if (kind === 'Race') {
      gap = str(line.GapToLeader);
      interval = str(line.IntervalToPositionAhead?.Value);
    } else if (kind === 'Qualifying' && Array.isArray(line.Stats) && part > 0) {
      gap = str(line.Stats[part - 1]?.TimeDiffToFastest);
      interval = str(line.Stats[part - 1]?.TimeDiffToPositionAhead);
    } else {
      gap = str(line.TimeDiffToFastest);
      interval = str(line.TimeDiffToPositionAhead);
    }

    const grid = parseInt(str(app[n]?.GridPos), 10);
    const pos = parseInt(str(line.Position), 10);

    result.push({
      num: n,
      line: num(line.Line, 99),
      position: str(line.Position),
      driver,
      gap,
      interval,
      catching: !!line.IntervalToPositionAhead?.Catching,
      last: { value: str(line.LastLapTime?.Value), mark: markOf(line.LastLapTime) },
      best,
      sectors,
      tyre: {
        compound: compoundOf(current?.Compound),
        age: num(current?.TotalLaps),
        isNew: truthy(current?.New),
        stint: stintList.length,
        stintLaps: Math.max(0, num(current?.TotalLaps) - num(current?.StartLaps)),
      },
      stops: num(line.NumberOfPitStops),
      inPit: !!line.InPit,
      pitOut: !!line.PitOut,
      out: !!line.Retired || !!line.Stopped,
      knockedOut: !!line.KnockedOut,
      laps: num(line.NumberOfLaps),
      gridDelta: kind === 'Race' && grid > 0 && pos > 0 ? grid - pos : null,
      speedTrap: str(line.Speeds?.ST?.Value),
    });
  }
  return result.sort((a, b) => a.line - b.line);
}

export interface TrackState {
  code: string;
  label: string;
  tone: 'green' | 'yellow' | 'sc' | 'red' | 'idle';
}

export function trackState(state: Raw): TrackState {
  const code = str(state.TrackStatus?.Status);
  const session = str(state.SessionStatus?.Status);
  if (session === 'Finished' || session === 'Finalised' || session === 'Ends') {
    return { code, label: 'Session finished', tone: 'idle' };
  }
  if (session === 'Inactive' || !session) return { code, label: 'Session not started', tone: 'idle' };
  switch (code) {
    case '2': return { code, label: 'Yellow flag', tone: 'yellow' };
    case '4': return { code, label: 'Safety car', tone: 'sc' };
    case '5': return { code, label: 'Red flag', tone: 'red' };
    case '6': return { code, label: 'Virtual safety car', tone: 'sc' };
    case '7': return { code, label: 'VSC ending', tone: 'sc' };
    case '1': return { code, label: 'Track clear', tone: 'green' };
    default: return { code, label: session === 'Aborted' ? 'Session suspended' : 'Track clear', tone: session === 'Aborted' ? 'red' : 'green' };
  }
}

/** Remaining session time in ms, extrapolated while the clock runs. */
export function remaining(state: Raw, nowMs: number): number | null {
  const clock = state.ExtrapolatedClock;
  if (!clock?.Remaining) return null;
  const [h, m, s] = str(clock.Remaining).split(':').map(Number);
  if ([h, m, s].some((x) => !Number.isFinite(x))) return null;
  let ms = ((h * 60 + m) * 60 + s) * 1000;
  if (clock.Extrapolating) {
    const since = nowMs - Date.parse(clock.Utc);
    if (Number.isFinite(since)) ms -= since;
  }
  return Math.max(0, ms);
}

export interface Weather {
  air: number;
  track: number;
  humidity: number;
  pressure: number;
  wind: number;
  windDir: number;
  rain: boolean;
}

export function weather(state: Raw): Weather | null {
  const w = state.WeatherData;
  if (!w || w.AirTemp === undefined) return null;
  return {
    air: num(w.AirTemp),
    track: num(w.TrackTemp),
    humidity: num(w.Humidity),
    pressure: num(w.Pressure),
    wind: num(w.WindSpeed),
    windDir: num(w.WindDirection),
    rain: num(w.Rainfall) > 0,
  };
}

export interface RaceMessage {
  id: number;
  utc: string;
  lap?: number;
  category: string;
  flag?: string;
  scope?: string;
  sector?: number;
  message: string;
  driver?: string;
}

export function raceControl(state: Raw): RaceMessage[] {
  const raw = state.RaceControlMessages?.Messages;
  const list: any[] = Array.isArray(raw) ? raw : raw && typeof raw === 'object' ? Object.values(raw) : [];
  return list
    .map((m, i) => ({
      id: i,
      utc: str(m?.Utc),
      lap: m?.Lap,
      category: str(m?.Category),
      flag: m?.Flag ? str(m.Flag) : undefined,
      scope: m?.Scope ? str(m.Scope) : undefined,
      sector: m?.Sector,
      message: str(m?.Message),
      driver: m?.RacingNumber ? str(m.RacingNumber) : undefined,
    }))
    .filter((m) => m.message)
    .reverse();
}

export interface RadioClip {
  id: number;
  utc: string;
  num: string;
  url: string;
}

export function teamRadio(state: Raw): RadioClip[] {
  const raw = state.TeamRadio?.Captures;
  const list: any[] = Array.isArray(raw) ? raw : raw && typeof raw === 'object' ? Object.values(raw) : [];
  const base = str(state.SessionInfo?.Path);
  return list
    .filter((c) => c?.Path && base)
    .map((c, i) => ({
      id: i,
      utc: str(c.Utc),
      num: str(c.RacingNumber),
      url: `https://livetiming.formula1.com/static/${base}${c.Path}`,
    }))
    .reverse();
}

export interface Prediction {
  num: string;
  current: number;
  predicted: number;
  points: number;
  predictedPoints: number;
}

export function prediction(state: Raw): Prediction[] {
  const list = state.ChampionshipPrediction?.Drivers;
  if (!list || typeof list !== 'object') return [];
  return Object.entries<any>(list)
    .filter(([, d]) => d)
    .map(([n, d]) => ({
      num: n,
      current: num(d.CurrentPosition),
      predicted: num(d.PredictedPosition),
      points: num(d.CurrentPoints),
      predictedPoints: num(d.PredictedPoints),
    }))
    .sort((a, b) => a.predicted - b.predicted);
}

/**
 * Lap-time trend over the current stint in seconds per lap (least squares).
 * The stint is taken from its lap count, not from each lap's compound label, which the feed
 * occasionally gets wrong and corrects later. The out-lap, pit laps, laps under
 * neutralisation and outliers (traffic, mistakes) are left out. Fuel burn is not corrected
 * for, so this reads as "pace trend", not pure degradation.
 */
export function stintTrend(laps: Lap[] | undefined, stintLaps: number): { slope: number; laps: number } | null {
  if (!laps?.length || stintLaps < 5) return null;
  const latest = laps[laps.length - 1].l;
  const firstFlying = latest - stintLaps + 2;
  const usable = laps.filter((l) => l.l >= firstFlying && !(l.f & 3));
  if (usable.length < 4) return null;
  const sorted = usable.map((l) => l.t).sort((a, b) => a - b);
  const median = sorted[Math.floor(sorted.length / 2)];
  const clean = usable.filter((l) => l.t <= median * 1.025).slice(-12);
  if (clean.length < 4) return null;

  const n = clean.length;
  const mx = clean.reduce((s, l) => s + l.l, 0) / n;
  const my = clean.reduce((s, l) => s + l.t, 0) / n;
  let sxy = 0;
  let sxx = 0;
  for (const l of clean) {
    sxy += (l.l - mx) * (l.t - my);
    sxx += (l.l - mx) ** 2;
  }
  return sxx === 0 ? null : { slope: sxy / sxx / 1000, laps: n };
}

export function mmss(ms: number | null): string {
  if (ms === null) return '--:--';
  const total = Math.floor(ms / 1000);
  const h = Math.floor(total / 3600);
  const m = Math.floor((total % 3600) / 60);
  const s = total % 60;
  const pad = (x: number) => String(x).padStart(2, '0');
  return h > 0 ? `${h}:${pad(m)}:${pad(s)}` : `${pad(m)}:${pad(s)}`;
}

export function lapTime(ms: number): string {
  const m = Math.floor(ms / 60000);
  const s = (ms % 60000) / 1000;
  return m > 0 ? `${m}:${s.toFixed(3).padStart(6, '0')}` : s.toFixed(3);
}

export function localTime(utc: string | number | undefined, withSeconds = false): string {
  if (utc === undefined || utc === '') return '';
  const d = new Date(typeof utc === 'number' ? utc : utc.endsWith('Z') || /[+-]\d\d:\d\d$/.test(utc) ? utc : `${utc}Z`);
  if (Number.isNaN(d.getTime())) return '';
  return d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', ...(withSeconds ? { second: '2-digit' } : {}) });
}

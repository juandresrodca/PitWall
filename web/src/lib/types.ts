/** Shapes of the server's own endpoints. The live-timing topics stay loosely typed (see model.ts). */

export type FeedSource = 'Idle' | 'Live' | 'Replay';
export type FeedConnection = 'Stopped' | 'Connecting' | 'Connected' | 'Reconnecting' | 'Loading' | 'Error';

export interface ReplayStatus {
  path: string;
  title?: string;
  offset: number;
  duration: number;
  greenFlag?: number;
  speed: number;
  playing: boolean;
  loadProgress: number;
}

export interface AuthStatus {
  /** A usable F1 TV token is saved. */
  signedIn?: boolean;
  /** The live feed is authenticated with it right now. */
  connected: boolean;
  subscription?: string;
  product?: string;
  expires?: string;
  problem?: string;
}

export interface FeedStatus {
  source: FeedSource;
  connection: FeedConnection;
  message?: string;
  subscriberData: boolean;
  sessionPath?: string;
  replay?: ReplayStatus;
  auth: AuthStatus;
}

export interface CompactCars {
  utc: string | null;
  cars: Record<string, number[]>;
}

export interface Lap {
  l: number; // lap number
  t: number; // milliseconds
  c: string; // compound
  a: number; // tyre age
  f: number; // flags: 1 pit, 2 neutralised, 4 yellow
}

export interface WeekendSession {
  kind: string;
  name: string;
  start: string;
  end: string;
}

export interface RaceWeekend {
  season: number;
  round: number;
  name: string;
  circuitId: string;
  circuit: string;
  locality: string;
  country: string;
  latitude: number;
  longitude: number;
  sessions: WeekendSession[];
}

export interface Season {
  year: number;
  rounds: RaceWeekend[];
}

export interface DriverStanding {
  position: number;
  points: number;
  wins: number;
  code: string;
  number: string;
  firstName: string;
  lastName: string;
  team: string;
  nationality: string;
}

export interface TeamStanding {
  position: number;
  points: number;
  wins: number;
  name: string;
  nationality: string;
}

export interface Standings {
  season: number;
  round: number;
  drivers: DriverStanding[];
  teams: TeamStanding[];
}

export interface NewsItem {
  title: string;
  url: string;
  source: string;
  published?: string;
  summary?: string;
  image?: string;
}

export interface TrackMap {
  circuitKey: number;
  builtFrom: string;
  points: [number, number][];
  length: number;
}

export interface ArchiveMeeting {
  key: number;
  name: string;
  location: string;
  country: string;
  sessions: { key: number; name: string; type: string; startUtc: string; path: string }[];
}

export interface Forecast {
  current?: { time: number; temperature_2m: number; precipitation: number; wind_speed_10m: number; weather_code: number };
  hourly?: {
    time: number[];
    temperature_2m: number[];
    precipitation_probability: number[];
    precipitation: number[];
    wind_speed_10m: number[];
    weather_code: number[];
  };
  weekend?: { name: string; locality: string; country: string };
}

export type UpdateState =
  | 'Unknown' | 'Checking' | 'UpToDate' | 'Available' | 'Downloading' | 'Ready' | 'Failed' | 'NotSupported';

export interface UpdateStatus {
  current: string;
  latest?: string;
  notes?: string;
  published?: string;
  releaseUrl?: string;
  state: UpdateState;
  progress: number;
  error?: string;
}

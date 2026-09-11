/** REST helpers. State-changing calls carry X-PitWall, which the server requires. */

export async function getJson<T>(path: string, init?: RequestInit): Promise<T | null> {
  try {
    const response = await fetch(path, init);
    if (response.status === 204 || !response.ok) return null;
    return (await response.json()) as T;
  } catch {
    return null;
  }
}

export async function command(path: string, body?: unknown, method = 'POST'): Promise<boolean> {
  try {
    const response = await fetch(path, {
      method,
      headers: { 'X-PitWall': '1', 'Content-Type': 'application/json' },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
    return response.ok;
  } catch {
    return false;
  }
}

/**
 * Polls a JSON endpoint on an interval and exposes the latest good value.
 * A failed refresh keeps the previous value on screen.
 */
export function poll<T>(path: string, everyMs: number, onValue: (value: T) => void): () => void {
  let stopped = false;
  const run = async () => {
    const value = await getJson<T>(path);
    if (!stopped && value !== null) onValue(value);
  };
  run();
  const timer = setInterval(run, everyMs);
  return () => {
    stopped = true;
    clearInterval(timer);
  };
}

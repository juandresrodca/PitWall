/**
 * Mirror of PitWall.Core.Json.JsonMerge: the browser applies the same deltas the server
 * applied, so both sides hold identical state.
 *
 *  - objects merge key by key
 *  - an object with integer keys merges into an array by index
 *  - `_deleted` lists keys (or indexes) to remove
 *  - anything else replaces the old value
 */
type Json = null | boolean | number | string | Json[] | { [key: string]: Json };

const DELETED = '_deleted';

const isObject = (v: unknown): v is Record<string, Json> =>
  typeof v === 'object' && v !== null && !Array.isArray(v);

export function merge(target: Json | undefined, patch: Json): Json {
  if (!isObject(patch)) return patch;

  if (Array.isArray(target)) {
    for (const [key, value] of Object.entries(patch)) {
      if (key === DELETED) {
        const indexes = (Array.isArray(value) ? value : [value])
          .map(Number)
          .filter((i) => Number.isInteger(i) && i >= 0 && i < target.length)
          .sort((a, b) => b - a);
        for (const i of indexes) target.splice(i, 1);
        continue;
      }
      const index = Number(key);
      if (!Number.isInteger(index) || index < 0) continue;
      while (target.length <= index) target.push(null);
      const existing = target[index];
      target[index] = isObject(value) && (isObject(existing) || Array.isArray(existing))
        ? merge(existing, value)
        : value;
    }
    return target;
  }

  const base: Record<string, Json> = isObject(target) ? target : {};
  for (const [key, value] of Object.entries(patch)) {
    if (key === DELETED) {
      for (const name of Array.isArray(value) ? value : [value]) delete base[String(name)];
      continue;
    }
    const existing = base[key];
    base[key] = isObject(value) && (isObject(existing) || Array.isArray(existing))
      ? merge(existing, value)
      : value;
  }
  return base;
}

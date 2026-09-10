/**
 * Every `localStorage` access in the app goes through here.
 *
 * `localStorage` is not always there to be used: a browser configured to block site data
 * (Chrome's "Block all cookies", Safari's equivalent) throws a `SecurityError` on the very
 * first property access, and a storage that is full or partitioned away makes `setItem`
 * throw. Both happen inside a render or an effect, where an uncaught throw takes the whole
 * page down — for a value that is only ever a convenience. On top of that, anything stored
 * as JSON can come back unparseable (hand-edited, truncated, or written by an older version
 * of the app).
 *
 * So nothing here throws. A read that cannot be satisfied returns `null`; a write that
 * cannot happen is dropped. That is the right trade for everything the app actually keeps
 * in localStorage — UI preferences, in-progress quiz answers, "tour already seen" flags —
 * none of which is worth interrupting the user over.
 */

/** Reads a raw string. Returns null when the key is absent or storage is unavailable. */
export function readString(key: string): string | null {
  try {
    return localStorage.getItem(key);
  } catch {
    return null;
  }
}

/** Writes a raw string. Silently does nothing when storage is unavailable or full. */
export function writeString(key: string, value: string): void {
  try {
    localStorage.setItem(key, value);
  } catch {
    /* unavailable or full — see the module comment */
  }
}

/** Removes a key. Silently does nothing when storage is unavailable. */
export function removeKey(key: string): void {
  try {
    localStorage.removeItem(key);
  } catch {
    /* unavailable — see the module comment */
  }
}

/**
 * Reads and parses a JSON value, returning null when the key is absent, storage is
 * unavailable, or the stored text is not valid JSON.
 *
 * `T` is an unchecked assertion — `JSON.parse` cannot know the shape it produced. Callers
 * that care whether the value still matches what they wrote (an older app version, a
 * hand-edited entry) must check it themselves; this only guarantees that a corrupt entry
 * comes back as null instead of throwing.
 */
export function readJson<T>(key: string): T | null {
  const raw = readString(key);
  if (raw === null) return null;
  try {
    return JSON.parse(raw) as T;
  } catch {
    return null;
  }
}

/** Serializes a value to JSON and stores it. Values that cannot be serialized are dropped
 * like any other failed write. */
export function writeJson(key: string, value: unknown): void {
  try {
    writeString(key, JSON.stringify(value));
  } catch {
    /* not serializable (a cycle, a BigInt) — see the module comment */
  }
}

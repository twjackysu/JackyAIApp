import { afterEach, describe, expect, it, vi } from 'vitest';

import { readJson, readString, removeKey, writeJson, writeString } from './storage';

/** Replaces the jsdom localStorage with one whose every method throws, the way a browser
 * configured to block site data behaves. Returns a restore function. */
function withBlockedStorage(): () => void {
  const original = Object.getOwnPropertyDescriptor(window, 'localStorage');
  const blocked = {
    getItem: () => {
      throw new DOMException('The operation is insecure.', 'SecurityError');
    },
    setItem: () => {
      throw new DOMException('The operation is insecure.', 'SecurityError');
    },
    removeItem: () => {
      throw new DOMException('The operation is insecure.', 'SecurityError');
    },
  };
  Object.defineProperty(window, 'localStorage', { value: blocked, configurable: true });
  return () => {
    if (original) Object.defineProperty(window, 'localStorage', original);
  };
}

afterEach(() => {
  localStorage.clear();
  vi.restoreAllMocks();
});

describe('readString / writeString', () => {
  it('round-trips a value', () => {
    writeString('k', 'v');
    expect(readString('k')).toBe('v');
  });

  it('returns null for a key that was never written', () => {
    expect(readString('missing')).toBeNull();
  });
});

describe('readJson / writeJson', () => {
  it('round-trips an object', () => {
    writeJson('k', { a: 1, b: ['x'] });
    expect(readJson<{ a: number; b: string[] }>('k')).toEqual({ a: 1, b: ['x'] });
  });

  it('returns null instead of throwing when the stored text is not valid JSON', () => {
    localStorage.setItem('k', '{not json');
    expect(readJson('k')).toBeNull();
  });

  it('returns null for a missing key', () => {
    expect(readJson('missing')).toBeNull();
  });

  it('drops a value that cannot be serialized rather than throwing', () => {
    const cyclic: Record<string, unknown> = {};
    cyclic.self = cyclic;
    expect(() => writeJson('k', cyclic)).not.toThrow();
    expect(readString('k')).toBeNull();
  });
});

describe('removeKey', () => {
  it('removes a stored value', () => {
    writeString('k', 'v');
    removeKey('k');
    expect(readString('k')).toBeNull();
  });
});

describe('when the browser blocks storage entirely', () => {
  it('reads return null and writes are dropped, without throwing', () => {
    const restore = withBlockedStorage();
    try {
      expect(() => writeString('k', 'v')).not.toThrow();
      expect(() => writeJson('k', { a: 1 })).not.toThrow();
      expect(() => removeKey('k')).not.toThrow();
      expect(readString('k')).toBeNull();
      expect(readJson('k')).toBeNull();
    } finally {
      restore();
    }
  });
});

import { describe, expect, it } from 'vitest';

import { periodOf } from './period';

describe('periodOf', () => {
  const now = new Date(2026, 9, 21, 9, 0);
  const at = (y: number, m: number, d: number, h = 12) => new Date(y, m, d, h).toISOString();

  it('goes by calendar day, not 24-hour spans', () => {
    expect(periodOf(at(2026, 9, 21, 0), now)).toBe('今天');
    expect(periodOf(at(2026, 9, 20, 23), now)).toBe('昨天');
  });

  it('puts older conversations under wider headings', () => {
    expect(periodOf(at(2026, 9, 15), now)).toBe('過去 7 天');
    expect(periodOf(at(2026, 9, 14), now)).toBe('過去 30 天');
    expect(periodOf(at(2026, 8, 1), now)).toBe('更早');
  });
});

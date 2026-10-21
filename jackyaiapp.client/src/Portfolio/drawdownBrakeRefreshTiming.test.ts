import { describe, expect, it } from 'vitest';

import {
  formatLastChecked,
  PRICE_REFRESH_COOLDOWN_SECONDS,
  sanitizeRetryAfter,
  secondsLeft,
  THROTTLED_FALLBACK_SECONDS,
} from './drawdownBrakeRefreshTiming';

describe('formatLastChecked', () => {
  it('UTC ISO → 當地時間 MM/DD HH:mm（不論主機時區）', () => {
    const iso = new Date(2026, 9, 9, 15, 5).toISOString();
    expect(formatLastChecked(iso)).toBe('10/09 15:05');
  });

  it('個位數補零；午夜顯示 00:xx 而不是 24:xx', () => {
    expect(formatLastChecked(new Date(2026, 0, 2, 0, 7).toISOString())).toBe('01/02 00:07');
  });

  it.each([null, undefined, '', 'not-a-date', '1970-01-01T00:00:00Z', '1969-12-31T00:00:00Z'])(
    '%j → null（呼叫端省略，不顯示 1970 或 Invalid Date）',
    (value) => {
      expect(formatLastChecked(value)).toBeNull();
    },
  );
});

describe('secondsLeft', () => {
  it('無條件進位：還剩 0.2 秒仍顯示 1，不會提早放行', () => {
    expect(secondsLeft(10_200, 10_000)).toBe(1);
    expect(secondsLeft(11_000, 10_000)).toBe(1);
    expect(secondsLeft(11_001, 10_000)).toBe(2);
  });

  it('到點或已過 → 0，不出現負數；0 時間戳（沒有冷卻）→ 0', () => {
    expect(secondsLeft(10_000, 10_000)).toBe(0);
    expect(secondsLeft(9_000, 10_000)).toBe(0);
    expect(secondsLeft(0, Date.now())).toBe(0);
  });
});

describe('sanitizeRetryAfter', () => {
  it('正常值：整數秒原樣，小數無條件進位', () => {
    expect(sanitizeRetryAfter(45)).toBe(45);
    expect(sanitizeRetryAfter(1)).toBe(1);
    expect(sanitizeRetryAfter(12.2)).toBe(13);
  });

  it.each([null, undefined, 0, -5, Number.NaN, Number.POSITIVE_INFINITY])(
    '%j → 後備值（後端全站冷卻 60 秒）',
    (value) => {
      expect(sanitizeRetryAfter(value)).toBe(THROTTLED_FALLBACK_SECONDS);
      expect(THROTTLED_FALLBACK_SECONDS).toBe(60);
    },
  );

  it('異常大的值截斷在 5 分鐘，不會把按鈕鎖上很久', () => {
    expect(sanitizeRetryAfter(86_400)).toBe(300);
  });
});

describe('常數', () => {
  it('持股報價客戶端冷卻為 30 秒（ARCHITECTURE.md／TASK-043）', () => {
    expect(PRICE_REFRESH_COOLDOWN_SECONDS).toBe(30);
  });
});

import { describe, expect, it } from 'vitest';

import { defaultRange, firstRecordDate, localToday, minusOneMonth } from './netWorthRange';

describe('minusOneMonth', () => {
  it('同一天的上個月', () => {
    expect(minusOneMonth('2026-10-07')).toBe('2026-09-07');
  });

  it('跨年', () => {
    expect(minusOneMonth('2026-01-15')).toBe('2025-12-15');
  });

  it('上個月比較短時落在月底，跟後端 DateOnly.AddMonths 一致', () => {
    // 若不一致，欄位顯示的起始日會跟伺服器實際算的起點差幾天。
    expect(minusOneMonth('2026-03-31')).toBe('2026-02-28');
    expect(minusOneMonth('2028-03-31')).toBe('2028-02-29'); // 閏年
    expect(minusOneMonth('2026-10-31')).toBe('2026-09-30');
  });
});

describe('defaultRange', () => {
  it('紀錄超過一個月：近一個月到今天', () => {
    expect(defaultRange('2026-10-07', '2026-06-01')).toEqual({
      from: '2026-09-07',
      to: '2026-10-07',
    });
  });

  it('紀錄不足一個月：從第一筆紀錄到今天', () => {
    expect(defaultRange('2026-10-07', '2026-09-28')).toEqual({
      from: '2026-09-28',
      to: '2026-10-07',
    });
  });

  it('第一筆紀錄剛好是一個月前', () => {
    expect(defaultRange('2026-10-07', '2026-09-07')).toEqual({
      from: '2026-09-07',
      to: '2026-10-07',
    });
  });

  it('還沒有任何紀錄：近一個月（圖表會顯示資料不足）', () => {
    expect(defaultRange('2026-10-07', undefined)).toEqual({ from: '2026-09-07', to: '2026-10-07' });
  });
});

describe('firstRecordDate', () => {
  it('取最早的一筆，不依賴傳入順序', () => {
    expect(firstRecordDate(['2026-09-28', '2026-06-01', '2026-08-15'])).toBe('2026-06-01');
  });

  it('沒有紀錄時是 undefined', () => {
    expect(firstRecordDate([])).toBeUndefined();
  });
});

describe('localToday', () => {
  it('用本地時區的日期，不是 UTC', () => {
    // 台灣早上 07:00 時 UTC 還是前一天；用 toISOString 會把結束日少算一天。
    const morning = new Date(2026, 9, 7, 7, 0, 0); // 本地時間 10-07 07:00
    expect(localToday(morning)).toBe('2026-10-07');
  });

  it('月、日補零', () => {
    expect(localToday(new Date(2026, 0, 5))).toBe('2026-01-05');
  });
});

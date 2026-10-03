import { describe, expect, it } from 'vitest';

import {
  addMonths,
  addYears,
  applyEdit,
  defaultRange,
  firstRecordDate,
  localToday,
  minusOneMonth,
  presetRange,
} from './netWorthRange';

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

describe('addYears', () => {
  it('前後一年', () => {
    expect(addYears('2026-10-07', -1)).toBe('2025-10-07');
    expect(addYears('2025-10-07', 1)).toBe('2026-10-07');
  });

  it('2/29 遇到平年落在 2/28，跟後端 DateOnly.AddYears 一致', () => {
    expect(addYears('2028-02-29', -1)).toBe('2027-02-28');
    expect(addYears('2028-02-29', 1)).toBe('2029-02-28');
  });
});

describe('applyEdit', () => {
  const range = { from: '2026-09-07', to: '2026-10-07' };

  it('區間內的修改照原樣套用', () => {
    expect(applyEdit(range, 'from', '2026-08-01')).toEqual({
      from: '2026-08-01',
      to: '2026-10-07',
    });
    expect(applyEdit(range, 'to', '2026-09-30')).toEqual({ from: '2026-09-07', to: '2026-09-30' });
  });

  it('剛好一年不會被調整', () => {
    expect(applyEdit(range, 'from', '2025-10-07')).toEqual({
      from: '2025-10-07',
      to: '2026-10-07',
    });
  });

  it('起始日往前超過一年：結束日跟著移到起始日加一年', () => {
    // 不用先把結束日往回調，就能直接跳到兩年前去看。
    expect(applyEdit(range, 'from', '2024-03-15')).toEqual({
      from: '2024-03-15',
      to: '2025-03-15',
    });
  });

  it('結束日往後讓區間超過一年：起始日跟著移到結束日減一年', () => {
    const old = { from: '2024-01-10', to: '2024-06-30' };
    expect(applyEdit(old, 'to', '2026-10-07')).toEqual({ from: '2025-10-07', to: '2026-10-07' });
  });

  it('起始日改到結束日之後：結束日跟上', () => {
    expect(applyEdit(range, 'from', '2026-10-05')).toEqual({
      from: '2026-10-05',
      to: '2026-10-07',
    });
    expect(applyEdit(range, 'from', '2026-12-01')).toEqual({
      from: '2026-12-01',
      to: '2026-12-01',
    });
  });

  it('結束日改到起始日之前：起始日跟上', () => {
    expect(applyEdit(range, 'to', '2026-08-20')).toEqual({ from: '2026-08-20', to: '2026-08-20' });
  });

  it('不論改哪一邊，結果都不超過一年、也不會顛倒', () => {
    const edits: ['from' | 'to', string][] = [
      ['from', '2020-01-01'],
      ['to', '2030-01-01'],
      ['from', '2026-12-31'],
      ['to', '2019-05-05'],
    ];
    for (const [field, value] of edits) {
      const r = applyEdit(range, field, value);
      expect(r.from <= r.to).toBe(true);
      expect(r.from >= addYears(r.to, -1)).toBe(true);
      expect(r[field]).toBe(value); // 使用者改的那一邊永遠照他輸入的
    }
  });
});

describe('addMonths', () => {
  it('往前幾個月，跨年也對', () => {
    expect(addMonths('2026-10-07', -3)).toBe('2026-07-07');
    expect(addMonths('2026-02-15', -6)).toBe('2025-08-15');
    expect(addMonths('2026-10-07', -12)).toBe('2025-10-07');
  });

  it('落在較短的月份時取月底，跟後端 DateOnly.AddMonths 一致', () => {
    expect(addMonths('2026-05-31', -3)).toBe('2026-02-28');
    expect(addMonths('2028-08-31', -6)).toBe('2028-02-29'); // 閏年
  });

  it('往後也可以', () => {
    expect(addMonths('2026-11-30', 3)).toBe('2027-02-28');
  });

  it('十二個月等於一年，2/29 也是', () => {
    // 1Y 按鈕用 addMonths(-12)，一年上限用 addYears(-1)，兩者不一致的話 1Y 會被當成超過上限。
    for (const d of ['2026-10-07', '2028-02-29', '2027-01-31']) {
      expect(addMonths(d, -12)).toBe(addYears(d, -1));
    }
  });
});

describe('presetRange', () => {
  it('從今天往回推，結束日是今天', () => {
    expect(presetRange('2026-10-07', 3, '2025-01-01')).toEqual({
      from: '2026-07-07',
      to: '2026-10-07',
    });
    expect(presetRange('2026-10-07', 12, '2020-01-01')).toEqual({
      from: '2025-10-07',
      to: '2026-10-07',
    });
  });

  it('紀錄比區間短：起始日是第一筆紀錄', () => {
    expect(presetRange('2026-10-07', 6, '2026-08-20')).toEqual({
      from: '2026-08-20',
      to: '2026-10-07',
    });
  });

  it('1Y 剛好是一年上限，改日期的規則不會再去動它', () => {
    const r = presetRange('2026-10-07', 12, '2020-01-01');
    expect(applyEdit(r, 'from', r.from)).toEqual(r);
  });
});

import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { DEFAULT_WIDE_RANGE_DAYS, PRICE_HISTORY_DAYS, WIDE_RANGES } from './dayRanges';
import { HistoryLimitNote } from './HistoryLimitNote';

import type { HistoryBackfill } from '@/apis/portfolioApis/types';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  // Only Date is faked (timers stay real, React's act needs them): the note compares the chart's
  // first day with "today − days".
  vi.useFakeTimers({ toFake: ['Date'] });
  vi.setSystemTime(new Date(2026, 9, 19, 12));
  container = document.createElement('div');
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(() => {
  act(() => root.unmount());
  container.remove();
  vi.useRealTimers();
});

const render = (
  days: number,
  backfill?: HistoryBackfill | null,
  firstPointDate?: string | null,
) => {
  act(() =>
    root.render(
      <HistoryLimitNote days={days} backfill={backfill} firstPointDate={firstPointDate} />,
    ),
  );
  return container.textContent ?? '';
};

const backfill = (state: HistoryBackfill['state']): HistoryBackfill => ({
  state,
  coveredFrom: '2026-02-03',
  targetFrom: '2025-10-19',
});

// 2026-10-19 − 365 days.
const ONE_YEAR_AGO = '2025-10-19';

describe('HistoryLimitNote', () => {
  it('says nothing without a backfill when the range is inside the price history', () => {
    for (const range of WIDE_RANGES.filter((r) => r.days <= PRICE_HISTORY_DAYS)) {
      expect(render(range.days, null, '2026-08-01')).toBe('');
      expect(render(range.days, undefined, undefined)).toBe('');
    }
  });

  it('says what is being fetched, and that the numbers are provisional, while Running', () => {
    const text = render(365, backfill('Running'), '2026-02-03');
    expect(text).toContain('2026-02-03');
    expect(text).toContain('2025-10-19');
    expect(text).toContain('補齊');
    expect(text).toContain('暫定');
  });

  it('says so while Running even for a range inside the price history', () => {
    // 6M is 180 days, further back than the five months the server never fetches for.
    expect(render(180, backfill('Running'), '2026-04-22')).toContain('正在補齊');
  });

  it('says some prices could not be fetched, and that refreshing tries again, when Stalled', () => {
    const text = render(365, backfill('Stalled'), '2026-02-03');
    expect(text).toContain('暫時抓不到');
    expect(text).toContain('重新整理');
    expect(text).not.toContain('正在補齊');
  });

  it('says nothing once Complete and the chart starts where the range does', () => {
    expect(render(365, backfill('Complete'), ONE_YEAR_AGO)).toBe('');
    // A weekend, holiday or a few days' wait for the first snapshot is not worth a note.
    expect(render(365, backfill('Complete'), '2025-11-01')).toBe('');
  });

  it('says where the chart starts when it begins well after the range', () => {
    for (const bf of [backfill('Complete'), null, undefined]) {
      const text = render(365, bf, '2026-03-02');
      expect(text).toContain('圖從 2026-03-02 開始');
      expect(text).toContain('更早沒有持倉紀錄');
    }
  });

  it('does not note a late start for a range inside the price history', () => {
    expect(render(180, null, '2026-09-01')).toBe('');
  });

  it('never claims the price history is only about six months', () => {
    for (const range of WIDE_RANGES) {
      for (const bf of [null, backfill('Complete'), backfill('Running'), backfill('Stalled')]) {
        expect(render(range.days, bf, '2026-03-02')).not.toMatch(/6 個月/);
      }
    }
  });
});

describe('the long-range charts open on a range their data fully covers', () => {
  it('starts at three months, one of the offered ranges, and inside the price history', () => {
    expect(WIDE_RANGES.map((r) => r.days)).toContain(DEFAULT_WIDE_RANGE_DAYS);
    expect(DEFAULT_WIDE_RANGE_DAYS).toBeLessThanOrEqual(PRICE_HISTORY_DAYS);
    expect(render(DEFAULT_WIDE_RANGE_DAYS, null, '2026-07-01')).toBe('');
  });

  it('offers 3M and 6M as covered ranges and 1Y and 3Y as the ones that need a backfill', () => {
    const covered = WIDE_RANGES.filter((r) => r.days <= PRICE_HISTORY_DAYS).map((r) => r.label);
    const beyond = WIDE_RANGES.filter((r) => r.days > PRICE_HISTORY_DAYS).map((r) => r.label);
    expect(covered).toEqual(['3M', '6M']);
    expect(beyond).toEqual(['1Y', '3Y']);
  });
});

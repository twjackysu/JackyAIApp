import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';

import { DEFAULT_WIDE_RANGE_DAYS, PRICE_HISTORY_DAYS, WIDE_RANGES } from './dayRanges';
import { HistoryLimitNote } from './HistoryLimitNote';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  container = document.createElement('div');
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(() => {
  act(() => root.unmount());
  container.remove();
});

const render = (days: number) => {
  act(() => root.render(<HistoryLimitNote days={days} />));
  return container.textContent ?? '';
};

describe('HistoryLimitNote', () => {
  it('says nothing for a range the price history covers', () => {
    for (const range of WIDE_RANGES.filter((r) => r.days <= PRICE_HISTORY_DAYS)) {
      expect(render(range.days)).toBe('');
    }
  });

  it('warns for a range that reaches back further', () => {
    for (const range of WIDE_RANGES.filter((r) => r.days > PRICE_HISTORY_DAYS)) {
      expect(render(range.days)).toContain('約 6 個月');
    }
  });
});

describe('the long-range charts open on a range their data fully covers', () => {
  it('starts at three months, one of the offered ranges, and inside the price history', () => {
    expect(WIDE_RANGES.map((r) => r.days)).toContain(DEFAULT_WIDE_RANGE_DAYS);
    expect(DEFAULT_WIDE_RANGE_DAYS).toBeLessThanOrEqual(PRICE_HISTORY_DAYS);
    expect(render(DEFAULT_WIDE_RANGE_DAYS)).toBe('');
  });

  it('offers 3M and 6M as covered ranges and 1Y and 3Y as the ones that need the warning', () => {
    const covered = WIDE_RANGES.filter((r) => r.days <= PRICE_HISTORY_DAYS).map((r) => r.label);
    const beyond = WIDE_RANGES.filter((r) => r.days > PRICE_HISTORY_DAYS).map((r) => r.label);
    expect(covered).toEqual(['3M', '6M']);
    expect(beyond).toEqual(['1Y', '3Y']);
  });
});

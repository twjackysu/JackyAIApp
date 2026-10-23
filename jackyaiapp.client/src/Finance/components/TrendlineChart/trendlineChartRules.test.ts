import { describe, expect, it } from 'vitest';

import { lineHint } from './copy';
import { shownPresetOf } from './useRangeChoice';

import type { RangePreset } from '../../utils/trendlineRange';

describe('lineHint', () => {
  it('drawing comes first, then a selected line, then how to start', () => {
    expect(lineHint('resistance', true)).toMatch(/^畫壓力線：在圖上點起點和終點/);
    expect(lineHint(null, true)).toMatch(/^拖曳圓點調整端點/);
    expect(lineHint(null, false)).toBe('點一下線就能拖曳或刪除，按「畫線」可以自己畫。');
  });
});

describe('shownPresetOf', () => {
  const presets = [
    { id: '6m', label: '半年', months: 6 },
    { id: 'all', label: '全部', months: null },
  ] as RangePreset[];

  it('a preset still offered is shown as picked; one no longer offered reads as 全部', () => {
    expect(shownPresetOf({ preset: '6m' }, presets)).toBe('6m');
    expect(shownPresetOf({ preset: '5y' }, presets)).toBe('all');
  });

  it('a start date of its own picks no preset', () => {
    expect(shownPresetOf({ from: '2025-03-01' }, presets)).toBeNull();
  });
});

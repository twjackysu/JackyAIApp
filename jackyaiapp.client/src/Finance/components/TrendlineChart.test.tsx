import { act, useImperativeHandle, type Ref } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import taiex from '../utils/__fixtures__/taiex-2024-2026.json';

import { TrendlineChart } from './TrendlineChart';

import type { CandlestickChartOption } from '../charts/registerFinanceCharts';
import type { PriceBarsData } from '@/apis/financeApis/types';

const mocks = vi.hoisted(() => ({ option: null as CandlestickChartOption | null }));

// A canvas can't run under jsdom. The stand-in lays the chart out the way ECharts would — bars
// 10 px apart, the fixed price range over 300 px — so the overlay has something to measure.
vi.mock('@/charts/ReactECharts', () => ({
  default: function MockChart({
    ref,
    option,
  }: {
    ref?: Ref<unknown>;
    option: CandlestickChartOption;
  }) {
    mocks.option = option;
    useImperativeHandle(ref, () => ({
      getEchartsInstance: () => ({
        convertToPixel: (finder: { xAxisIndex?: number }, value: string | number) => {
          const x = option.xAxis as { data: string[] };
          const y = option.yAxis as { min: number; max: number };
          return 'xAxisIndex' in finder
            ? 5 + 10 * x.data.indexOf(value as string)
            : 310 - (300 * ((value as number) - y.min)) / (y.max - y.min);
        },
      }),
    }));
    return <div data-testid="chart" />;
  },
}));

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

const data: PriceBarsData = {
  code: 'TAIEX',
  name: '加權指數',
  asOf: '2026-10-16',
  bars: (taiex.bars as [string, number, number, number, number][]).map(
    ([date, open, high, low, close]) => ({ date, open, high, low, close }),
  ),
};
const DRAWINGS = 'finance.trendlines.TAIEX';
const RANGE = 'finance.trendlineRange.TAIEX';

let container: HTMLDivElement;
let root: Root;

const render = () => act(() => root.render(<TrendlineChart data={data} defaultRange="1y" />));
const text = () => container.textContent ?? '';
const startDate = () => (container.querySelector('input[type="date"]') as HTMLInputElement).value;
const lineCount = () => container.querySelectorAll('[data-line-id]').length;
const button = (label: string) =>
  [...container.querySelectorAll('button')].find((b) => b.textContent?.includes(label)) ?? null;
const click = (el: Element | null) => act(() => (el as HTMLElement).click());
const press = (el: Element, x: number, y: number) =>
  act(() => {
    for (const type of ['pointerdown', 'pointerup'])
      el.dispatchEvent(
        new PointerEvent(type, { bubbles: true, pointerId: 1, button: 0, clientX: x, clientY: y }),
      );
  });
const stored = () => JSON.parse(localStorage.getItem(DRAWINGS) ?? 'null');
/** The menu renders outside the chart, at the end of the page. */
const menuItem = (label: string) =>
  [...document.querySelectorAll('[role="menuitem"]')].find((m) => m.textContent?.includes(label)) ??
  null;
/** What the tooltip shows for bar `index` of the chart. */
const tooltip = (index: number) => {
  const formatter = (mocks.option?.tooltip as { formatter: (p: unknown) => string }).formatter;
  return formatter([{ dataIndex: index }]);
};

beforeEach(() => {
  localStorage.clear();
  container = document.createElement('div');
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(() => {
  act(() => root.unmount());
  container.remove();
});

describe('TrendlineChart', () => {
  it('starts at the default range and remembers a picked one', () => {
    render();
    expect(startDate()).toBe('2025-10-16');
    expect(lineCount()).toBeGreaterThan(0);

    // The fixture holds two years, which makes 2年 the same as 全部.
    expect(button('2年')).toBeNull();
    click(button('半年'));
    expect(startDate()).toBe('2026-04-16');
    expect(JSON.parse(localStorage.getItem(RANGE) as string)).toEqual({ preset: '6m' });

    act(() => {
      const input = container.querySelector('input[type="date"]') as HTMLInputElement;
      // React tracks the value it set; go through the native setter so it sees a change.
      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set?.call(
        input,
        '2025-03-01',
      );
      input.dispatchEvent(new Event('input', { bubbles: true }));
    });
    expect(startDate()).toBe('2025-03-03');
    expect(JSON.parse(localStorage.getItem(RANGE) as string)).toEqual({ from: '2025-03-01' });
  });

  it('keeps the automatic lines until a line is changed, then keeps the drawing', () => {
    render();
    const auto = lineCount();
    expect(button('還原自動線')).toBeNull();
    expect(button('刪除這條線')).toBeNull();

    const line = container.querySelector('[data-line-id]') as SVGLineElement;
    press(line, Number(line.getAttribute('x1')), Number(line.getAttribute('y1')));
    click(button('刪除這條線'));

    expect(lineCount()).toBe(auto - 1);
    expect(stored().lines).toHaveLength(auto - 1);
    expect(text()).toContain('調整過的版本');

    click(button('復原'));
    expect(lineCount()).toBe(auto);
    expect(stored()).toBeNull(); // back to the automatic lines, which aren't stored
  });

  it('draws a line of the kind picked from the menu, and turns it into the other kind', () => {
    render();
    click(button('清除全部'));
    expect(lineCount()).toBe(0);
    expect(text()).toContain('壓力：目前沒有有效的線');

    click(button('畫線'));
    click(menuItem('壓力線'));
    expect(button('取消畫壓力線')).not.toBeNull();
    const surface = container.querySelector('[data-testid="trendline-draw-surface"]') as Element;
    // From the 100th bar to the newest, well under the price — a resistance all the same, since
    // that is what was asked for.
    const bars = Number(surface.getAttribute('width')) / 10; // the plot is 10 px a bar
    press(surface, 5 + 10 * 100, 300);
    press(surface, 5 + 10 * (bars - 1), 280);

    expect(lineCount()).toBe(1);
    expect(stored().lines).toHaveLength(1);
    expect(stored().lines[0]).toMatchObject({ toLatest: true, side: 'resistance' });
    expect(text()).toMatch(/壓力 ≈ [\d,]+（連 \d+ 點/);
    expect(text()).toContain('支撐：目前沒有有效的線');
    expect(button('刪除這條線')).not.toBeNull(); // the new line is selected

    click(button('改為支撐線'));
    expect(stored().lines[0].side).toBe('support');
    expect(text()).toMatch(/支撐 ≈ [\d,]+（連 \d+ 點/);
    expect(button('改為壓力線')).not.toBeNull();
  });

  it('opens with the stored drawing, and restores the automatic lines on request', () => {
    render();
    const auto = lineCount();
    localStorage.setItem(
      DRAWINGS,
      JSON.stringify({
        version: 1,
        lines: [
          {
            id: 'mine',
            from: { date: '2026-08-25', price: 40000 },
            to: { date: '2026-10-16', price: 42000 },
            toLatest: true,
          },
        ],
      }),
    );
    act(() => root.unmount());
    root = createRoot(container);
    render();
    expect(lineCount()).toBe(1);
    expect(text()).toContain('支撐 ≈ 42,000');

    // The tooltip lists the line on the days it covers, with its value that day.
    const last = (mocks.option?.xAxis as { data: string[] }).data.length - 1; // the shown year
    expect(tooltip(last)).toContain('2026-10-16');
    expect(tooltip(last)).toContain('支撐線');
    expect(tooltip(last)).toContain('42,000');
    expect(tooltip(last - 200)).not.toContain('支撐線');

    click(button('還原自動線'));
    expect(lineCount()).toBe(auto);
    expect(localStorage.getItem(DRAWINGS)).toBeNull();
  });
});

import { act, useImperativeHandle, type Ref } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import taiex from '../utils/__fixtures__/taiex-2024-2026.json';
import { TW_VOLUME } from '../utils/trendlineChartOption';

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
          const x = (option.xAxis as { data: string[] }[])[0];
          const y = (option.yAxis as { min: number; max: number }[])[0];
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

  it('puts a volume pane under a stock’s prices, and none under the index', () => {
    render();
    expect(mocks.option?.grid).toHaveLength(1); // the TAIEX: no unit, no volume

    const stock = {
      ...data,
      code: '2330',
      bars: data.bars.map((b, i) => ({ ...b, volume: 1_000_000 + i })),
    };
    act(() => root.render(<TrendlineChart data={stock} defaultRange="1y" volume={TW_VOLUME} />));
    expect(mocks.option?.grid).toHaveLength(2);
    const chartBox = container.querySelector('[data-testid="chart"]')?.parentElement?.parentElement;
    expect(getComputedStyle(chartBox as Element).height).toBe('442px');
    // The overlay still measures the price pane, so the lines are still drawn over it.
    expect(lineCount()).toBeGreaterThan(0);
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
    const last = (mocks.option?.xAxis as { data: string[] }[])[0].data.length - 1; // the shown year
    expect(tooltip(last)).toContain('2026-10-16');
    expect(tooltip(last)).toContain('支撐線');
    expect(tooltip(last)).toContain('42,000');
    expect(tooltip(last - 200)).not.toContain('支撐線');

    click(button('還原自動線'));
    expect(lineCount()).toBe(auto);
    expect(localStorage.getItem(DRAWINGS)).toBeNull();
  });
});

describe('TrendlineChart — choices, selection and drawing', () => {
  const firstLine = () => container.querySelector('[data-line-id]') as SVGLineElement;
  /** Selects a line the way a keyboard user does: focusing it. */
  const select = (line: Element = firstLine()) => act(() => (line as SVGElement).focus());
  const isSelected = () => button('刪除這條線') !== null;
  const escape = (el: Element = container.firstElementChild!) =>
    act(() => {
      el.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    });
  const startDrawing = (kind: '支撐線' | '壓力線') => {
    // A real click moves the focus to the button (jsdom's does not); the menu gives it back there.
    act(() => button('畫線')!.focus());
    click(button('畫線'));
    click(menuItem(kind));
  };
  const caption = () => container.querySelector('.MuiTypography-caption')?.textContent ?? '';
  const pressedPreset = () =>
    [...container.querySelectorAll('[aria-label="圖表區間"] button')]
      .filter((b) => b.getAttribute('aria-pressed') === 'true')
      .map((b) => b.textContent);
  const typeStartDate = (value: string) =>
    act(() => {
      const input = container.querySelector('input[type="date"]') as HTMLInputElement;
      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set?.call(input, value);
      input.dispatchEvent(new Event('input', { bubbles: true }));
    });
  const storeDrawing = (...lines: object[]) =>
    localStorage.setItem(DRAWINGS, JSON.stringify({ version: 1, lines }));
  const mine = {
    id: 'mine',
    from: { date: '2026-08-25', price: 40000 },
    to: { date: '2026-10-16', price: 42000 },
    toLatest: true,
  };

  it('opens at the range stored for this code', () => {
    localStorage.setItem(RANGE, JSON.stringify({ preset: '6m' }));
    render();
    expect(startDate()).toBe('2026-04-16');
    expect(pressedPreset()).toEqual(['半年']);
  });

  it('a stored range no longer offered shows as 全部; a start date of its own shows no range', () => {
    localStorage.setItem(RANGE, JSON.stringify({ preset: '2y' }));
    render();
    expect(pressedPreset()).toEqual(['全部']);

    act(() => root.unmount());
    root = createRoot(container);
    localStorage.setItem(RANGE, JSON.stringify({ from: '2025-03-01' }));
    render();
    expect(pressedPreset()).toEqual([]);
  });

  it('another code is another chart: its own range, nothing selected', () => {
    render();
    click(button('半年'));
    select();
    expect(isSelected()).toBe(true);

    act(() => root.render(<TrendlineChart data={{ ...data, code: '2330' }} defaultRange="1y" />));
    expect(startDate()).toBe('2025-10-16');
    expect(isSelected()).toBe(false);
  });

  it('the start date box takes the history’s first day to the latest start, and ignores a half-typed date', () => {
    render();
    const input = container.querySelector('input[type="date"]') as HTMLInputElement;
    expect(input.min).toBe('2024-10-16');
    expect(input.max).not.toBe('');
    typeStartDate('');
    expect(startDate()).toBe('2025-10-16');
    expect(localStorage.getItem(RANGE)).toBeNull();
  });

  it('says what to do next: click a line, drag it, or where to draw', () => {
    render();
    expect(caption()).toBe('點一下線就能拖曳或刪除，按「畫線」可以自己畫。');
    select();
    expect(caption()).toBe('拖曳圓點調整端點、拖曳線段整條移動；鍵盤可用方向鍵微調、Delete 刪除。');
    startDrawing('壓力線');
    expect(caption()).toMatch(/^畫壓力線：在圖上點起點和終點/);
  });

  it('a level chip gives the distance from the close', () => {
    render();
    expect(text()).toMatch(/(支撐|壓力) ≈ [\d,.]+（連 \d+ 點，收盤距離 [-+]?[\d.]+%）/);
  });

  it('nothing to undo, restore or say about a drawing until something is changed', () => {
    render();
    expect(button('復原')).toBeNull();
    expect(button('還原自動線')).toBeNull();
    expect(text()).not.toContain('調整過的版本');
    click(button('清除全部'));
    expect(button('清除全部')).toBeNull(); // nothing left to clear
    expect(button('復原')).not.toBeNull();
  });

  it('Esc stops drawing first, and only then lets go of the selected line', () => {
    render();
    select();
    escape(firstLine());
    expect(isSelected()).toBe(false);

    startDrawing('支撐線');
    escape();
    expect(button('畫線')).not.toBeNull();
    expect(button('取消畫支撐線')).toBeNull();
  });

  it('picking a kind of line closes the menu, lets go of the line, and its button stops drawing', () => {
    render();
    select();
    startDrawing('支撐線');
    expect(isSelected()).toBe(false);
    click(button('取消畫支撐線'));
    expect(button('畫線')?.getAttribute('aria-expanded')).toBe('false');
    expect(isSelected()).toBe(false);
  });

  it('while drawing, a line focused from the keyboard gets no buttons of its own', () => {
    render();
    startDrawing('壓力線');
    select();
    expect(isSelected()).toBe(false);
  });

  it('pressing the chart away from a line lets go of it', () => {
    render();
    select();
    const chartBox =
      container.querySelector('[data-testid="chart"]')!.parentElement!.parentElement!;
    act(() => {
      chartBox.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true, button: 0 }));
    });
    expect(isSelected()).toBe(false);
  });

  it('pressing the chart while drawing keeps the line focused from the keyboard', () => {
    render();
    startDrawing('壓力線');
    select();
    const chartBox =
      container.querySelector('[data-testid="chart"]')!.parentElement!.parentElement!;
    act(() => {
      chartBox.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true, button: 0 }));
    });
    click(button('取消畫壓力線'));
    expect(isSelected()).toBe(true);
  });

  it('a new range lets go of the line and stops drawing', () => {
    // A line of one's own keeps its id over both ranges, so it would still be found selected.
    storeDrawing(mine);
    render();
    select();
    click(button('半年'));
    expect(isSelected()).toBe(false);

    startDrawing('支撐線');
    click(button('1年'));
    expect(button('取消畫支撐線')).toBeNull();
  });

  it('undo lets go of the selected line', () => {
    render();
    select();
    click(button('改為'));
    expect(isSelected()).toBe(true);
    click(button('復原'));
    expect(isSelected()).toBe(false);
  });

  it('還原自動線 also stops drawing', () => {
    storeDrawing(mine);
    render();
    startDrawing('壓力線');
    click(button('還原自動線'));
    expect(button('取消畫壓力線')).toBeNull();
  });

  it('keeps twenty steps to undo', () => {
    render();
    select();
    for (let i = 0; i < 21; i++) click(button('改為'));
    for (let i = 0; i < 20; i++) click(button('復原'));
    expect(button('復原')).toBeNull();
    // The oldest step is gone: what is left is the first change, not the automatic lines.
    expect(text()).toContain('調整過的版本');
  });

  it('a change lets go of a stored line wholly before the history; one before the start date stays', () => {
    const gone = {
      id: 'gone',
      from: { date: '2020-01-02', price: 10000 },
      to: { date: '2020-03-02', price: 11000 },
      toLatest: false,
    };
    const earlier = {
      id: 'earlier',
      from: { date: '2025-01-02', price: 20000 },
      to: { date: '2025-03-03', price: 21000 },
      toLatest: false,
    };
    storeDrawing(gone, earlier, mine);
    render();
    // Only the line inside the shown year is drawn — and counted as drawn.
    expect(lineCount()).toBe(1);
    expect(container.querySelector('[role="img"]')?.getAttribute('aria-label')).toContain(
      '共 1 條趨勢線',
    );

    select();
    click(button('改為'));
    expect(stored().lines.map((l: { id: string }) => l.id)).toEqual(['earlier', 'mine']);
  });

  it('says how far the data goes, and the close', () => {
    render();
    expect(text()).toContain('2025-10-16 起，資料至 2026-10-16，收盤 47,458。');
  });
});

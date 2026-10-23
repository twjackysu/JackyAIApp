import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { NetWorthChart } from './NetWorthChart';
import { MASK, PrivacyContext, type PrivacyMode } from './privacy';

import type { NetWorthDataPoint } from '@/apis/portfolioApis/types';

const mocks = vi.hoisted(() => ({
  query: vi.fn(),
  chart: vi.fn(),
}));

vi.mock('@/apis/portfolioApis', () => ({
  useGetNetWorthHistoryQuery: (...args: unknown[]) => mocks.query(...args),
}));
// A canvas can't run under jsdom: the stand-in records what the chart is given.
vi.mock('@/charts/ReactECharts', () => ({
  default: (props: unknown) => {
    mocks.chart(props);
    return null;
  },
}));

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

interface Series {
  name: string;
  stack: string;
  data: number[];
  lineStyle: { color: string };
}
interface Option {
  tooltip: { formatter: (params: unknown) => string };
  legend: { selected: Record<string, boolean> };
  grid: { left: number };
  xAxis: { data: string[] };
  yAxis: { axisLabel: { formatter: (v: number) => string } };
  series: Series[];
}

const day = (
  date: string,
  totalTWD: number,
  groups: Record<string, number>,
): NetWorthDataPoint => ({
  date,
  totalTWD,
  groups: Object.entries(groups).map(([key, valueTWD]) => ({ key, valueTWD })),
});

const points = [
  day('2026-10-01', 2_500_000, {
    TwStock: 1_000_000,
    Cash: 500_000,
    Property: 2_000_000,
    Liability: -1_000_000,
  }),
  day('2026-10-02', 2_600_000, {
    TwStock: 1_100_000,
    Cash: 500_000,
    Property: 2_000_000,
    Liability: -1_000_000,
    Fund: 0,
  }),
];

function answer(data: NetWorthDataPoint[] | undefined, extra: Record<string, unknown> = {}) {
  mocks.query.mockReturnValue({
    data: data ? { data } : undefined,
    isLoading: false,
    isFetching: false,
    ...extra,
  });
}

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  container = document.createElement('div');
  document.body.appendChild(container);
  root = createRoot(container);
  answer(points);
});

afterEach(() => {
  act(() => root.unmount());
  container.remove();
  vi.clearAllMocks();
});

function render(mode: PrivacyMode = 'full') {
  act(() =>
    root.render(
      <PrivacyContext.Provider
        value={{
          mode,
          cycle: () => {},
          value: (s) => (mode === 'full' ? s : MASK),
          change: (s) => (mode === 'hidden' ? MASK : s),
          showRatio: mode !== 'changes',
        }}
      >
        <NetWorthChart />
      </PrivacyContext.Provider>,
    ),
  );
}

const option = () => (mocks.chart.mock.lastCall![0] as { option: Option }).option;
const chartProps = () => mocks.chart.mock.lastCall![0] as { notMerge: boolean; style: object };
const seriesNamed = (name: string) => option().series.find((s) => s.name === name);
const button = (label: string) =>
  [...container.querySelectorAll('button')].find(
    (b) => b.textContent === label,
  ) as HTMLButtonElement;
const click = (el: Element) => act(() => (el as HTMLElement).click());
const legendItems = () =>
  [...container.querySelectorAll('.MuiTypography-caption')].map((t) => t.textContent);
const legendItem = (name: string) =>
  [...container.querySelectorAll('.MuiTypography-caption')].find((t) => t.textContent === name)!
    .parentElement as HTMLElement;
const tooltip = (index: number, names: string[]) =>
  option().tooltip.formatter(
    names.map((seriesName) => ({
      axisValue: option().xAxis.data[index],
      seriesName,
      value: seriesNamed(seriesName)!.data[index],
      dataIndex: index,
    })),
  );

describe('NetWorthChart — loading and empty', () => {
  it('a spinner while loading, and while a combination not cached yet is fetched', () => {
    answer(undefined, { isLoading: true, isFetching: true });
    render();
    expect(container.querySelector('.MuiCircularProgress-root')).not.toBeNull();
    expect(mocks.chart).not.toHaveBeenCalled();

    answer([], { isFetching: true });
    render();
    expect(container.querySelector('.MuiCircularProgress-root')).not.toBeNull();
    expect(container.textContent).not.toContain('資料不足');
  });

  it('says there is not enough data when the history is empty', () => {
    answer([]);
    render();
    expect(container.textContent).toBe('資料不足，更新持倉後即可看到圖表');
  });

  it('a thin bar over the chart while a cached view is revalidated', () => {
    render();
    expect(container.querySelector('.MuiLinearProgress-root')).toBeNull();
    answer(points, { isFetching: true });
    render();
    expect(container.querySelector('.MuiLinearProgress-root')).not.toBeNull();
  });
});

describe('NetWorthChart — view and range', () => {
  it('asks for 90 days by asset type at first; each button asks for its view and range', () => {
    render();
    expect(mocks.query).toHaveBeenLastCalledWith({ days: 90, groupBy: 'assetType' });
    expect(button('資產類型').className).toContain('MuiButton-contained');
    expect(button('3M').className).toContain('MuiButton-contained');

    click(button('產業'));
    expect(mocks.query).toHaveBeenLastCalledWith({ days: 90, groupBy: 'category' });
    expect(button('產業').className).toContain('MuiButton-contained');
    expect(button('資產類型').className).toContain('MuiButton-outlined');
    click(button('子類別'));
    expect(mocks.query).toHaveBeenLastCalledWith({ days: 90, groupBy: 'subCategory' });
    click(button('個股明細'));
    expect(mocks.query).toHaveBeenLastCalledWith({ days: 90, groupBy: 'holding' });

    click(button('6M'));
    expect(mocks.query).toHaveBeenLastCalledWith({ days: 180, groupBy: 'holding' });
  });

  it('動產/不動產 asks for asset types and folds everything but real estate and debt into 動產', () => {
    render();
    click(button('動產/不動產'));
    expect(mocks.query).toHaveBeenLastCalledWith({ days: 90, groupBy: 'assetType' });
    expect(option().series.map((s) => [s.name, s.data])).toEqual([
      ['動產', [1_500_000, 1_600_000]],
      ['不動產', [2_000_000, 2_000_000]],
      ['負債', [-1_000_000, -1_000_000]],
    ]);
  });

  it('a fold with nothing in it is left out', () => {
    answer([day('2026-10-01', 500_000, { Cash: 500_000 })]);
    render();
    click(button('動產/不動產'));
    expect(option().series.map((s) => s.name)).toEqual(['動產']);
  });
});

describe('NetWorthChart — series', () => {
  it('one series per group seen in the range, named in Chinese; one that is zero throughout is left out', () => {
    render();
    expect(option().xAxis.data).toEqual(['2026-10-01', '2026-10-02']);
    expect(option().series.map((s) => [s.name, s.data])).toEqual([
      ['台股', [1_000_000, 1_100_000]],
      ['現金', [500_000, 500_000]],
      ['不動產', [2_000_000, 2_000_000]],
      ['負債', [-1_000_000, -1_000_000]],
    ]);
  });

  it('a group missing on a day counts as 0 that day; an unknown key is shown as it is', () => {
    answer([
      day('2026-10-01', 100, { 半導體設備: 100 }),
      day('2026-10-02', 300, { 半導體設備: 100, Semiconductor: 200 }),
    ]);
    render();
    expect(option().series.map((s) => [s.name, s.data])).toEqual([
      ['半導體設備', [100, 100]],
      ['半導體', [0, 200]],
    ]);
  });

  it('debt gets a stack of its own, so it dips from 0 instead of riding on the assets', () => {
    render();
    expect(seriesNamed('負債')!.stack).toBe('neg-Liability');
    expect(seriesNamed('台股')!.stack).toBe('total');
    expect(seriesNamed('不動產')!.stack).toBe('total');
  });

  it('a known key has its fixed colour; any other the same colour from its name every time', () => {
    answer([
      day('2026-10-01', 400, {
        TwStock: 100,
        Semiconductor: 100,
        未分類: 100,
        半導體設備: 100,
        AI伺服器: 100,
      }),
    ]);
    render();
    const colour = (name: string) => seriesNamed(name)!.lineStyle.color;
    expect(colour('台股')).toBe('#3B82F6');
    expect(colour('半導體')).toBe('#3B82F6');
    expect(colour('未分類')).toBe('#9CA3AF');
    expect(colour('半導體設備')).toBe('#71d58f');
    expect(colour('AI伺服器')).toBe('#d34acc');
  });

  it('gives free-form keys (sub-categories, holdings) colours of their own, the same every time', () => {
    // Eighty keys reach every step of the palette: its 20 base colours and later cycles, every hue
    // range, and the lightness limits. The colours are pinned as the palette made them.
    const keys = Array.from({ length: 80 }, (_, i) => `子類別${i}`);
    answer([
      { date: '2026-10-01', totalTWD: 80, groups: keys.map((key) => ({ key, valueTWD: 1 })) },
    ]);
    render();
    expect(keys.map((k) => seriesNamed(k)!.lineStyle.color).join(' ')).toBe(
      '#a15dd0 #7ce04e #66ff2e #6bd05d #5fd05d #a8d05d #adf03d #d0cd5d #d0ca5d #5dd064 #d1a161 #d19b61 #b93936 #c12f3e #0ee2e2 #00f0f0 #6836b9 #6036b9 #2f97c1 #14a9dc #91d571 #c664e4 #dd47ff #d271d5 #d571cf #9c71d5 #9c54f2 #7771d5 #7571d5 #d571c7 #dace81 #dfc67c #657bf6 #5c74ff #da81c6 #da81cb #8a7ddf #7769f2 #da9c81 #da9781 #ff1966 #cb4d65 #cb4d58 #cb4da8 #ee2ab2 #bf4dcb #bd4dcb #cb4d4e #cb4d56 #df90bd #bb3af3 #c02eff #d0665d #d0605d #d458d6 #e63fee #bed05d #c5d05d #694ee0 #5e2eff #e2d29c #e2a59c #f69a89 #e29cad #e29caf #e2d79c #e2d39c #d48a6d #da7a67 #4dc8f4 #c5bb3a #c5b33a #cd334c #ea153f #3ac53a #41c53a #d927d3 #ff00ea #c53aa5 #c53a97',
    );
  });

  it('a key that lands on one of the 20 base colours takes it as it is', () => {
    answer([day('2026-10-01', 1, { 子類別130: 1 })]);
    render();
    expect(seriesNamed('子類別130')!.lineStyle.color).toBe('#f14b81');
  });

  it('keeps the chart from patching the previous view onto the next one', () => {
    render();
    expect(chartProps().notMerge).toBe(true);
    expect(chartProps().style).toEqual({ height: 280 });
  });
});

describe('NetWorthChart — privacy', () => {
  it('full: amounts on the axis and in the tooltip, with the day’s total', () => {
    render('full');
    expect(option().grid.left).toBe(70);
    expect(option().yAxis.axisLabel.formatter(2_500_000)).toBe('2.5M');
    expect(tooltip(0, ['台股', '負債'])).toBe(
      '<b>2026-10-01</b><br/><b>總資產：NT$2.5M</b><br/>台股：NT$1.0M<br/>負債：NT$-1.0M',
    );
  });

  it('只看損益: no amounts and no shares, the total masked', () => {
    render('changes');
    expect(option().grid.left).toBe(24);
    expect(option().yAxis.axisLabel.formatter(2_500_000)).toBe('');
    expect(tooltip(0, ['台股'])).toBe(
      `<b>2026-10-01</b><br/><b>總資產：${MASK}</b><br/>台股：${MASK}`,
    );
  });

  it('隱藏金額: each group as a share of the day’s total, no total line', () => {
    render('hidden');
    expect(option().grid.left).toBe(24);
    expect(tooltip(1, ['台股', '現金'])).toBe('<b>2026-10-02</b><br/>台股：42.3%<br/>現金：19.2%');
  });

  it('隱藏金額 on a day whose total is not positive shows 0%', () => {
    answer([day('2026-10-01', 0, { Cash: 100, Liability: -100 })]);
    render('hidden');
    expect(tooltip(0, ['現金'])).toBe('<b>2026-10-01</b><br/>現金：0.0%');
  });
});

describe('NetWorthChart — legend', () => {
  it('lists the series under the chart; a click hides one, another click shows it again', () => {
    render();
    expect(legendItems()).toEqual(['台股', '現金', '不動產', '負債']);
    expect(option().legend.selected).toEqual({ 台股: true, 現金: true, 不動產: true, 負債: true });

    click(legendItem('現金'));
    expect(option().legend.selected.現金).toBe(false);
    expect(getComputedStyle(legendItem('現金')).opacity).toBe('0.4');
    expect(
      getComputedStyle(legendItem('現金').querySelector('.MuiTypography-caption')!).textDecoration,
    ).toBe('line-through');

    const dot = () => getComputedStyle(legendItem('現金').firstElementChild!).backgroundColor;
    expect(dot()).toBe('rgba(0, 0, 0, 0.38)'); // greyed out

    click(legendItem('現金'));
    expect(dot()).toBe('rgb(167, 139, 250)'); // its own colour again, #A78BFA
    expect(option().legend.selected.現金).toBe(true);
    expect(getComputedStyle(legendItem('現金')).opacity).toBe('1');
  });
});

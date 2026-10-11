import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { MarketSeasonalChart } from './MarketSeasonalChart';

import type { MarketIndexHistoryData, MarketIndexPoint } from '@/apis/financeApis/types';
import type { ECOption } from '@/charts/echarts';

const mocks = vi.hoisted(() => ({
  query: vi.fn(),
  charts: [] as { option: ECOption }[],
}));

vi.mock('@/apis/financeApis', () => ({
  useGetMarketIndexHistoryQuery: (...args: unknown[]) => mocks.query(...args),
}));
// A canvas can't run under jsdom; the option handed to ECharts is what the component decides.
vi.mock('@/charts/ReactECharts', () => ({
  default: (props: { option: ECOption }) => {
    mocks.charts.push(props);
    return <div data-testid="chart" />;
  },
}));

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

const SUBJECT = '加權指數（價格指數，不含息）';
const DISCLAIMER = '過去走勢僅供參考，不代表未來表現，不構成投資建議。';
const UNAVAILABLE = '資料暫時無法取得';
const ASOF = '2026-10-08';

/** Weekday closes for `years`, Jan 2 through Dec 31 (2026 stops at ASOF). */
function makeHistory(years = [2021, 2022, 2023, 2024, 2025, 2026]): MarketIndexHistoryData {
  const points: MarketIndexPoint[] = [];
  for (const year of years) {
    let n = 0;
    for (let t = Date.UTC(year, 0, 2); t <= Date.UTC(year, 11, 31); t += 86_400_000) {
      const d = new Date(t);
      if (d.getUTCDay() === 0 || d.getUTCDay() === 6) continue;
      const date = d.toISOString().slice(0, 10);
      if (date > ASOF) break;
      points.push({ date, close: 10_000 + (year - 2000) * 400 + n++ * 2 });
    }
  }
  return {
    symbol: '^TWII',
    name: SUBJECT,
    source: 'Yahoo Finance',
    asOf: ASOF,
    points,
  };
}

function respondWith(state: { data?: MarketIndexHistoryData; isError?: boolean }) {
  const res = state.data ? { data: state.data } : undefined;
  mocks.query.mockReturnValue({ data: res, isError: state.isError ?? false });
}

/** Sets a native <select>'s value through its own setter and fires the `change` React listens to. */
function choose(select: HTMLSelectElement, value: number) {
  const setter = Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, 'value')!.set!;
  act(() => {
    setter.call(select, String(value));
    select.dispatchEvent(new Event('change', { bubbles: true }));
  });
}

describe('MarketSeasonalChart', () => {
  let container: HTMLDivElement;
  let root: Root;

  const render = () => act(() => root.render(<MarketSeasonalChart />));
  const select = (label: string) =>
    container.querySelector<HTMLSelectElement>(`select[aria-label="${label}"]`)!;
  const lastOption = () => mocks.charts[mocks.charts.length - 1].option;
  const seriesNames = () => ((lastOption().series ?? []) as { name: string }[]).map((s) => s.name);

  beforeEach(() => {
    mocks.query.mockReset();
    mocks.charts.length = 0;
    container = document.createElement('div');
    document.body.appendChild(container);
    root = createRoot(container);
  });

  afterEach(() => {
    act(() => root.unmount());
    container.remove();
  });

  it('while loading: skeleton, no chart, and the standing texts are already there', () => {
    respondWith({});
    render();

    expect(container.querySelector('[data-testid="chart"]')).toBeNull();
    expect(container.querySelector('.MuiSkeleton-root')).not.toBeNull();
    expect(container.textContent).toContain(SUBJECT);
    expect(container.textContent).toContain(DISCLAIMER);
    expect(container.textContent).toContain('資料至');
    expect(container.textContent).not.toContain(UNAVAILABLE);
  });

  it('on a request error: says the data is unavailable — no chart, no controls', () => {
    respondWith({ isError: true });
    render();

    expect(container.textContent).toContain(UNAVAILABLE);
    expect(container.querySelector('[data-testid="chart"]')).toBeNull();
    expect(container.querySelector('select')).toBeNull();
    expect(container.textContent).toContain(SUBJECT);
    expect(container.textContent).toContain(DISCLAIMER);
    expect(container.textContent).toContain('資料至');
  });

  it('treats a payload with no points or an unreadable asOf as unavailable, never an empty chart', () => {
    respondWith({ data: { ...makeHistory(), points: [] } });
    render();
    expect(container.textContent).toContain(UNAVAILABLE);
    expect(container.querySelector('[data-testid="chart"]')).toBeNull();

    respondWith({ data: { ...makeHistory(), asOf: 'tomorrow' } });
    render();
    expect(container.textContent).toContain(UNAVAILABLE);
    expect(container.querySelector('[data-testid="chart"]')).toBeNull();
  });

  it('draws five years — the data year plus four — with the standing texts and the asOf date', () => {
    respondWith({ data: makeHistory() });
    render();

    expect(container.querySelector('[data-testid="chart"]')).not.toBeNull();
    expect(seriesNames()).toEqual(['2022', '2023', '2024', '2025', '2026']);
    expect(container.textContent).toContain(SUBJECT);
    expect(container.textContent).toContain(DISCLAIMER);
    expect(container.textContent).toContain('資料至 2026-10-08');
    expect(container.textContent).not.toContain(UNAVAILABLE);
  });

  it('gives every year its own colour and draws the current year heavier', () => {
    respondWith({ data: makeHistory() });
    render();

    const series = lastOption().series as {
      name: string;
      lineStyle: { width: number; color: string };
    }[];
    expect(new Set(series.map((s) => s.lineStyle.color)).size).toBe(5);
    const widths = Object.fromEntries(series.map((s) => [s.name, s.lineStyle.width]));
    expect(widths['2026']).toBeGreaterThan(widths['2025']);
    expect(widths['2025']).toBe(widths['2022']);
  });

  it('defaults to January 1 through the latest data day', () => {
    respondWith({ data: makeHistory() });
    render();

    expect(select('起始月').value).toBe('1');
    expect(select('起始日').value).toBe('1');
    expect(select('結束月').value).toBe('10');
    expect(select('結束日').value).toBe('8');
    const axis = (lastOption().xAxis as { data: string[] }).data;
    expect(axis[0]).toBe('01-01');
    expect(axis[axis.length - 1]).toBe('10-08');
  });

  it('recomputes all lines when the range changes — without asking the API for anything', () => {
    const history = makeHistory();
    respondWith({ data: history });
    render();
    const callsBefore = mocks.query.mock.calls.length;

    choose(select('結束月'), 6);
    choose(select('結束日'), 30);

    const axis = (lastOption().xAxis as { data: string[] }).data;
    expect(axis[axis.length - 1]).toBe('06-30');
    expect(seriesNames()).toEqual(['2022', '2023', '2024', '2025', '2026']);
    // The range lives below the data hook: picking dates re-renders the chart, not the query, and
    // the only question ever asked is the parameterless one — so RTK Query's cached payload serves
    // every range the user tries.
    expect(mocks.query.mock.calls.length).toBe(callsBefore);
    expect(mocks.query.mock.calls.every((args) => args.length === 0)).toBe(true);
  });

  it('blocks a day that does not exist (2/30) and explains why instead of drawing', () => {
    respondWith({ data: makeHistory() });
    render();

    choose(select('結束月'), 2);
    choose(select('結束日'), 30);

    expect(container.textContent).toContain('結束日期不存在：2 月沒有 30 日');
    expect(container.querySelector('[data-testid="chart"]')).toBeNull();
    expect(container.querySelector('[role="alert"]')).not.toBeNull();
  });

  it('blocks a start later than the end', () => {
    respondWith({ data: makeHistory() });
    render();

    choose(select('起始月'), 11); // end is 10/8

    expect(container.textContent).toContain('起始日不可晚於結束日');
    expect(container.querySelector('[data-testid="chart"]')).toBeNull();
  });

  it('accepts Feb 29 as a date', () => {
    respondWith({ data: makeHistory() });
    render();

    choose(select('起始月'), 2);
    choose(select('起始日'), 29);
    choose(select('結束月'), 3);
    choose(select('結束日'), 5);

    expect(container.querySelector('[data-testid="chart"]')).not.toBeNull();
    expect((lastOption().xAxis as { data: string[] }).data).toContain('02-29');
  });

  it('leaves out a year with no trading day in the range and says so', () => {
    respondWith({ data: makeHistory([2022, 2024, 2025, 2026]) }); // no 2023 at all
    render();

    expect(seriesNames()).toEqual(['2022', '2024', '2025', '2026']);
    expect(container.textContent).toContain('2023 年在此區間沒有交易日，未繪製。');
  });

  it('says so when no year has a trading day in the range', () => {
    respondWith({ data: makeHistory([2026]) });
    render();

    choose(select('起始月'), 10); // 10/9 onwards: after the latest data
    choose(select('起始日'), 9);
    choose(select('結束月'), 12);
    choose(select('結束日'), 31);

    expect(container.textContent).toContain('此區間沒有任何交易日資料');
    expect(container.querySelector('[data-testid="chart"]')).toBeNull();
  });

  it('"年初至今" puts the default range back', () => {
    respondWith({ data: makeHistory() });
    render();
    const buttons = () => [...container.querySelectorAll('button')].map((b) => b.textContent);
    expect(buttons()).not.toContain('年初至今');

    choose(select('結束月'), 6);
    expect(buttons()).toContain('年初至今');

    act(() =>
      [...container.querySelectorAll('button')].find((b) => b.textContent === '年初至今')!.click(),
    );
    expect(select('結束月').value).toBe('10');
    expect(buttons()).not.toContain('年初至今');
  });

  it('never shows forecast wording', () => {
    respondWith({ data: makeHistory() });
    render();

    expect(container.textContent).not.toMatch(/預期|預測|看漲|看跌|平均上漲|平均下跌|可能上漲/);
  });
});

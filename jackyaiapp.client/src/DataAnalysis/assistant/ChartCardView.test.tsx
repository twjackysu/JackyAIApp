import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import ChartCardView from './ChartCardView';

const mocks = vi.hoisted(() => ({ renderViz: vi.fn(), dialog: vi.fn() }));

// The page's renderers and the dashboard dialog have their own tests.
vi.mock('../viz/renderViz', () => ({
  renderViz: (...args: unknown[]) => {
    mocks.renderViz(...args);
    return '[viz]';
  },
}));
vi.mock('../viz/AddToDashboardDialog', () => ({
  AddToDashboardDialog: (p: { open: boolean; vizType: string }) => {
    mocks.dialog(p);
    return p.open ? `[AddToDashboard ${p.vizType}]` : null;
  },
}));

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
  vi.clearAllMocks();
});

const data = {
  viz: 'line',
  headers: ['month', 'revenue'],
  rows: [{ month: '2026-01', revenue: 100 }],
  groupHints: ['month'],
  temporalHints: ['month'],
  plan: { viewBy: [{ fieldName: 'OrderDate' }], measures: [{ fieldName: 'Amount' }] },
};

const render = (d: Record<string, unknown>) =>
  act(() => root.render(<ChartCardView card={{ kind: 'chart', title: '營收趨勢', data: d }} />));

describe('ChartCardView', () => {
  it("draws the result with the page's renderer, its columns told apart by the plan", () => {
    render(data);
    expect(container.textContent).toContain('營收趨勢');
    const [viz, rows, headers, shape] = mocks.renderViz.mock.calls[0];
    expect([viz, rows, headers]).toEqual(['line', data.rows, data.headers]);
    expect(shape).toMatchObject({
      groupKeys: ['month'],
      measureKeys: ['revenue'],
      temporalKey: 'month',
    });
  });

  it('adds a result that carries its plan to a dashboard', () => {
    render(data);
    const add = [...container.querySelectorAll('button')].find(
      (b) => b.textContent === '加到 dashboard',
    )!;
    act(() => add.click());
    expect(container.textContent).toContain('[AddToDashboard line]');
  });

  it('offers no dashboard for a result without a plan', () => {
    render({ ...data, plan: undefined });
    expect([...container.querySelectorAll('button')].map((b) => b.textContent)).not.toContain(
      '加到 dashboard',
    );
  });

  it('draws it as another chart the data fits when the reader picks one, and adds that one', () => {
    render({
      ...data,
      viz: 'column',
      headers: ['region', 'revenue'],
      rows: [
        { region: '北區', revenue: 100 },
        { region: '南區', revenue: 80 },
      ],
      groupHints: ['region'],
      temporalHints: [],
    });
    const offered = [
      ...container.querySelectorAll<HTMLButtonElement>('[aria-label="換一種圖表"] button'),
    ];
    const labels = offered.map((b) => b.getAttribute('aria-label'));
    expect(labels).toContain('圓餅圖 (Pie)');
    // Only what fits: one category and one measure is no sankey.
    expect(labels).not.toContain('桑基圖 (Sankey)');
    expect(offered.find((b) => b.getAttribute('aria-pressed') === 'true')?.value).toBe('column');

    act(() => offered.find((b) => b.value === 'pie')!.click());
    expect(mocks.renderViz.mock.lastCall![0]).toBe('pie');
    const add = [...container.querySelectorAll('button')].find(
      (b) => b.textContent === '加到 dashboard',
    )!;
    act(() => add.click());
    expect(container.textContent).toContain('[AddToDashboard pie]');
  });

  it('draws nothing for data it cannot check', () => {
    render({ viz: 'radar' });
    expect(container.innerHTML).toBe('');
  });
});

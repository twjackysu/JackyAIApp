import { act, ComponentProps } from 'react';
import { createRoot, Root } from 'react-dom/client';
import { renderToStaticMarkup } from 'react-dom/server';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { SummarizeTab } from './SummarizeTab';

import type { SummarizeRequest } from '../../apis/dataAnalysisApis/types';
import type { FieldTransformDef, ViewByEntry } from '../types';

const mocks = vi.hoisted(() => ({
  viz: vi.fn(),
  vizMounts: { count: 0 },
  editors: vi.fn(),
}));

// The result view and the four manual editors have their own tests; here they only show that they
// were rendered, and with what.
vi.mock('../viz/VizSwitcher', async () => {
  const { useState } = await import('react');
  return {
    VizSwitcher: (props: Record<string, unknown>) => {
      const [mount] = useState(() => ++mocks.vizMounts.count);
      mocks.viz({ ...props, mount });
      return '[VizSwitcher]';
    },
  };
});
vi.mock('./FieldTransformEditor', () => ({
  FieldTransformEditor: (p: object) => (mocks.editors('transform', p), '[FieldTransformEditor]'),
}));
vi.mock('./ViewByEditor', () => ({
  ViewByEditor: (p: object) => (mocks.editors('viewBy', p), '[ViewByEditor]'),
}));
vi.mock('./MeasureEditor', () => ({
  MeasureEditor: (p: object) => (mocks.editors('measure', p), '[MeasureEditor]'),
}));
vi.mock('./FilterEditor', () => ({
  FilterEditor: (p: object) => (mocks.editors('filter', p), '[FilterEditor]'),
}));

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

const noop = () => {};

type Props = ComponentProps<typeof SummarizeTab>;

function baseProps(overrides: Partial<Props> = {}): Props {
  return {
    fieldTransforms: [],
    onTransformAdd: noop,
    onTransformUpdate: noop,
    onTransformRemove: noop,
    viewByEntries: [],
    onViewByAdd: noop,
    onViewByUpdate: noop,
    onViewByRemove: noop,
    measures: [],
    onMeasureAdd: noop,
    onMeasureRemove: noop,
    onMeasureUpdate: noop,
    summarizeFilters: [],
    onSummarizeFiltersChange: noop,
    onRunSummarize: noop,
    isLoading: false,
    error: null,
    summarizeRows: [],
    summarizeHeaders: [],
    onRunNlSummarize: noop,
    nlIsLoading: false,
    nlError: null,
    nlRows: [],
    nlResultVersion: 0,
    summarizeVersion: 0,
    summarizeLastRequest: null,
    canAddToDashboard: false,
    isLoggedIn: true,
    fields: [],
    ...overrides,
  };
}

function render(isLoggedIn: boolean) {
  return renderToStaticMarkup(<SummarizeTab {...baseProps({ isLoggedIn })} />);
}

describe('SummarizeTab AI mode data disclosure', () => {
  // AI is the default mode, so a plain render is the AI panel.
  it.each([
    ['logged in', true],
    ['logged out', false],
  ])('tells the user what is sent to OpenAI when %s', (_label, isLoggedIn) => {
    const html = render(isLoggedIn);
    expect(html).toContain('OpenAI');
    expect(html).toContain('欄位名稱');
  });

  it('shows the notice as visible helper text tied to the input, not in a tooltip', () => {
    const html = render(true);
    const helper = /<p[^>]*id="([^"]*-helper-text)"[^>]*>([^<]*OpenAI[^<]*)<\/p>/.exec(html);
    expect(helper).not.toBeNull();
    // aria-describedby on the textarea points at the helper text, so screen readers read it.
    expect(html).toMatch(new RegExp(`<textarea[^>]*aria-describedby="${helper![1]}"`));
  });
});

const dateTrunc: FieldTransformDef = {
  id: 7,
  fieldName: 'OrderDate',
  name: 'Order Month',
  type: 'DateTrunc',
  granularity: 'Month',
  bins: [],
  groups: [],
  fallback: '',
};
const stringGroups: FieldTransformDef = { ...dateTrunc, id: 8, type: 'StringGrouping' };

const nlPlan: SummarizeRequest = {
  viewBy: [
    { type: 'DateTrunc', fieldName: 'OrderDate', alias: 'Month', granularity: 'Month' },
    { type: 'Plain', fieldName: 'Region' },
  ],
  measures: [{ fieldName: 'Revenue', aggregation: 'Sum' }],
  filters: [{ fieldName: 'Status', values: ['Completed'] }],
};

describe('SummarizeTab', () => {
  let container: HTMLDivElement;
  let root: Root;
  let props: Props;

  beforeEach(() => {
    container = document.createElement('div');
    document.body.appendChild(container);
    root = createRoot(container);
    props = baseProps();
  });

  afterEach(() => {
    act(() => root.unmount());
    container.remove();
    vi.clearAllMocks();
    vi.useRealTimers();
  });

  async function mount(overrides: Partial<Props> = {}) {
    props = { ...props, ...overrides };
    await act(async () => {
      root.render(<SummarizeTab {...props} />);
    });
  }

  const text = () => container.textContent ?? '';
  const button = (label: string) =>
    Array.from(container.querySelectorAll('button')).find((b) =>
      b.textContent?.includes(label),
    ) as HTMLButtonElement;
  const chip = (label: string) =>
    Array.from(container.querySelectorAll('.MuiChip-root')).filter(
      (el) => el.textContent === label,
    ) as HTMLElement[];
  const textarea = () =>
    container.querySelector('textarea:not([aria-hidden])') as HTMLTextAreaElement;
  const click = async (el: Element | undefined) => {
    if (!el) throw new Error('nothing to click');
    await act(async () => {
      (el as HTMLElement).click();
    });
  };
  /** Lets a Collapse finish its transition, so its class says where it ended up. */
  const settle = () => act(() => new Promise((resolve) => setTimeout(resolve, 400)));
  const collapsed = (el: HTMLElement) => el.className.includes('MuiCollapse-hidden');
  const expanded = (el: HTMLElement) => el.className.includes('MuiCollapse-entered');
  const type = async (value: string) => {
    await act(async () => {
      const setValue = Object.getOwnPropertyDescriptor(
        HTMLTextAreaElement.prototype,
        'value',
      )!.set!;
      setValue.call(textarea(), value);
      textarea().dispatchEvent(new Event('input', { bubbles: true }));
    });
  };
  const keyDown = async (init: KeyboardEventInit) => {
    await act(async () => {
      textarea().dispatchEvent(new KeyboardEvent('keydown', { bubbles: true, ...init }));
    });
  };
  const toManual = () => click(container.querySelector('[aria-label="manual mode"]')!);
  const lastViz = () => mocks.viz.mock.lastCall?.[0] as Record<string, unknown> | undefined;

  describe('模式', () => {
    it('預設「問 AI」；手動的說明與編輯器要切過去才出現', async () => {
      await mount();
      expect(container.querySelector('[aria-label="ask AI"]')?.getAttribute('aria-pressed')).toBe(
        'true',
      );
      expect(text()).toContain('用一般文字描述你想看的內容');
      expect(text()).not.toContain('SummarizeAsync');
      expect(text()).not.toContain('[ViewByEditor]');

      await toManual();
      expect(text()).toContain('SummarizeAsync');
      expect(text()).not.toContain('用一般文字描述你想看的內容');
      for (const editor of [
        '[FieldTransformEditor]',
        '[ViewByEditor]',
        '[MeasureEditor]',
        '[FilterEditor]',
      ]) {
        expect(text()).toContain(editor);
      }
    });

    it('再按一次已選的模式不會取消選取', async () => {
      await mount();
      await click(container.querySelector('[aria-label="ask AI"]')!);
      expect(text()).toContain('用一般文字描述你想看的內容');
    });
  });

  describe('手動', () => {
    it('編輯器拿到各自的資料、欄位、轉換與來源', async () => {
      const fields = [{ name: 'Region' }] as unknown as Props['fields'];
      const onChange = vi.fn();
      await mount({
        fields,
        fieldTransforms: [dateTrunc],
        sourceId: 's1',
        tableName: 'dbo.Orders',
        onSummarizeFiltersChange: onChange,
      });
      await toManual();
      const propsOf = (name: string) =>
        mocks.editors.mock.calls.filter((c) => c[0] === name).slice(-1)[0][1];
      expect(propsOf('transform')).toEqual(
        expect.objectContaining({
          transforms: [dateTrunc],
          fields,
          sourceId: 's1',
          tableName: 'dbo.Orders',
        }),
      );
      expect(propsOf('viewBy')).toEqual(
        expect.objectContaining({ fields, transforms: [dateTrunc] }),
      );
      expect(propsOf('measure')).toEqual(expect.objectContaining({ fields }));
      expect(propsOf('filter')).toEqual(
        expect.objectContaining({
          fields,
          transforms: [dateTrunc],
          onChange,
          sourceId: 's1',
          tableName: 'dbo.Orders',
        }),
      );
    });

    it('執行鈕：要有分組與聚合、且不在執行中才可按；按下呼叫 onRunSummarize', async () => {
      const onRunSummarize = vi.fn();
      const viewBy: ViewByEntry[] = [
        { id: 1, fieldName: 'Region', type: 'Plain', alias: '', transformId: null },
      ];
      const measures = [{ fieldName: 'Revenue', aggregation: 'Sum' as const }];
      await mount({ onRunSummarize });
      await toManual();
      expect(button('執行查詢').disabled).toBe(true);

      await mount({ viewByEntries: viewBy });
      expect(button('執行查詢').disabled).toBe(true);

      await mount({ viewByEntries: [], measures });
      expect(button('執行查詢').disabled).toBe(true);

      await mount({ viewByEntries: viewBy, measures, isLoading: true });
      expect(button('執行中…').disabled).toBe(true);

      await mount({ isLoading: false });
      await click(button('執行查詢'));
      expect(onRunSummarize).toHaveBeenCalledTimes(1);
    });

    it('手動的錯誤只在手動模式顯示；AI 的錯誤不影響手動', async () => {
      await mount({ error: { status: 500 } });
      expect(text()).not.toContain('查詢失敗，詳情請查看主控台。');
      await toManual();
      expect(text()).toContain('查詢失敗，詳情請查看主控台。');

      await mount({ error: null, nlError: { status: 500 } });
      expect(text()).not.toContain('AI 無法回答這個問題');
      expect(text()).not.toContain('查詢失敗');
    });
  });

  describe('問 AI', () => {
    it('沒登入：按鈕是「登入以使用 AI」，空白也能按，按下帶著目前路徑去登入，不發查詢', async () => {
      const onRunNlSummarize = vi.fn();
      const original = window.location;
      Object.defineProperty(window, 'location', {
        configurable: true,
        value: { href: '', pathname: '/data-analysis' },
      });
      try {
        await mount({ isLoggedIn: false, onRunNlSummarize });
        expect(button('登入以使用 AI').disabled).toBe(false);
        await click(button('登入以使用 AI'));
        expect(window.location.href).toBe('/api/account/login/Google?ReturnUrl=%2Fdata-analysis');
        expect(onRunNlSummarize).not.toHaveBeenCalled();

        // 快捷鍵只在有內容時有作用，沒登入也一樣
        window.location.href = '';
        await keyDown({ key: 'Enter', ctrlKey: true });
        expect(window.location.href).toBe('');
      } finally {
        Object.defineProperty(window, 'location', { configurable: true, value: original });
      }
    });

    it('登入：空白或只有空格不能按；查詢中顯示「生成中…」且停用', async () => {
      await mount();
      expect(button('生成').disabled).toBe(true);
      await type('   ');
      expect(button('生成').disabled).toBe(true);
      await type('各地區營收');
      expect(button('生成').disabled).toBe(false);

      await mount({ nlIsLoading: true });
      expect(button('生成中…').disabled).toBe(true);
    });

    it('生成：去掉前後空白後送出，並記進最近查詢（新的在前、不重複、最多 5 筆）', async () => {
      const onRunNlSummarize = vi.fn();
      await mount({ onRunNlSummarize });
      expect(text()).not.toContain('最近查詢');

      for (const q of ['  a  ', 'b', 'c', 'a', 'd', 'e', 'f']) {
        await type(q);
        await click(button('生成'));
      }
      expect(onRunNlSummarize.mock.calls.map((c) => c[0])).toEqual([
        'a',
        'b',
        'c',
        'a',
        'd',
        'e',
        'f',
      ]);
      expect(text()).toContain('最近查詢 (5)');

      const recentPanel = () => container.querySelector('.MuiCollapse-root') as HTMLElement;
      expect(collapsed(recentPanel())).toBe(true);
      await click(button('最近查詢'));
      await settle();
      expect(expanded(recentPanel())).toBe(true);
      const recent = Array.from(recentPanel().querySelectorAll('.MuiChip-label')).map(
        (el) => el.textContent,
      );
      expect(recent).toEqual(['f', 'e', 'd', 'a', 'c']);

      await click(chip('c')[0]);
      expect(textarea().value).toBe('c');
    });

    it('Ctrl／Cmd＋Enter 送出（有內容時）；單按 Enter 不送', async () => {
      const onRunNlSummarize = vi.fn();
      await mount({ onRunNlSummarize });
      await keyDown({ key: 'Enter', ctrlKey: true });
      expect(onRunNlSummarize).not.toHaveBeenCalled();

      await type('q1');
      await keyDown({ key: 'Enter' });
      expect(onRunNlSummarize).not.toHaveBeenCalled();
      await keyDown({ key: 'Enter', ctrlKey: true });
      await keyDown({ key: 'Enter', metaKey: true });
      expect(onRunNlSummarize).toHaveBeenCalledTimes(2);
    });

    it('點範例問題會填進輸入框', async () => {
      await mount();
      await click(chip('每月訂單數量趨勢')[0]);
      expect(textarea().value).toBe('每月訂單數量趨勢');
    });

    it('查詢中顯示三個階段：一開始在第一步，2.2 秒到第二步，5.5 秒到第三步；手動模式不顯示', async () => {
      vi.useFakeTimers();
      await mount({ nlIsLoading: true });
      const steps = () =>
        ['檢查資料中的實際數值', '理解你的問題', '執行查詢'].map((step) => {
          const row = Array.from(container.querySelectorAll('p')).find(
            (p) => p.textContent === step,
          )!.parentElement!;
          return row.querySelector('[data-testid="CheckIcon"]')
            ? 'done'
            : row.querySelector('.MuiCircularProgress-root')
              ? 'running'
              : 'waiting';
        });
      expect(steps()).toEqual(['running', 'waiting', 'waiting']);
      await act(async () => vi.advanceTimersByTime(2200));
      expect(steps()).toEqual(['done', 'running', 'waiting']);
      await act(async () => vi.advanceTimersByTime(3300));
      expect(steps()).toEqual(['done', 'done', 'running']);

      // 下一次查詢從第一步重來
      await mount({ nlIsLoading: false });
      await mount({ nlIsLoading: true });
      expect(steps()).toEqual(['running', 'waiting', 'waiting']);

      await toManual();
      expect(text()).not.toContain('理解你的問題');
    });

    it('查詢提早結束又重來：上一次的計時不會讓新的查詢跳步', async () => {
      vi.useFakeTimers();
      await mount({ nlIsLoading: true });
      await act(async () => vi.advanceTimersByTime(1000));
      await mount({ nlIsLoading: false });
      await mount({ nlIsLoading: true });
      // 上一次的 2.2 秒會落在這裡；這次的第一步要到 2.2 秒後才完成
      await act(async () => vi.advanceTimersByTime(1500));
      expect(container.querySelector('[data-testid="CheckIcon"]')).toBeNull();
    });

    it('AI 錯誤：說明可能沒有符合的欄位，附範例問題，點了填進輸入框', async () => {
      await mount({ nlError: { status: 422 } });
      expect(text()).toContain('AI 無法回答這個問題');
      const alert = container.querySelector('.MuiAlert-root')!;
      const example = Array.from(alert.querySelectorAll('.MuiChip-root')).find(
        (el) => el.textContent === '各地區總營收',
      );
      await click(example);
      expect(textarea().value).toBe('各地區總營收');
    });
  });

  describe('結果', () => {
    it('還沒有結果：不顯示結果區', async () => {
      await mount();
      expect(text()).not.toContain('筆資料');
      expect(mocks.viz).not.toHaveBeenCalled();
    });

    it('AI 查詢跑完但 0 筆：顯示筆數、AI 的解讀，與「找不到符合」及範例問題，沒有圖表', async () => {
      await mount({ nlInterpretation: '各地區的營收', nlRows: [] });
      expect(text()).toContain('回傳 0 筆資料');
      expect(text()).toContain('各地區的營收');
      expect(text()).toContain('找不到符合此問題的資料');
      expect(mocks.viz).not.toHaveBeenCalled();
    });

    it('AI 有資料：圖表拿到列、第一列的欄位、分組與時間欄位提示、可加到儀表板的計畫', async () => {
      const rows = [{ Month: '2026-01', Region: 'EMEA', Revenue: 10 }];
      await mount({
        nlRows: rows,
        nlInterpretation: 'x',
        nlPlan,
        sourceId: 's1',
        tableName: 'dbo.Orders',
        canAddToDashboard: true,
      });
      expect(text()).toContain('回傳 1 筆資料');
      expect(text()).not.toContain('找不到符合');
      expect(lastViz()).toEqual(
        expect.objectContaining({
          rows,
          headers: ['Month', 'Region', 'Revenue'],
          groupHints: ['Month', 'Region'],
          temporalHints: ['Month'],
          currentPlan: {
            context: { sourceId: 's1', tableName: 'dbo.Orders' },
            viewBy: nlPlan.viewBy,
            measures: nlPlan.measures,
            filters: nlPlan.filters,
          },
          canAddToDashboard: true,
        }),
      );
    });

    it('AI 結果的欄位取自第一列', async () => {
      await mount({ nlRows: [{ A: 1 }, { A: 2, B: 3 }], nlInterpretation: 'x' });
      expect(lastViz()).toEqual(expect.objectContaining({ headers: ['A'] }));
    });

    it('AI 計畫沒有來源 → 計畫不帶 context；沒有計畫 → 沒有可加的計畫', async () => {
      const rows = [{ Region: 'EMEA' }];
      await mount({ nlRows: rows, nlInterpretation: 'x', nlPlan });
      expect((lastViz()!.currentPlan as SummarizeRequest).context).toBeUndefined();

      await mount({ nlPlan: undefined });
      expect(lastViz()).toEqual(
        expect.objectContaining({ currentPlan: null, groupHints: [], temporalHints: [] }),
      );
    });

    it('每次新的結果（版本號變了）圖表重新掛載，同一個結果不會', async () => {
      const rows = [{ Region: 'EMEA' }];
      await mount({ nlRows: rows, nlInterpretation: 'x', nlResultVersion: 1 });
      const first = lastViz()!.mount;
      await mount({ canAddToDashboard: true });
      expect(lastViz()!.mount).toBe(first);
      await mount({ nlResultVersion: 2 });
      expect(lastViz()!.mount).not.toBe(first);
    });

    it('原始查詢計畫：有計畫才有按鈕，預設收合，按了展開；下一次生成又收合', async () => {
      await mount({ nlRows: [{ Region: 'EMEA' }], nlInterpretation: 'x' });
      expect(container.querySelector('[data-testid="CodeIcon"]')).toBeNull();

      await mount({ nlPlan });
      const planPanel = () =>
        Array.from(container.querySelectorAll('.MuiCollapse-root')).find((el) =>
          el.textContent?.includes('"viewBy"'),
        ) as HTMLElement;
      expect(collapsed(planPanel())).toBe(true);
      await click(container.querySelector('[data-testid="CodeIcon"]')!.closest('button')!);
      await settle();
      expect(expanded(planPanel())).toBe(true);
      expect(planPanel().textContent).toContain('"fieldName": "Revenue"');

      await type('next');
      await click(button('生成'));
      await settle();
      expect(collapsed(planPanel())).toBe(true);
    });

    it('AI 的附註（找不到的篩選值）逐條列出；沒有就不顯示', async () => {
      await mount({
        nlRows: [{ Region: 'EMEA' }],
        nlInterpretation: 'x',
        nlNotes: ['找不到 Region = Mars', '找不到 Status = X'],
      });
      expect(text()).toContain('找不到 Region = Mars');
      expect(text()).toContain('找不到 Status = X');

      await mount({ nlNotes: [] });
      expect(container.querySelector('.MuiAlert-root')).toBeNull();
    });

    it('手動有資料：欄位用 summarizeHeaders，時間欄位只算轉換是 DateTrunc 的分組，計畫是上一次執行的', async () => {
      const lastRequest: SummarizeRequest = { viewBy: [], measures: [] };
      const viewByEntries: ViewByEntry[] = [
        { id: 1, fieldName: 'OrderDate', type: 'Transform', alias: 'Month', transformId: 7 },
        { id: 2, fieldName: 'Region', type: 'Transform', alias: '', transformId: 8 },
        { id: 3, fieldName: 'Status', type: 'Plain', alias: '', transformId: null },
        { id: 4, fieldName: 'Other', type: 'Transform', alias: '', transformId: 99 },
        // A Plain group-by is never temporal, even if it still carries a transform id.
        { id: 5, fieldName: 'Shipped', type: 'Plain', alias: '', transformId: 7 },
      ];
      await mount({
        summarizeRows: [{ Month: '2026-01' }],
        summarizeHeaders: ['Month', 'Region', 'Status', 'Other', 'Shipped', 'Revenue'],
        viewByEntries,
        fieldTransforms: [dateTrunc, stringGroups],
        summarizeLastRequest: lastRequest,
        nlInterpretation: 'AI 的解讀',
        nlNotes: ['AI 附註'],
        nlPlan,
      });
      await toManual();
      expect(text()).toContain('回傳 1 筆資料');
      expect(text()).not.toContain('AI 的解讀');
      expect(text()).not.toContain('AI 附註');
      expect(container.querySelector('[data-testid="CodeIcon"]')).toBeNull();
      expect(text()).not.toContain('"viewBy"');
      expect(lastViz()).toEqual(
        expect.objectContaining({
          headers: ['Month', 'Region', 'Status', 'Other', 'Shipped', 'Revenue'],
          groupHints: ['Month', 'Region', 'Status', 'Other', 'Shipped'],
          temporalHints: ['Month'],
          currentPlan: lastRequest,
        }),
      );
    });

    it('手動的新結果（summarizeVersion 變了）同樣重新掛載圖表', async () => {
      await mount({ summarizeRows: [{ B: 1 }], summarizeHeaders: ['B'], summarizeVersion: 1 });
      await toManual();
      const first = lastViz()!.mount;
      await mount({ nlResultVersion: 5 });
      expect(lastViz()!.mount).toBe(first);
      await mount({ summarizeVersion: 2 });
      expect(lastViz()!.mount).not.toBe(first);
    });

    it('切換模式就切換顯示的結果，各自的圖表分開掛載', async () => {
      await mount({
        nlRows: [{ A: 1 }, { A: 2 }],
        nlInterpretation: 'x',
        summarizeRows: [{ B: 1 }],
        summarizeHeaders: ['B'],
      });
      expect(text()).toContain('回傳 2 筆資料');
      const aiMount = lastViz()!.mount;
      await toManual();
      expect(text()).toContain('回傳 1 筆資料');
      expect(lastViz()).toEqual(expect.objectContaining({ headers: ['B'] }));
      expect(lastViz()!.mount).not.toBe(aiMount);
    });

    it('手動沒有欄位就沒有結果區，即使 AI 有解讀', async () => {
      await mount({ nlInterpretation: 'x' });
      await toManual();
      expect(text()).not.toContain('筆資料');
    });
  });
});

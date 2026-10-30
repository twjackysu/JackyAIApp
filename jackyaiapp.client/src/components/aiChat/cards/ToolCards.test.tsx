import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { AiChatConfig, AiChatConfigContext } from '../config';

import ToolCards from './ToolCards';

import type { CustomCard, TableCard, ToolCard } from './toolCard';
import type { ToolStep } from '../utils/toolActivity';
import type { ECOption } from '@/charts/echarts';

const charts = vi.hoisted(() => ({ drawn: [] as ECOption[], fail: false }));
vi.mock('@/charts/ReactECharts', () => ({
  default: ({ option }: { option: ECOption }) => {
    if (charts.fail) throw new Error('chunk gone');
    charts.drawn.push(option);
    return <div data-testid="echarts" />;
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
});

const config = (patch: Partial<AiChatConfig> = {}): AiChatConfig => ({
  profile: 'test',
  greetingTitle: '',
  greeting: '',
  examples: [],
  suggestions: [],
  connectedLabel: '',
  disclaimer: '',
  signedOutPlaceholder: '',
  inputPlaceholder: { narrow: '', wide: '' },
  ...patch,
});

const step = (key: string, card?: ToolCard): ToolStep => ({
  key,
  name: key,
  label: key,
  status: 'done',
  card,
});

function render(steps: ToolStep[], cfg = config()) {
  act(() =>
    root.render(
      <MemoryRouter>
        <AiChatConfigContext.Provider value={cfg}>
          <ToolCards steps={steps} />
        </AiChatConfigContext.Provider>
      </MemoryRouter>,
    ),
  );
}

const cards = () => [...container.querySelectorAll('[data-testid="tool-card"]')];

describe('ToolCards', () => {
  it('draws nothing when no tool returned a card', () => {
    render([step('a')]);
    expect(container.innerHTML).toBe('');
  });

  it('draws a table, numbers grouped and right-aligned, and says when rows were left out', () => {
    render([
      step('t', {
        kind: 'table',
        title: '各地區營收',
        columns: [
          { key: 'region', label: '地區', numeric: false },
          { key: 'revenue', label: '營收', numeric: true },
        ],
        rows: [
          { region: '北區', revenue: 1234567 },
          { region: '<b>南區</b>', revenue: null },
        ],
        totalRows: 40,
      }),
    ]);
    expect(cards()).toHaveLength(1);
    const text = container.textContent ?? '';
    expect(text).toContain('各地區營收');
    expect(text).toContain('1,234,567');
    expect(text).toContain('—');
    expect(text).toContain('顯示前 2 筆，共 40 筆');
    // Text from a tool is text, never markup.
    expect(container.querySelector('b')).toBeNull();
    expect(text).toContain('<b>南區</b>');
  });

  it('links to a page on this site in the same tab', () => {
    render([
      step('l', {
        kind: 'link',
        title: 'serendipity',
        description: null,
        href: '/english/dictionary/serendipity',
        actionLabel: '看單字',
      }),
    ]);
    const a = container.querySelector('a')!;
    expect(a.getAttribute('href')).toBe('/english/dictionary/serendipity');
    expect(a.getAttribute('target')).toBeNull();
    expect(a.textContent).toBe('看單字');
  });

  it('opens an https link in a new tab, without handing it this page', () => {
    render([
      step('l', {
        kind: 'link',
        title: 'Docs',
        description: null,
        href: 'https://example.com',
        actionLabel: null,
      }),
    ]);
    const a = container.querySelector('a')!;
    expect(a.getAttribute('target')).toBe('_blank');
    expect(a.getAttribute('rel')).toBe('noopener noreferrer');
  });

  it("names the host an outside link goes to, whatever the tool's title says", () => {
    render([
      step('l', {
        kind: 'link',
        title: '台積電官網',
        description: null,
        href: 'https://login.example.net/tsmc',
        actionLabel: null,
      }),
    ]);
    expect(container.textContent).toContain('login.example.net');
  });

  it('names no host for a page on this site', () => {
    render([
      step('l', {
        kind: 'link',
        title: 'Repo',
        description: null,
        href: '/english/repository',
        actionLabel: null,
      }),
    ]);
    expect(container.textContent).toBe('Repo前往');
  });

  it("draws an app's own kind with the app's renderer, in the order the tools ran", () => {
    const Chart = ({ card }: { card: CustomCard }) => <span>[chart {card.title}]</span>;
    render(
      [
        step('c', { kind: 'chart', title: '趨勢', data: {} }),
        step('l', { kind: 'link', title: 'x', description: null, href: '/x', actionLabel: null }),
      ],
      config({ cardRenderers: { chart: Chart } }),
    );
    expect(cards().map((c) => c.textContent)).toEqual(['[chart 趨勢]', 'x前往']);
  });

  it("says a card the app's renderer cannot draw cannot be shown, keeping the others", () => {
    const Broken = (): never => {
      throw new Error('bad data');
    };
    const quiet = vi.spyOn(console, 'error').mockImplementation(() => {});
    render(
      [
        step('c', { kind: 'chart', title: '趨勢', data: {} }),
        step('l', { kind: 'link', title: 'x', description: null, href: '/x', actionLabel: null }),
      ],
      config({ cardRenderers: { chart: Broken } }),
    );
    quiet.mockRestore();
    expect(cards().map((c) => c.textContent)).toEqual(['這張卡片無法顯示', 'x前往']);
  });

  it("tells the app's renderer whether the card is a saved one, drawn again from a reopened conversation", () => {
    const Quiz = ({ saved }: { card: CustomCard; saved?: boolean }) => (
      <span>{saved ? 'saved' : 'live'}</span>
    );
    const cfg = config({ cardRenderers: { quiz: Quiz } });
    render([step('q', { kind: 'quiz', title: null, data: {} })], cfg);
    expect(cards().map((c) => c.textContent)).toEqual(['live']);
    act(() =>
      root.render(
        <MemoryRouter>
          <AiChatConfigContext.Provider value={cfg}>
            <ToolCards cards={[{ kind: 'quiz', title: null, data: {} }]} />
          </AiChatConfigContext.Provider>
        </MemoryRouter>,
      ),
    );
    expect(cards().map((c) => c.textContent)).toEqual(['saved']);
  });

  it('leaves out a kind nobody draws', () => {
    render([step('c', { kind: 'chart', title: null, data: {} })]);
    expect(cards()).toHaveLength(0);
  });
});

describe('ToolCards: a table to take out (D-150)', () => {
  const table: TableCard = {
    kind: 'table',
    title: '外資買賣超',
    columns: [
      { key: 'code', label: '代號', numeric: false },
      { key: 'net', label: '買賣超', numeric: true },
    ],
    rows: [
      { code: '2330', net: 12345 },
      { code: '=HYPERLINK("x")', net: null },
    ],
    totalRows: null,
  };
  const button = (label: string) =>
    container.querySelector<HTMLButtonElement>(`button[aria-label="${label}"]`);

  afterEach(() => vi.restoreAllMocks());

  it('offers copy and CSV under a table card, and copies it as cells a spreadsheet pastes, formulas defused', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
    render([step('t', table)]);
    expect(button('下載 CSV')).not.toBeNull();
    await act(async () => button('複製表格')!.click());
    expect(writeText).toHaveBeenCalledWith('代號\t買賣超\n2330\t12345\n"\'=HYPERLINK(""x"")"\t');
    expect(container.querySelector('[role="status"]')!.textContent).toBe('已複製 2 筆');
  });

  it('copies it as a table for a document, with the tab-separated text beside it (D-168)', async () => {
    const items: Record<string, Blob>[] = [];
    vi.stubGlobal(
      'ClipboardItem',
      class {
        constructor(public parts: Record<string, Blob>) {
          items.push(parts);
        }
      },
    );
    const write = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', { value: { write }, configurable: true });
    try {
      render([step('t', table)]);
      await act(async () => button('複製成文件表格')!.click());
      expect(write).toHaveBeenCalledTimes(1);
      const html = await items[0]['text/html'].text();
      expect(html.startsWith('<table')).toBe(true);
      expect(html).toContain('>12,345</td>');
      expect(await items[0]['text/plain'].text()).toBe(
        '代號\t買賣超\n2330\t12345\n"\'=HYPERLINK(""x"")"\t',
      );
      expect(container.querySelector('[role="status"]')!.textContent).toBe(
        '已複製 2 筆，可貼到文件',
      );
    } finally {
      vi.unstubAllGlobals();
    }
  });

  it('says so when the browser cannot copy a table for a document', async () => {
    vi.stubGlobal('ClipboardItem', undefined);
    try {
      render([step('t', table)]);
      await act(async () => button('複製成文件表格')!.click());
      expect(container.querySelector('[role="status"]')!.textContent).toBe(
        '這個瀏覽器不能複製成文件表格，請改用複製表格',
      );
    } finally {
      vi.unstubAllGlobals();
    }
  });

  it('says nothing was copied when the copy fails, rather than 已複製', async () => {
    Object.defineProperty(navigator, 'clipboard', {
      value: { writeText: vi.fn().mockRejectedValue(new Error('denied')) },
      configurable: true,
    });
    document.execCommand = vi.fn(() => false);
    render([step('t', table)]);
    await act(async () => button('複製表格')!.click());
    expect(container.querySelector('[role="status"]')!.textContent).toBe(
      '沒有複製成功，請改用下載 CSV',
    );
  });

  it('says it again for its full time on a second press with the same words', async () => {
    vi.useFakeTimers();
    try {
      Object.defineProperty(navigator, 'clipboard', {
        value: { writeText: vi.fn().mockResolvedValue(undefined) },
        configurable: true,
      });
      render([step('t', table)]);
      const status = () => container.querySelector('[role="status"]')!.textContent;
      await act(async () => button('複製表格')!.click());
      await act(async () => vi.advanceTimersByTime(2500));
      await act(async () => button('複製表格')!.click());
      await act(async () => vi.advanceTimersByTime(2500));
      expect(status()).toBe('已複製 2 筆');
      await act(async () => vi.advanceTimersByTime(600));
      expect(status()).toBe('');
    } finally {
      vi.useRealTimers();
    }
  });

  it('sorts by a header it is pressed on, and copies the rows in the order on screen', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
    const rows = {
      ...table,
      rows: [
        { code: '2603', net: -500 },
        { code: '2330', net: 12345 },
      ],
    };
    render([step('t', rows)]);
    const header = () => [...container.querySelectorAll('th')][1];
    const codes = () =>
      [...container.querySelectorAll('tbody tr')].map((r) => r.firstChild!.textContent);
    expect(codes()).toEqual(['2603', '2330']);
    act(() => header().querySelector<HTMLElement>('.MuiTableSortLabel-root')!.click());
    expect(header().getAttribute('aria-sort')).toBe('descending');
    expect(codes()).toEqual(['2330', '2603']);
    await act(async () => button('複製表格')!.click());
    expect(writeText).toHaveBeenCalledWith('代號\t買賣超\n2330\t12345\n2603\t-500');
    act(() => header().querySelector<HTMLElement>('.MuiTableSortLabel-root')!.click());
    act(() => header().querySelector<HTMLElement>('.MuiTableSortLabel-root')!.click());
    expect(header().getAttribute('aria-sort')).toBeNull();
    expect(codes()).toEqual(['2603', '2330']);
  });

  it('downloads a CSV named after the card, with a BOM, and says the tool found more when it did', async () => {
    const blobs: Blob[] = [];
    URL.createObjectURL = vi.fn((b: Blob) => {
      blobs.push(b);
      return 'blob:x';
    });
    URL.revokeObjectURL = vi.fn();
    let downloaded = '';
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (
      this: HTMLAnchorElement,
    ) {
      downloaded = this.download;
    });
    render([step('t', { ...table, totalRows: 1200 })]);
    act(() => button('下載 CSV')!.click());
    expect(downloaded).toMatch(/^外資買賣超 \d{4}-\d{2}-\d{2}\.csv$/);
    const bytes = new Uint8Array(await blobs[0].arrayBuffer());
    expect([...bytes.slice(0, 3)]).toEqual([0xef, 0xbb, 0xbf]);
    expect(new TextDecoder().decode(bytes.slice(3))).toBe(
      '代號,買賣超\r\n2330,12345\r\n"\'=HYPERLINK(""x"")",\r\n',
    );
    expect(container.querySelector('[role="status"]')!.textContent).toBe(
      '已下載畫面上的 2 筆（共找到 1,200 筆）',
    );
  });

  it("offers them under an app's card it says how to read as a table, not under a link or an empty table", () => {
    const Chart = () => <span>chart</span>;
    render(
      [
        step('c', { kind: 'chart', title: '趨勢', data: {} }),
        step('l', { kind: 'link', title: 'x', description: null, href: '/x', actionLabel: null }),
        step('e', { ...table, rows: [] }),
      ],
      config({ cardRenderers: { chart: Chart }, cardTables: { chart: () => table } }),
    );
    const withButtons = cards().map(
      (c) => c.querySelector('button[aria-label="下載 CSV"]') !== null,
    );
    expect(withButtons).toEqual([true, false, false]);
  });

  it("offers nothing under an app's card it gives no table for", () => {
    const Quiz = () => <span>quiz</span>;
    render(
      [step('q', { kind: 'quiz', title: null, data: {} })],
      config({ cardRenderers: { quiz: Quiz }, cardTables: { quiz: () => null } }),
    );
    expect(button('下載 CSV')).toBeNull();
  });
});

describe('ToolCards: 畫成圖 (D-153)', () => {
  const holdings: TableCard = {
    kind: 'table',
    title: '持倉',
    columns: [
      { key: 'name', label: '持倉', numeric: false },
      { key: 'value', label: '市值', numeric: true },
      { key: 'pct', label: '損益 %', numeric: true },
    ],
    rows: [
      { name: '長榮（2603）', value: 98000, pct: '-18.0%' },
      { name: '台積電（2330）', value: 1245000, pct: '49.5%' },
    ],
    totalRows: null,
  };
  const button = (label: string) =>
    container.querySelector<HTMLButtonElement>(`button[aria-label="${label}"]`);
  const lastChart = () =>
    charts.drawn[charts.drawn.length - 1] as unknown as {
      series: { type: string; data: unknown[] }[];
      yAxis?: { data: string[] };
    };
  const settle = async () => {
    // The chart is loaded on the first press (React.lazy); the first import can take a while on a busy
    // machine (it failed once at 50ms while other work ran), so wait up to 2s.
    for (let i = 0; i < 200 && !container.querySelector('[data-testid="echarts"]'); i++) {
      await act(async () => new Promise((r) => setTimeout(r, 10)));
    }
  };

  beforeEach(() => {
    charts.drawn = [];
  });

  it('draws the rows in place of the table, in the order sorted, starting with the sorted column', async () => {
    render([step('t', holdings)]);
    const header = () => [...container.querySelectorAll('th')][2];
    act(() => header().querySelector<HTMLElement>('.MuiTableSortLabel-root')!.click());
    await act(async () => button('畫成圖')!.click());
    await settle();
    expect(container.querySelector('table')).toBeNull();
    expect(container.querySelector('[role="img"]')!.getAttribute('aria-label')).toBe(
      '損益 %的長條圖',
    );
    expect(lastChart().yAxis!.data).toEqual(['台積電（2330）', '長榮（2603）']);
    // A column of percentages is no pie: only one kind, so no kind picker; two columns, so a column picker.
    expect(container.querySelector('[aria-label="換一種圖表"]')).toBeNull();
    expect(container.querySelector('[aria-label="畫哪一欄"]')).not.toBeNull();
    expect(button('畫成圖')!.getAttribute('aria-pressed')).toBe('true');
    await act(async () => button('畫成圖')!.click());
    expect(container.querySelector('table')).not.toBeNull();
  });

  it('switches to another column and to a pie where it fits, with no question to the AI', async () => {
    const fetchSpy = vi.spyOn(globalThis, 'fetch');
    render([step('t', holdings)]);
    await act(async () => button('畫成圖')!.click());
    await settle();
    const pick = (group: string, label: string) =>
      [...container.querySelectorAll(`[aria-label="${group}"] button`)].find(
        (b) => b.textContent === label,
      ) as HTMLButtonElement;
    expect(lastChart().series[0].type).toBe('bar');
    await act(async () => pick('換一種圖表', '圓餅圖').click());
    expect(lastChart().series[0].type).toBe('pie');
    await act(async () => pick('畫哪一欄', '損益 %').click());
    // 損益 % cannot be a pie: back to bars.
    expect(lastChart().series[0].type).toBe('bar');
    expect(fetchSpy).not.toHaveBeenCalled();
  });

  it('shows the table, saying so, when the chart cannot be loaded or drawn', async () => {
    vi.spyOn(console, 'error').mockImplementation(() => {});
    render([step('t', { ...holdings, title: 'boom' })]);
    // The mocked chart throws for this card, as a chunk gone after a deploy would.
    charts.fail = true;
    try {
      await act(async () => button('畫成圖')!.click());
      await settle();
      expect(container.querySelector('table')).not.toBeNull();
      expect(container.textContent).toContain('圖表沒有載入成功');
    } finally {
      charts.fail = false;
    }
  });

  it('offers no chart for a table with nothing to draw', () => {
    render([
      step('t', {
        kind: 'table',
        title: null,
        columns: [
          { key: 'time', label: '時間', numeric: false },
          { key: 'title', label: '行程', numeric: false },
        ],
        rows: [
          { time: '09:00', title: '週會' },
          { time: '14:00', title: '面試' },
        ],
        totalRows: null,
      }),
    ]);
    expect(button('下載 CSV')).not.toBeNull();
    expect(button('畫成圖')).toBeNull();
  });
});

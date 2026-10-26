import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { AiChatConfig, AiChatConfigContext } from '../config';

import ToolCards from './ToolCards';

import type { CustomCard, TableCard, ToolCard } from './toolCard';
import type { ToolStep } from '../utils/toolActivity';

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
    expect(writeText).toHaveBeenCalledWith('代號\t買賣超\n2330\t12345\n\'=HYPERLINK("x")\t');
    expect(container.querySelector('[role="status"]')!.textContent).toBe('已複製 2 筆');
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

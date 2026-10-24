import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';

import { AiChatConfig, AiChatConfigContext } from '../config';

import ToolCards from './ToolCards';

import type { CustomCard, ToolCard } from './toolCard';
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

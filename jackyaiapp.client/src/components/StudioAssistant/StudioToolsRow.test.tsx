import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import StudioToolsRow from './StudioToolsRow';

import { installed } from '@/Connectors/testData';

const mocks = vi.hoisted(() => ({ catalog: vi.fn() }));

vi.mock('@/apis/connectorsApis/connectorsApis', () => ({
  useGetConnectorCatalogQuery: () => mocks.catalog(),
}));

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  mocks.catalog.mockReturnValue({ data: undefined });
  container = document.createElement('div');
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(() => {
  act(() => root.unmount());
  container.remove();
});

const render = () =>
  act(() =>
    root.render(
      <MemoryRouter>
        <StudioToolsRow />
      </MemoryRouter>,
    ),
  );

const chips = () => [...container.querySelectorAll('li .MuiChip-label')].map((c) => c.textContent);
const link = () => container.querySelector('a');

describe('StudioToolsRow', () => {
  it('draws nothing until the reader’s connectors are read', () => {
    render();
    expect(container.textContent).toBe('');
  });

  it('names the connectors Studio AI can use now, and links to the connectors page', () => {
    mocks.catalog.mockReturnValue({
      data: {
        entries: [],
        customLimit: 10,
        installed: [
          installed({ displayName: '本站功能', alwaysOn: true, category: 'site' }),
          installed({ id: 'd', displayName: 'DeepWiki', health: 'ok' }),
          installed({ id: 'n', displayName: 'Notion', health: 'needsAuth' }),
        ],
      },
    });
    render();

    expect(container.textContent).toContain('我能用的工具');
    expect(chips()).toEqual(['本站功能', 'DeepWiki']);
    expect(link()?.getAttribute('href')).toBe('/connectors');
    expect(link()?.textContent).toBe('管理 connectors');
  });

  it('counts the ones past the first five, and offers to add more when there are only the site’s own', () => {
    mocks.catalog.mockReturnValue({
      data: {
        entries: [],
        customLimit: 10,
        installed: Array.from({ length: 7 }, (_, i) =>
          installed({ id: `c${i}`, displayName: `C${i}`, health: 'ok' }),
        ),
      },
    });
    render();
    expect(chips()).toHaveLength(5);
    expect(container.textContent).toContain('還有 2 個');

    mocks.catalog.mockReturnValue({
      data: {
        entries: [],
        customLimit: 10,
        installed: [installed({ displayName: '本站功能', alwaysOn: true, category: 'site' })],
      },
    });
    render();
    expect(link()?.textContent).toBe('接上更多工具');
  });
});

import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import Connectors from './index';

import type { ConnectorStatus } from '@/apis/connectorsApis/types';

const mocks = vi.hoisted(() => ({
  status: vi.fn(),
  connect: vi.fn(),
  disconnect: vi.fn(),
  refetch: vi.fn(),
}));

vi.mock('@/apis/connectorsApis/connectorsApis', () => ({
  useGetConnectorStatusQuery: () => mocks.status(),
  useConnectProviderMutation: () => [mocks.connect],
  useDisconnectProviderMutation: () => [mocks.disconnect],
}));

// The card has its own flows (custom client settings, copying a token); the page only lists one
// per provider and wires its toggle.
vi.mock('./ConnectorCard', async () => {
  const { createElement } = await import('react');
  return {
    default: (p: {
      connector: ConnectorStatus;
      onToggle: (provider: string, connect: boolean) => void;
    }) =>
      createElement(
        'button',
        { onClick: () => p.onToggle(p.connector.provider, !p.connector.isConnected) },
        `[Card ${p.connector.provider} ${p.connector.isConnected ? 'on' : 'off'}]`,
      ),
  };
});

// What the AI may use has its own tests (AiConnectorsSection.test.tsx); here it only has to be there.
vi.mock('./AiConnectorsSection', () => ({ default: () => '[AiConnectors]' }));

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

const status = (provider: string, isConnected: boolean): ConnectorStatus => ({
  provider,
  providerDisplayName: provider,
  isConnected,
  services: [],
  requiresReconnection: false,
});

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  container = document.createElement('div');
  document.body.appendChild(container);
  root = createRoot(container);
  mocks.status.mockReturnValue({
    data: [status('Google', true), status('Microsoft', false)],
    isLoading: false,
    error: undefined,
    refetch: mocks.refetch,
  });
});

afterEach(() => {
  act(() => root.unmount());
  container.remove();
  vi.clearAllMocks();
});

function render(path = '/connectors') {
  act(() =>
    root.render(
      <MemoryRouter initialEntries={[path]}>
        <Connectors />
      </MemoryRouter>,
    ),
  );
}

const text = () => document.body.textContent ?? '';
const button = (label: string) =>
  [...container.querySelectorAll('button')].find((b) => b.textContent === label)!;

describe('Connectors page — the account connections', () => {
  it('shows what the AI may use above the account connections', () => {
    render();
    expect(text().indexOf('[AiConnectors]')).toBeLessThan(text().indexOf('[Card Google on]'));
  });

  it('lists one card per provider', () => {
    render();
    expect(text()).toContain('[Card Google on]');
    expect(text()).toContain('[Card Microsoft off]');
  });

  it('says why when the status cannot be read', () => {
    mocks.status.mockReturnValue({
      data: undefined,
      isLoading: false,
      error: { status: 500, data: { error: { code: 1500000, message: '壞了' } } },
      refetch: mocks.refetch,
    });
    render();
    expect(text()).toContain('Failed to load connector status');
  });

  it('connecting goes to the provider the server answers with', async () => {
    const assign = vi.fn();
    const original = window.location;
    Object.defineProperty(window, 'location', {
      configurable: true,
      value: {
        ...original,
        set href(url: string) {
          assign(url);
        },
      },
    });
    mocks.connect.mockReturnValue({
      unwrap: () => Promise.resolve({ redirectUrl: 'https://login.example/authorize' }),
    });
    render();
    await act(async () => button('[Card Microsoft off]').click());
    expect(mocks.connect).toHaveBeenCalledWith({ provider: 'Microsoft', customConfig: undefined });
    expect(assign).toHaveBeenCalledWith('https://login.example/authorize');
    Object.defineProperty(window, 'location', { configurable: true, value: original });
  });

  it('disconnecting says so', async () => {
    mocks.disconnect.mockReturnValue({ unwrap: () => Promise.resolve({ message: 'ok' }) });
    render();
    await act(async () => button('[Card Google on]').click());
    expect(mocks.disconnect).toHaveBeenCalledWith('Google');
    expect(text()).toContain('Disconnected from Google');
  });

  it('reports the OAuth callback result and refreshes the status', () => {
    render('/connectors?success=true&provider=Google');
    expect(text()).toContain('Successfully connected to Google');
    expect(mocks.refetch).toHaveBeenCalled();
  });

  it('turns a denied authorization into words', () => {
    render('/connectors?error=access_denied&provider=Google');
    expect(text()).toContain('Access was denied by the user');
  });
});

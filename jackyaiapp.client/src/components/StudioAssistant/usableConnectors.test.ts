import { describe, expect, it } from 'vitest';

import { usableConnectors } from './usableConnectors';

import type { ConnectorHealth, InstalledConnector } from '@/apis/connectorsApis/types';

const connector = (
  key: string,
  health: ConnectorHealth,
  permissions: ('auto' | 'confirm' | 'disabled')[] = ['auto'],
  alwaysOn = false,
): InstalledConnector => ({
  id: alwaysOn ? null : key,
  definitionKey: key,
  displayName: key,
  description: '',
  category: alwaysOn ? 'site' : 'dev',
  serverUrl: null,
  authKind: 'none',
  health,
  alwaysOn,
  lastError: null,
  lastTestedAt: null,
  scopes: [],
  tools: permissions.map((permission, i) => ({
    name: `t${i}`,
    title: null,
    description: '',
    effect: 'read',
    permission,
  })),
});

describe('usableConnectors', () => {
  it('lists the working ones with a tool the AI may see, the site’s own first', () => {
    const usable = usableConnectors([
      connector('deepwiki', 'ok'),
      connector('fresh', 'unknown', ['confirm']),
      connector('site', 'unknown', ['auto'], true),
    ]);

    expect(usable.map((c) => c.definitionKey)).toEqual(['site', 'deepwiki', 'fresh']);
  });

  it('leaves out one that needs signing in again, failed, is switched off or has every tool off', () => {
    const usable = usableConnectors([
      connector('notion', 'needsAuth'),
      connector('broken', 'error'),
      connector('off', 'disabled'),
      connector('quiet', 'ok', ['disabled', 'disabled']),
      connector('none', 'ok', []),
    ]);

    expect(usable).toEqual([]);
  });
});

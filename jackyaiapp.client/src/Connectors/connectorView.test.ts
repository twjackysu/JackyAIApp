import { describe, expect, it } from 'vitest';

import {
  canTryInStudio,
  drawerDescription,
  catalogCategories,
  catalogEntries,
  connectAction,
  installedOrder,
  installedServerUrls,
  serverUrlKey,
  toolCounts,
  lastTestedText,
  connectorToOpen,
} from './connectorView';
import { entry, installed } from './testData';

describe('catalogEntries', () => {
  const entries = [
    entry({ key: 'b', displayName: 'Beta', category: 'dev' }),
    entry({
      key: 'g',
      displayName: 'Google',
      authKind: 'existingProvider',
      category: 'productivity',
    }),
    entry({ key: 's', displayName: '本站工具', category: 'site' }),
    entry({ key: 'a', displayName: 'Alpha', category: 'dev' }),
  ];

  it('leaves out the account connections, which have cards of their own', () => {
    expect(catalogEntries(entries, null).map((e) => e.key)).not.toContain('g');
  });

  it('orders by category, then by name', () => {
    expect(catalogEntries(entries, null).map((e) => e.key)).toEqual(['s', 'a', 'b']);
  });

  it('keeps only the picked category', () => {
    expect(catalogEntries(entries, 'dev').map((e) => e.key)).toEqual(['a', 'b']);
  });

  it('offers chips only for categories that have something', () => {
    expect(catalogCategories(entries)).toEqual(['site', 'dev']);
  });
});

describe('connectAction', () => {
  it('adds a server without credentials at once', () => {
    expect(connectAction(entry({ authKind: 'none' }))).toBe('install');
  });
  it('asks for an API key or token first', () => {
    expect(connectAction(entry({ authKind: 'apiKey' }))).toBe('askCredentials');
    expect(connectAction(entry({ authKind: 'bearer' }))).toBe('askCredentials');
  });
  it('sends the reader to sign in for OAuth', () => {
    expect(connectAction(entry({ authKind: 'oauth' }))).toBe('authorize');
  });
  it('does nothing for one already added', () => {
    expect(connectAction(entry({ authKind: 'oauth', installed: true }))).toBe('installed');
  });
});

describe('installedOrder', () => {
  it('puts the ones needing attention first, then always-on, then by name', () => {
    const order = installedOrder([
      installed({ displayName: 'Zeta' }),
      installed({ displayName: '本站工具', alwaysOn: true }),
      installed({ displayName: 'Alpha' }),
      installed({ displayName: 'Broken', health: 'needsAuth' }),
    ]).map((c) => c.displayName);
    expect(order).toEqual(['Broken', '本站工具', 'Alpha', 'Zeta']);
  });
});

describe('toolCounts', () => {
  it('counts each permission', () => {
    const c = installed({
      tools: [
        { name: 'a', title: null, description: '', effect: 'read', permission: 'auto' },
        { name: 'b', title: null, description: '', effect: 'write', permission: 'confirm' },
        { name: 'c', title: null, description: '', effect: 'write', permission: 'confirm' },
      ],
    });
    expect(toolCounts(c)).toEqual({ auto: 1, confirm: 2, disabled: 0 });
  });
});

describe('drawerDescription', () => {
  it("keeps a catalog connector's description", () => {
    expect(drawerDescription(installed())).toBe('問任何 GitHub repo 的問題');
  });

  it('says nothing when the description is only the address the drawer lists anyway', () => {
    const own = installed({
      definitionKey: 'custom',
      description: 'https://kb.example.com/mcp',
      serverUrl: 'https://kb.example.com/mcp',
    });
    expect(drawerDescription(own)).toBeNull();
  });

  it('says nothing for an empty description', () => {
    expect(drawerDescription(installed({ description: '  ' }))).toBeNull();
  });
});

describe('canTryInStudio', () => {
  it('offers a connector the reader added that works and has a tool the AI sees', () => {
    expect(canTryInStudio(installed())).toBe(true);
    expect(canTryInStudio(installed({ health: 'unknown' }))).toBe(true);
  });

  it('does not offer one turned off, failing or waiting for a sign-in', () => {
    for (const health of ['disabled', 'error', 'needsAuth'] as const)
      expect(canTryInStudio(installed({ health }))).toBe(false);
  });

  it('does not offer one with no tool the AI may see', () => {
    expect(canTryInStudio(installed({ tools: [] }))).toBe(false);
    const off = installed().tools.map((t) => ({ ...t, permission: 'disabled' as const }));
    expect(canTryInStudio(installed({ tools: off }))).toBe(false);
  });

  it("does not offer the site's own tools, which are always there", () => {
    expect(canTryInStudio(installed({ alwaysOn: true, id: null }))).toBe(false);
  });
});

describe('serverUrlKey', () => {
  it('writes an address the way the site stores one', () => {
    expect(serverUrlKey('https://MCP.Example.com/mcp/')).toBe('https://mcp.example.com/mcp');
    expect(serverUrlKey(' https://mcp.example.com/mcp#x ')).toBe('https://mcp.example.com/mcp');
    expect(serverUrlKey('https://mcp.example.com/mcp?v=2')).toBe('https://mcp.example.com/mcp?v=2');
  });

  it('has nothing for no address, or one that is not an address', () => {
    expect(serverUrlKey(null)).toBeNull();
    expect(serverUrlKey('')).toBeNull();
    expect(serverUrlKey('not a url')).toBeNull();
  });
});

describe('installedServerUrls', () => {
  it("lists the reader's servers by address, leaving out those without one", () => {
    const urls = installedServerUrls([
      installed({ serverUrl: 'https://mcp.deepwiki.com/mcp/' }),
      installed({ alwaysOn: true, serverUrl: null }),
    ]);
    expect([...urls]).toEqual(['https://mcp.deepwiki.com/mcp']);
  });
});

describe('lastTestedText', () => {
  const now = new Date('2026-10-30T08:00:00Z');

  it('says minutes or hours ago within a day, the date in Taipei after that', () => {
    expect(lastTestedText('2026-10-30T07:59:40Z', now)).toBe('剛剛測試過');
    expect(lastTestedText('2026-10-30T07:35:00Z', now)).toBe('25 分鐘前測試');
    expect(lastTestedText('2026-10-29T09:00:00Z', now)).toBe('23 小時前測試');
    // 10/24 20:00 UTC is 10/25 in Taipei.
    expect(lastTestedText('2026-10-24T20:00:00Z', now)).toBe('10/25 測試');
  });

  it('says nothing for one never tested or a time it cannot read', () => {
    expect(lastTestedText(null, now)).toBeNull();
    expect(lastTestedText('not a time', now)).toBeNull();
  });
});

describe('connectorToOpen', () => {
  const params = (q: string) => new URLSearchParams(q);
  const mine = [installed({ id: 'i-1' }), installed({ id: null, definitionKey: 'site' })];

  it('asks for none without the parameter', () => {
    expect(connectorToOpen(params('x=1'), mine)).toBeUndefined();
  });

  it("finds the reader's connector by its id", () => {
    expect(connectorToOpen(params('connector=i-1'), mine)?.id).toBe('i-1');
  });

  it('is null for one that is not theirs, or that names no id', () => {
    expect(connectorToOpen(params('connector=i-2'), mine)).toBeNull();
    expect(connectorToOpen(params('connector='), mine)).toBeNull();
  });
});

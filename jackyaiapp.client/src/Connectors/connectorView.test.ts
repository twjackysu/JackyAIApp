import { describe, expect, it } from 'vitest';

import {
  canTryInStudio,
  drawerDescription,
  catalogCategories,
  catalogEntries,
  connectAction,
  installedOrder,
  toolCounts,
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

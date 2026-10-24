import { permissionCounts } from './toolPermission';

import type {
  CatalogEntry,
  ConnectorCategory,
  ConnectorHealth,
  InstalledConnector,
  ToolPermission,
} from '@/apis/connectorsApis/types';

/** The catalog's categories, in the order the page shows their chips. */
export const CATEGORY_ORDER: ConnectorCategory[] = [
  'site',
  'data',
  'finance',
  'dev',
  'productivity',
];

export const CATEGORY_LABELS: Record<ConnectorCategory, string> = {
  site: '本站',
  data: '資料',
  finance: '金融',
  dev: '開發',
  productivity: '生產力',
};

export interface HealthView {
  label: string;
  color: 'success' | 'warning' | 'error' | 'default';
}

export const HEALTH_VIEW: Record<ConnectorHealth, HealthView> = {
  ok: { label: '已連線', color: 'success' },
  needsAuth: { label: '需要重新授權', color: 'warning' },
  error: { label: '錯誤', color: 'error' },
  unknown: { label: '尚未測試', color: 'default' },
  disabled: { label: '已停用', color: 'default' },
};

/**
 * The catalog the page lists: the account connections (Google, Microsoft, Atlassian) have cards of
 * their own below, so they are left out here; the rest in category order, then by name.
 */
export function catalogEntries(
  entries: readonly CatalogEntry[],
  category: ConnectorCategory | null,
): CatalogEntry[] {
  return entries
    .filter((e) => e.authKind !== 'existingProvider')
    .filter((e) => category === null || e.category === category)
    .sort(
      (a, b) =>
        CATEGORY_ORDER.indexOf(a.category) - CATEGORY_ORDER.indexOf(b.category) ||
        a.displayName.localeCompare(b.displayName, 'zh-Hant'),
    );
}

/** The categories that have something to show, for the filter chips. */
export function catalogCategories(entries: readonly CatalogEntry[]): ConnectorCategory[] {
  const present = new Set(
    entries.filter((e) => e.authKind !== 'existingProvider').map((e) => e.category),
  );
  return CATEGORY_ORDER.filter((c) => present.has(c));
}

/**
 * What「連線」does for a catalog entry: one without credentials is added at once, one with an API
 * key asks for it first, one with OAuth sends the reader to sign in. One already added is done.
 */
export type ConnectAction = 'installed' | 'install' | 'askCredentials' | 'authorize';

export function connectAction(entry: CatalogEntry): ConnectAction {
  if (entry.installed) return 'installed';
  switch (entry.authKind) {
    case 'none':
      return 'install';
    case 'oauth':
      return 'authorize';
    default:
      return 'askCredentials';
  }
}

/** How many of a connector's tools run on their own, ask first, or are off. */
export function toolCounts(connector: InstalledConnector): Record<ToolPermission, number> {
  return permissionCounts(connector.tools.map((t) => t.permission));
}

/**
 * The connectors the reader has, the ones needing their attention first (a token the service no
 * longer takes, an error), then always-on, then by name.
 */
export function installedOrder(connectors: readonly InstalledConnector[]): InstalledConnector[] {
  const rank = (c: InstalledConnector) =>
    c.health === 'needsAuth' || c.health === 'error' ? 0 : c.alwaysOn ? 1 : 2;
  return [...connectors].sort(
    (a, b) => rank(a) - rank(b) || a.displayName.localeCompare(b.displayName, 'zh-Hant'),
  );
}

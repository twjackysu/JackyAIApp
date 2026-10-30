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

/**
 * The line under a connector's name in its drawer. A server of the reader's own is described by its
 * address, which the drawer already lists under 網址, so it says nothing rather than the same twice.
 */
export function drawerDescription(
  connector: Pick<InstalledConnector, 'description' | 'serverUrl' | 'alwaysOn'>,
): string | null {
  const text = connector.description.trim();
  if (!text) return null;
  if (!connector.alwaysOn && connector.serverUrl && text === connector.serverUrl) return null;
  return text;
}

/**
 * Whether a connector can be tried in Studio AI (D-126): one the reader added, not turned off or failing, with a tool
 * the AI may see. The site's own tools are always there, so they need no trying.
 */
export function canTryInStudio(connector: InstalledConnector): boolean {
  return (
    !connector.alwaysOn &&
    (connector.health === 'ok' || connector.health === 'unknown') &&
    connector.tools.some((t) => t.permission !== 'disabled')
  );
}

/**
 * A server address as the site stores one (https, no fragment, no trailing slash; the host in lower case), so a server
 * the official MCP Registry lists can be told apart from one the reader has already added; null for no address.
 */
export function serverUrlKey(url: string | null | undefined): string | null {
  if (!url) return null;
  try {
    const u = new URL(url.trim());
    return `${u.protocol}//${u.host}${u.pathname.replace(/\/+$/, '')}${u.search}`;
  } catch {
    return null;
  }
}

/** The addresses of the MCP servers the reader has added, as `serverUrlKey` writes them. */
export function installedServerUrls(installed: readonly InstalledConnector[]): Set<string> {
  return new Set(
    installed.map((c) => serverUrlKey(c.serverUrl)).filter((k): k is string => k !== null),
  );
}

const TAIPEI_DAY = new Intl.DateTimeFormat('zh-TW', {
  timeZone: 'Asia/Taipei',
  month: 'numeric',
  day: 'numeric',
});

/**
 * When a connector was last tested, as its card says it (design/05 §3: 最後測試時間): minutes or hours ago within a day,
 * the date in Taipei after that. Nothing for one never tested (the site's own) or a time that cannot be read.
 */
export function lastTestedText(lastTestedAt: string | null, now: Date = new Date()): string | null {
  if (!lastTestedAt) return null;
  const at = new Date(lastTestedAt);
  const minutes = Math.floor((now.getTime() - at.getTime()) / 60_000);
  if (Number.isNaN(minutes)) return null;
  if (minutes < 1) return '剛剛測試過';
  if (minutes < 60) return `${minutes} 分鐘前測試`;
  if (minutes < 24 * 60) return `${Math.floor(minutes / 60)} 小時前測試`;
  return `${TAIPEI_DAY.format(at)} 測試`;
}

/** The query parameter /connectors opens one of the reader's connectors with (a failed call's 「前往重新連線」 card, D-223). */
export const CONNECTOR_PARAM = 'connector';

/**
 * Which connector a /connectors address asks to open (`?connector=<installation id>`): `undefined` when it asks for
 * none, `null` when the one it names is not among the reader's (removed since the card was shown).
 */
export function connectorToOpen(
  params: URLSearchParams,
  installed: readonly InstalledConnector[],
): InstalledConnector | null | undefined {
  const id = params.get(CONNECTOR_PARAM);
  if (id === null) return undefined;
  return installed.find((c) => c.id !== null && c.id === id) ?? null;
}

/**
 * The id of /connectors' account connections (Google, Microsoft, Atlassian): a native connector's card links to
 * `/connectors#accounts` (D-225), and the page scrolls there, below what the AI may use.
 */
export const ACCOUNTS_ANCHOR = 'accounts';

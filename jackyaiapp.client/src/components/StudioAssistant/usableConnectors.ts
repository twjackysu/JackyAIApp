import type { InstalledConnector } from '@/apis/connectorsApis/types';

/** At most this many are named on the welcome screen; the rest are counted. */
export const MAX_NAMED_CONNECTORS = 5;

/**
 * The reader's connectors Studio AI can use now, as its welcome screen lists them (design/05 §1: 「我能用的工具」):
 * working (tested fine, or not tested yet) and with a tool the AI may see. One that needs signing in again, failed or is
 * switched off is left out — the connectors page says why. The site's own first, then in the page's order.
 */
export function usableConnectors(installed: readonly InstalledConnector[]): InstalledConnector[] {
  return installed
    .filter(
      (c) =>
        (c.alwaysOn || c.health === 'ok' || c.health === 'unknown') &&
        c.tools.some((t) => t.permission !== 'disabled'),
    )
    .sort((a, b) => Number(b.alwaysOn) - Number(a.alwaysOn));
}

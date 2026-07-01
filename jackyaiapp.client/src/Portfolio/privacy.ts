import { createContext, useContext } from 'react';

/** Mask glyph used wherever a real money figure would otherwise appear. */
export const MASK = '••••••';

/**
 * Three privacy modes, cycled by the eye button:
 * - full    — show everything (absolute values, P&L changes, ratios)
 * - changes — show only P&L change amounts; hide absolute values and ratios
 * - hidden  — hide all money (absolute + change); keep ratios/percentages
 */
export type PrivacyMode = 'full' | 'changes' | 'hidden';

export interface PrivacyCtxValue {
  mode: PrivacyMode;
  cycle: () => void;
  /** Absolute values (net worth, market value, price, cost) — shown only in 'full'. */
  value: (formatted: string) => string;
  /** P&L change amounts — hidden only in 'hidden' (visible in 'full' and 'changes'). */
  change: (formatted: string) => string;
  /** Whether ratios/percentages are shown (hidden in 'changes'). */
  showRatio: boolean;
}

export const PrivacyContext = createContext<PrivacyCtxValue>({
  mode: 'full',
  cycle: () => {},
  value: (s) => s,
  change: (s) => s,
  showRatio: true,
});

export function usePrivacy() {
  return useContext(PrivacyContext);
}

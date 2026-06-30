import { createContext, useContext } from 'react';

/** Mask glyph used wherever a real money figure would otherwise appear. */
export const MASK = '••••••';

export interface PrivacyCtxValue {
  /** When true, actual money amounts are hidden; ratios/percentages stay visible. */
  hidden: boolean;
  toggle: () => void;
  /** Returns the formatted string when visible, the mask when hidden. */
  money: (formatted: string) => string;
}

export const PrivacyContext = createContext<PrivacyCtxValue>({
  hidden: true,
  toggle: () => {},
  money: (s) => s,
});

export function usePrivacy() {
  return useContext(PrivacyContext);
}

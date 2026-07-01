import { ReactNode, useCallback, useMemo, useState } from 'react';

import { MASK, PrivacyContext, PrivacyMode } from './privacy';

const STORAGE_KEY = 'portfolio_privacy_mode';
const ORDER: PrivacyMode[] = ['full', 'changes', 'hidden'];

function readMode(): PrivacyMode {
  try {
    const s = localStorage.getItem(STORAGE_KEY);
    if (s === 'full' || s === 'changes' || s === 'hidden') return s;
  } catch {
    /* ignore */
  }
  return 'hidden'; // default: amounts private until revealed
}

export function PrivacyProvider({ children }: { children: ReactNode }) {
  const [mode, setMode] = useState<PrivacyMode>(readMode);

  const cycle = useCallback(() => {
    setMode((m) => {
      const next = ORDER[(ORDER.indexOf(m) + 1) % ORDER.length];
      try {
        localStorage.setItem(STORAGE_KEY, next);
      } catch {
        /* ignore storage failures (private mode, quota) */
      }
      return next;
    });
  }, []);

  const ctx = useMemo(
    () => ({
      mode,
      cycle,
      value: (s: string) => (mode === 'full' ? s : MASK),
      change: (s: string) => (mode === 'hidden' ? MASK : s),
      showRatio: mode !== 'changes',
    }),
    [mode, cycle],
  );

  return <PrivacyContext.Provider value={ctx}>{children}</PrivacyContext.Provider>;
}

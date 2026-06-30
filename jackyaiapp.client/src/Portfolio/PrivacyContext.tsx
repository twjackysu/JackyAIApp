import { ReactNode, useCallback, useState } from 'react';

import { MASK, PrivacyContext } from './privacy';

const STORAGE_KEY = 'portfolio_amount_hidden';

export function PrivacyProvider({ children }: { children: ReactNode }) {
  // Default to hidden — amounts are private until the user explicitly reveals them.
  const [hidden, setHidden] = useState<boolean>(() => {
    try {
      const stored = localStorage.getItem(STORAGE_KEY);
      return stored === null ? true : stored === '1';
    } catch {
      return true;
    }
  });

  const toggle = useCallback(() => {
    setHidden((h) => {
      const next = !h;
      try {
        localStorage.setItem(STORAGE_KEY, next ? '1' : '0');
      } catch {
        /* ignore storage failures (private mode, quota) */
      }
      return next;
    });
  }, []);

  const money = useCallback((formatted: string) => (hidden ? MASK : formatted), [hidden]);

  return (
    <PrivacyContext.Provider value={{ hidden, toggle, money }}>{children}</PrivacyContext.Provider>
  );
}

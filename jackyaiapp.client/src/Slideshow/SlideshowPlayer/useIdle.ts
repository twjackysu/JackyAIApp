import { useCallback, useEffect, useRef, useState } from 'react';

/** Whether the mouse and keyboard have been still for `ms`; any move, key or click wakes it. */
export function useIdle(ms: number) {
  const [idle, setIdle] = useState(false);
  const idleTimer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);

  const wake = useCallback(() => {
    setIdle(false);
    clearTimeout(idleTimer.current);
    idleTimer.current = setTimeout(() => setIdle(true), ms);
  }, [ms]);

  useEffect(() => {
    wake();
    const events: (keyof WindowEventMap)[] = ['mousemove', 'keydown', 'click'];
    events.forEach((e) => window.addEventListener(e, wake));
    return () => {
      clearTimeout(idleTimer.current);
      events.forEach((e) => window.removeEventListener(e, wake));
    };
  }, [wake]);

  return idle;
}

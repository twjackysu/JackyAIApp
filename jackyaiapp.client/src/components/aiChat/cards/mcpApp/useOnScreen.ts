import { useEffect, useRef } from 'react';

import type { RefObject } from 'react';

/**
 * Whether `target` is where the reader can see it: some of it in the viewport (or in the scrolled column holding it)
 * and the tab shown. A ref, not state: the card reads it when an app's call comes in and draws nothing from it, so
 * scrolling past never draws the card again. Where the browser cannot tell (no IntersectionObserver), it counts as seen.
 */
export function useOnScreen(target: RefObject<Element | null>): RefObject<boolean> {
  const inViewRef = useRef(true);
  const seenRef = useRef(true);

  useEffect(() => {
    const el = target.current;
    const update = () => {
      seenRef.current = inViewRef.current && document.visibilityState !== 'hidden';
    };
    document.addEventListener('visibilitychange', update);
    update();
    if (!el || typeof IntersectionObserver === 'undefined') {
      return () => document.removeEventListener('visibilitychange', update);
    }
    const observer = new IntersectionObserver((entries) => {
      const entry = entries[entries.length - 1];
      if (!entry) return;
      inViewRef.current = entry.isIntersecting;
      update();
    });
    observer.observe(el);
    return () => {
      observer.disconnect();
      document.removeEventListener('visibilitychange', update);
    };
  }, [target]);

  return seenRef;
}

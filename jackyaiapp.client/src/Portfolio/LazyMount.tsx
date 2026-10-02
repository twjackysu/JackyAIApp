import Box from '@mui/material/Box';
import { ReactNode, useEffect, useRef, useState } from 'react';

import CenteredSpinner from '@/components/CenteredSpinner';

interface LazyMountProps {
  /** Mounted (and its RTK Query hooks start firing) only once this wrapper scrolls into the
   * viewport — never unmounted again afterwards, so a chart doesn't refetch every time the user
   * scrolls past it twice. */
  children: ReactNode;
  /** Reserved height while unmounted, close to the eventual content's real height so swapping in
   * the chart doesn't shift the page the user is scrolling through. */
  minHeight: number;
}

/**
 * Defers mounting `children` until the wrapper enters (or nears) the viewport. Used to keep the
 * Portfolio page's below-the-fold charts from firing their `useGet*Query` hooks on first paint —
 * see TASK-026 / Issue #401.
 */
export function LazyMount({ children, minHeight }: LazyMountProps) {
  const ref = useRef<HTMLDivElement>(null);
  const [visible, setVisible] = useState(false);

  useEffect(() => {
    if (visible) return;
    const el = ref.current;
    if (!el || typeof IntersectionObserver === 'undefined') {
      setVisible(true);
      return;
    }
    // rootMargin starts the fetch slightly before the section is actually on screen, so the
    // chart has a head start on the network round trip by the time the user scrolls to it.
    const observer = new IntersectionObserver(
      ([entry]) => {
        if (entry.isIntersecting) setVisible(true);
      },
      { rootMargin: '200px 0px' },
    );
    observer.observe(el);
    return () => observer.disconnect();
  }, [visible]);

  if (visible) {
    return <>{children}</>;
  }

  return (
    <Box
      ref={ref}
      sx={{ minHeight, display: 'flex', alignItems: 'center', justifyContent: 'center' }}
    >
      <CenteredSpinner />
    </Box>
  );
}

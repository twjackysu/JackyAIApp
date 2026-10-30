import { useCallback, useState } from 'react';

/** Studio AI's drawer on a wide screen (design/05 §1: 可拉寬): its width as first opened, and how far it goes. */
export const DRAWER_WIDTH = { initial: 480, min: 440, max: 640 } as const;
/** What stays of the page beside the widest drawer, so a narrow window keeps something to drag back from. */
const PAGE_LEFT = 120;
/** How far one arrow key moves the drawer's edge. */
export const DRAWER_WIDTH_STEP = 20;
const STORAGE_KEY = 'studio.drawerWidth';

/** A width the drawer can take in a window `viewport` wide: within its range, and never past the page's last strip. */
export function clampDrawerWidth(width: number, viewport: number): number {
  const max = Math.max(DRAWER_WIDTH.min, Math.min(DRAWER_WIDTH.max, viewport - PAGE_LEFT));
  if (!Number.isFinite(width)) return Math.min(DRAWER_WIDTH.initial, max);
  return Math.round(Math.min(max, Math.max(DRAWER_WIDTH.min, width)));
}

const readWidth = () => {
  try {
    const saved = Number(localStorage.getItem(STORAGE_KEY));
    return saved > 0 ? saved : DRAWER_WIDTH.initial;
  } catch {
    return DRAWER_WIDTH.initial;
  }
};

/** The drawer's width, remembered in this browser (a convenience: none kept, it opens as first). */
export function useDrawerWidth() {
  const [width, setWidth] = useState(readWidth);
  const change = useCallback((next: number) => {
    const clamped = clampDrawerWidth(next, window.innerWidth);
    setWidth(clamped);
    try {
      localStorage.setItem(STORAGE_KEY, String(clamped));
    } catch {
      // Storage unavailable (private window, blocked site data): the width lasts this visit only.
    }
  }, []);
  return [clampDrawerWidth(width, window.innerWidth), change] as const;
}

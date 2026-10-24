import type { DashboardWidget } from '@/apis/dashboardApis/types';

import { GRID_COLS } from '@/DataAnalysis/viz/ResizableWidget';

/** Below this width (a phone) a dashboard is read one chart a row: two side by side are too narrow to read. */
export const STACK_BELOW_PX = 600;

export interface ViewLayoutItem {
  i: string;
  x: number;
  y: number;
  w: number;
  h: number;
  static: true;
}

/**
 * Where each chart is drawn while the dashboard is read (not edited): as saved, or on a narrow screen one per row
 * at full width, in the order the saved layout reads (top to bottom, then left to right), each keeping its height.
 * Only the view: the saved layout is not changed, so the wide screen still shows what its owner arranged.
 */
export function viewLayout(widgets: readonly DashboardWidget[], width: number): ViewLayoutItem[] {
  if (width >= STACK_BELOW_PX)
    return widgets.map((w) => ({ i: w.id, x: w.x, y: w.y, w: w.w, h: w.h, static: true }));
  let y = 0;
  return [...widgets]
    .sort((a, b) => a.y - b.y || a.x - b.x)
    .map((w) => {
      const item: ViewLayoutItem = { i: w.id, x: 0, y, w: GRID_COLS, h: w.h, static: true };
      y += w.h;
      return item;
    });
}

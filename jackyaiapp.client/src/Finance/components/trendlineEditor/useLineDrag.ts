import { useRef, useState, type PointerEvent as ReactPointerEvent } from 'react';

import {
  moveLine,
  moveLineEnd,
  type DrawnLine,
  type LineGeometry,
} from '../../utils/trendlineDrawings';

import { capture, DRAG_SLOP_PX, type Pixel, type Snap } from './overlayGeometry';

import type { ChartProjection } from './chartProjection';
import type { OhlcBar } from '../../utils/trendlines';

interface Drag {
  pointerId: number;
  part: 'start' | 'end' | 'line';
  base: LineGeometry;
  from: Pixel;
  moved: boolean;
}

interface LineDragOptions {
  bars: OhlcBar[];
  projection: ChartProjection;
  measure: (line: DrawnLine) => LineGeometry | null;
  pixelOf: (e: ReactPointerEvent) => Pixel;
  snap: Snap;
  /** Called once, when the drag ends. */
  onChange: (line: DrawnLine) => void;
}

/**
 * Dragging a line's end, or the whole line: `preview` is where it is being dragged to, and the
 * change is handed over once, when it is let go. A step that would take the line off the chart
 * is skipped.
 */
export function useLineDrag({
  bars,
  projection,
  measure,
  pixelOf,
  snap,
  onChange,
}: LineDragOptions) {
  const drag = useRef<Drag | null>(null);
  const edited = useRef<DrawnLine | null>(null);
  const [preview, setPreview] = useState<LineGeometry | null>(null);

  const start = (e: ReactPointerEvent, base: LineGeometry, part: Drag['part']) => {
    if (e.button !== 0) return;
    e.stopPropagation();
    e.preventDefault(); // no text selection while dragging; focus is moved by hand
    capture(e);
    drag.current = { pointerId: e.pointerId, part, base, from: pixelOf(e), moved: false };
  };

  const move = (e: ReactPointerEvent) => {
    const d = drag.current;
    if (!d || d.pointerId !== e.pointerId) return;
    const at = pixelOf(e);
    if (!d.moved && Math.hypot(at.x - d.from.x, at.y - d.from.y) < DRAG_SLOP_PX) return;
    d.moved = true;

    let next: DrawnLine;
    if (d.part === 'line') {
      next = moveLine(
        bars,
        d.base,
        projection.index(at.x) - projection.index(d.from.x),
        projection.price(at.y) - projection.price(d.from.y),
      );
    } else {
      const p = snap(at);
      next = moveLineEnd(bars, d.base, d.part, p.index, p.price);
    }
    const g = measure(next);
    if (g) {
      edited.current = next;
      setPreview(g);
    }
  };

  const end = (e: ReactPointerEvent, commit: boolean) => {
    const d = drag.current;
    if (!d || d.pointerId !== e.pointerId) return;
    if (commit && d.moved && edited.current) onChange(edited.current);
    drag.current = null;
    edited.current = null;
    setPreview(null);
  };

  return { preview, start, move, end };
}

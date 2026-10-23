import { useRef, useState, type PointerEvent as ReactPointerEvent } from 'react';

import { fmtPrice, trendlineColor as lineColor } from '../../utils/trendlineChartOption';
import { createLine, type DrawnLine, type LineGeometry } from '../../utils/trendlineDrawings';

import {
  capture,
  DRAG_SLOP_PX,
  HANDLE_RADIUS,
  newLineId,
  type Pixel,
  type Point,
  type Snap,
} from './overlayGeometry';
import { PriceLabel } from './PriceLabel';

import type { ChartProjection } from './chartProjection';
import type { OhlcBar } from '../../utils/trendlines';

/**
 * Catches clicks on the plot while drawing: the first click (or press) sets the start, the second
 * click (or the release of a drag) the end. Mounted only while drawing, so a cancelled half-drawn
 * line is simply dropped.
 */
export function DrawSurface({
  side,
  bars,
  projection,
  snap,
  pixelOf,
  halo,
  onCreate,
}: {
  side: LineGeometry['side'];
  bars: OhlcBar[];
  projection: ChartProjection;
  snap: Snap;
  pixelOf: (e: ReactPointerEvent) => Pixel;
  halo: string;
  onCreate: (line: DrawnLine) => void;
}) {
  const [draft, setDraft] = useState<{ start: Point; end: Point } | null>(null);
  const [hover, setHover] = useState<Point | null>(null);
  const press = useRef<Pixel | null>(null);
  const { x, y, plot } = projection;

  const finish = (start: Point, end: Point) => {
    const line = createLine(bars, newLineId(), side, start, end);
    if (line) onCreate(line);
    // Both ends on one bar: keep the start and wait for a second point elsewhere.
    else setDraft({ start, end });
  };

  const tip = draft?.end ?? hover;
  const color = lineColor(side);

  return (
    <>
      <rect
        x={plot.left}
        y={plot.top}
        width={plot.right - plot.left}
        height={plot.bottom - plot.top}
        fill="transparent"
        data-testid="trendline-draw-surface"
        style={{ pointerEvents: 'all', cursor: 'crosshair' }}
        onPointerDown={(e) => {
          if (e.button !== 0) return;
          e.stopPropagation();
          capture(e);
          const at = pixelOf(e);
          const p = snap(at);
          if (draft) {
            finish(draft.start, p);
          } else {
            setDraft({ start: p, end: p });
            press.current = at;
          }
        }}
        onPointerMove={(e) => {
          const p = snap(pixelOf(e));
          if (draft) setDraft({ ...draft, end: p });
          else setHover(p);
        }}
        onPointerUp={(e) => {
          const from = press.current;
          press.current = null;
          if (!draft || !from) return;
          const at = pixelOf(e);
          // Released away from where it was pressed: that drag was the whole line.
          if (Math.hypot(at.x - from.x, at.y - from.y) >= DRAG_SLOP_PX * 2)
            finish(draft.start, snap(at));
        }}
      />
      {draft && (
        <line
          x1={x(draft.start.index)}
          y1={y(draft.start.price)}
          x2={x(draft.end.index)}
          y2={y(draft.end.price)}
          stroke={color}
          strokeWidth={2}
          strokeDasharray="6 4"
        />
      )}
      {[draft?.start, tip].map(
        (p, i) =>
          p && (
            <circle
              key={i}
              cx={x(p.index)}
              cy={y(p.price)}
              r={HANDLE_RADIUS - 1}
              fill={color}
              stroke={halo}
              strokeWidth={2}
            />
          ),
      )}
      {tip && (
        <PriceLabel
          at={{ x: x(tip.index), y: y(tip.price) }}
          plotRight={plot.right}
          color={color}
          halo={halo}
          text={`${bars[tip.index].date}  ${fmtPrice(tip.price)}`}
        />
      )}
    </>
  );
}

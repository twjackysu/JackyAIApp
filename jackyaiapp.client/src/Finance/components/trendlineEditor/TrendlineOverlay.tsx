import { useTheme } from '@mui/material/styles';
import {
  useId,
  useRef,
  useState,
  type KeyboardEvent,
  type PointerEvent as ReactPointerEvent,
} from 'react';

import { fmtPrice, trendlineColor as lineColor } from '../../utils/trendlineChartOption';
import {
  createLine,
  moveLine,
  moveLineEnd,
  type DrawnLine,
  type LineGeometry,
} from '../../utils/trendlineDrawings';

import type { ChartProjection } from './chartProjection';
import type { OhlcBar } from '../../utils/trendlines';

/** A point within this many pixels of a bar's high or low snaps to it. */
const MAGNET_PX = 10;
/** A press that moves less than this is a click, not a drag. */
const DRAG_SLOP_PX = 4;
const HANDLE_RADIUS = 6;
/** Fingers need a bigger target than the dot they see. */
const HANDLE_HIT_RADIUS = 18;
const LINE_HIT_WIDTH = 18;
/** Pixels an arrow key moves a line up or down. */
const NUDGE_PX = 4;

const sideName = (side: LineGeometry['side']) => (side === 'support' ? '支撐線' : '壓力線');

interface Point {
  index: number;
  price: number;
}

interface Pixel {
  x: number;
  y: number;
}

type Snap = (at: Pixel) => Point;

const newLineId = () => `user-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 8)}`;

/** Capturing keeps a drag going when the pointer leaves the element; jsdom has no capture. */
function capture(e: ReactPointerEvent<Element>) {
  try {
    e.currentTarget.setPointerCapture(e.pointerId);
  } catch {
    /* not supported — the drag still works while the pointer stays on the chart */
  }
}

export interface TrendlineOverlayProps {
  bars: OhlcBar[];
  lines: LineGeometry[];
  projection: ChartProjection;
  /** Where an edited line would be drawn, or null if it would fall off the chart. */
  measure: (line: DrawnLine) => LineGeometry | null;
  selectedId: string | null;
  /** The kind of line being drawn: clicks on the chart place its ends instead of selecting lines. */
  drawing: LineGeometry['side'] | null;
  onSelect: (id: string | null) => void;
  /** A line was moved or reshaped; called once, when the drag ends. */
  onChange: (line: DrawnLine) => void;
  onDelete: (id: string) => void;
  onCreate: (line: DrawnLine) => void;
}

interface Drag {
  pointerId: number;
  part: 'start' | 'end' | 'line';
  base: LineGeometry;
  from: Pixel;
  moved: boolean;
}

/**
 * The trendlines, drawn in SVG over the candlestick chart so they can be picked up: press a line
 * to select it, then drag its ends or the whole line; arrow keys nudge a focused line and Delete
 * removes it. While `drawing`, two clicks (or one drag) on the chart make a new line of that kind. Ends snap to
 * bars, and to a bar's high or low when close to it.
 */
export function TrendlineOverlay({
  bars,
  lines,
  projection,
  measure,
  selectedId,
  drawing,
  onSelect,
  onChange,
  onDelete,
  onCreate,
}: TrendlineOverlayProps) {
  const theme = useTheme();
  const clipId = `trendline-clip-${useId().replace(/[^\w-]/g, '')}`;
  const svgRef = useRef<SVGSVGElement>(null);
  const drag = useRef<Drag | null>(null);
  const edited = useRef<DrawnLine | null>(null);
  const [preview, setPreview] = useState<LineGeometry | null>(null);
  const { x, y, plot } = projection;

  /** The pointer in the chart's pixels, held inside the plot area. */
  const pixelOf = (e: ReactPointerEvent): Pixel => {
    const box = svgRef.current?.getBoundingClientRect();
    const px = e.clientX - (box?.left ?? 0);
    const py = e.clientY - (box?.top ?? 0);
    return {
      x: Math.min(plot.right, Math.max(plot.left, px)),
      y: Math.min(plot.bottom, Math.max(plot.top, py)),
    };
  };

  const snap: Snap = (at) => {
    const index = Math.min(bars.length - 1, Math.max(0, Math.round(projection.index(at.x))));
    const near = [bars[index].high, bars[index].low].find(
      (p) => Math.abs(projection.y(p) - at.y) <= MAGNET_PX,
    );
    return { index, price: near ?? projection.price(at.y) };
  };

  const startDrag = (e: ReactPointerEvent, base: LineGeometry, part: Drag['part']) => {
    if (e.button !== 0) return;
    e.stopPropagation();
    e.preventDefault(); // no text selection while dragging; focus is moved by hand below
    capture(e);
    drag.current = { pointerId: e.pointerId, part, base, from: pixelOf(e), moved: false };
  };

  const onPointerMove = (e: ReactPointerEvent) => {
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

  const endDrag = (e: ReactPointerEvent, commit: boolean) => {
    const d = drag.current;
    if (!d || d.pointerId !== e.pointerId) return;
    if (commit && d.moved && edited.current) onChange(edited.current);
    drag.current = null;
    edited.current = null;
    setPreview(null);
  };

  const onLineKeyDown = (e: KeyboardEvent<SVGElement>, g: LineGeometry) => {
    const step: Record<string, [number, number]> = {
      ArrowLeft: [-1, 0],
      ArrowRight: [1, 0],
      ArrowUp: [0, 1],
      ArrowDown: [0, -1],
    };
    if (e.key in step) {
      e.preventDefault();
      const [byBars, up] = step[e.key];
      const byPrice = up * Math.abs(projection.price(0) - projection.price(NUDGE_PX));
      onChange(moveLine(bars, g, byBars, byPrice));
    } else if (e.key === 'Delete' || e.key === 'Backspace') {
      e.preventDefault();
      onDelete(g.id);
    }
  };

  const shown = preview ? lines.map((g) => (g.id === preview.id ? preview : g)) : lines;
  const selected = shown.find((g) => g.id === selectedId) ?? null;
  const halo = theme.palette.background.paper;

  return (
    <svg
      ref={svgRef}
      role="group"
      aria-label="趨勢線"
      onPointerMove={onPointerMove}
      onPointerUp={(e) => endDrag(e, true)}
      onPointerCancel={(e) => endDrag(e, false)}
      style={{
        position: 'absolute',
        inset: 0,
        width: '100%',
        height: '100%',
        overflow: 'hidden',
        pointerEvents: 'none',
        // Chrome reads touch-action off the <svg>, not the shapes in it. While a line is
        // selected or being drawn, a finger on a line, a handle or the drawing surface moves it
        // instead of the page; the chart's empty area isn't the svg's (no pointer events), so
        // swiping there still scrolls. Otherwise a swipe across a line scrolls too.
        touchAction: selectedId !== null || drawing !== null ? 'none' : 'auto',
      }}
    >
      <defs>
        <clipPath id={clipId}>
          <rect
            x={plot.left}
            y={plot.top}
            width={plot.right - plot.left}
            height={plot.bottom - plot.top}
          />
        </clipPath>
      </defs>
      <g clipPath={`url(#${clipId})`}>
        {shown.map((g) => {
          const isSelected = g.id === selectedId;
          const ends = { x1: x(g.x1), y1: y(g.y1), x2: x(g.x2), y2: y(g.y2) };
          return (
            <g key={g.id}>
              <line
                {...ends}
                stroke={lineColor(g.side)}
                strokeWidth={(g.isActive ? 2.5 : 1.5) + (isSelected ? 1.5 : 0)}
                strokeOpacity={g.isActive || isSelected ? 1 : 0.7}
                strokeLinecap="round"
              />
              <line
                {...ends}
                data-line-id={g.id}
                role="button"
                tabIndex={0}
                aria-pressed={isSelected}
                aria-label={`${sideName(g.side)}，${bars[Math.ceil(g.x1)].date} 到 ${bars[g.x2].date}，連 ${g.touches} 點${g.isActive ? `，今天約 ${fmtPrice(g.y2)}` : ''}`}
                stroke="transparent"
                strokeWidth={LINE_HIT_WIDTH}
                style={{
                  pointerEvents: drawing !== null ? 'none' : 'stroke',
                  cursor: isSelected ? 'move' : 'pointer',
                  outline: 'none',
                }}
                onFocus={() => onSelect(g.id)}
                onKeyDown={(e) => onLineKeyDown(e, g)}
                onPointerDown={(e) => {
                  onSelect(g.id);
                  e.currentTarget.focus({ preventScroll: true });
                  startDrag(e, g, 'line');
                }}
              />
            </g>
          );
        })}
      </g>

      {selected && drawing === null && (
        <g>
          {(['start', 'end'] as const).map((part) => {
            const cx = x(part === 'start' ? selected.x1 : selected.x2);
            const cy = y(part === 'start' ? selected.y1 : selected.y2);
            return (
              <g key={part}>
                <circle
                  cx={cx}
                  cy={cy}
                  r={HANDLE_RADIUS}
                  fill={lineColor(selected.side)}
                  stroke={halo}
                  strokeWidth={2}
                />
                <circle
                  cx={cx}
                  cy={cy}
                  r={HANDLE_HIT_RADIUS}
                  fill="transparent"
                  data-handle={part}
                  style={{ pointerEvents: 'all', cursor: 'grab' }}
                  onPointerDown={(e) => startDrag(e, selected, part)}
                />
              </g>
            );
          })}
          <PriceLabel
            at={{ x: x(selected.x2), y: y(selected.y2) }}
            plotRight={plot.right}
            color={lineColor(selected.side)}
            halo={halo}
            text={fmtPrice(selected.y2)}
          />
        </g>
      )}

      {drawing !== null && (
        <DrawSurface
          side={drawing}
          bars={bars}
          projection={projection}
          snap={snap}
          pixelOf={pixelOf}
          halo={halo}
          onCreate={onCreate}
        />
      )}
    </svg>
  );
}

function PriceLabel({
  at,
  plotRight,
  color,
  halo,
  text,
}: {
  at: Pixel;
  plotRight: number;
  color: string;
  halo: string;
  text: string;
}) {
  // Near the right edge (where a line in force ends) the label goes to the left of the point.
  const left = at.x > plotRight - 80;
  return (
    <text
      x={at.x + (left ? -10 : 10)}
      y={at.y - 10}
      textAnchor={left ? 'end' : 'start'}
      fill={color}
      stroke={halo}
      strokeWidth={3}
      paintOrder="stroke"
      fontSize={12}
      fontWeight={600}
    >
      {text}
    </text>
  );
}

/**
 * Catches clicks on the plot while drawing: the first click (or press) sets the start, the second
 * click (or the release of a drag) the end. Mounted only while drawing, so a cancelled half-drawn
 * line is simply dropped.
 */
function DrawSurface({
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

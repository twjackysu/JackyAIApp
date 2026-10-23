import { useTheme } from '@mui/material/styles';
import { useId, useRef, type KeyboardEvent, type PointerEvent as ReactPointerEvent } from 'react';

import { fmtPrice, trendlineColor as lineColor } from '../../utils/trendlineChartOption';
import { moveLine, type DrawnLine, type LineGeometry } from '../../utils/trendlineDrawings';

import { DrawSurface } from './DrawSurface';
import { LineHandles } from './LineHandles';
import {
  clampToPlot,
  LINE_HIT_WIDTH,
  lineKeyAction,
  snapToBar,
  type Pixel,
  type Snap,
} from './overlayGeometry';
import { useLineDrag } from './useLineDrag';

import type { ChartProjection } from './chartProjection';
import type { OhlcBar } from '../../utils/trendlines';

const sideName = (side: LineGeometry['side']) => (side === 'support' ? '支撐線' : '壓力線');

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
  const { x, y, plot } = projection;

  /** The pointer in the chart's pixels, held inside the plot area. */
  const pixelOf = (e: ReactPointerEvent): Pixel => {
    const box = svgRef.current?.getBoundingClientRect();
    return clampToPlot(plot, {
      x: e.clientX - (box?.left ?? 0),
      y: e.clientY - (box?.top ?? 0),
    });
  };
  const snap: Snap = (at) => snapToBar(bars, projection, at);
  const drag = useLineDrag({ bars, projection, measure, pixelOf, snap, onChange });

  const onLineKeyDown = (e: KeyboardEvent<SVGElement>, g: LineGeometry) => {
    const action = lineKeyAction(e.key, projection);
    if (!action) return;
    e.preventDefault();
    if (action.kind === 'move') onChange(moveLine(bars, g, action.byBars, action.byPrice));
    else onDelete(g.id);
  };

  const { preview } = drag;
  const shown = preview ? lines.map((g) => (g.id === preview.id ? preview : g)) : lines;
  const selected = shown.find((g) => g.id === selectedId) ?? null;
  const halo = theme.palette.background.paper;

  return (
    <svg
      ref={svgRef}
      role="group"
      aria-label="趨勢線"
      onPointerMove={drag.move}
      onPointerUp={(e) => drag.end(e, true)}
      onPointerCancel={(e) => drag.end(e, false)}
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
                  drag.start(e, g, 'line');
                }}
              />
            </g>
          );
        })}
      </g>

      {selected && drawing === null && (
        <LineHandles
          line={selected}
          projection={projection}
          halo={halo}
          onPressEnd={(e, part) => drag.start(e, selected, part)}
        />
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

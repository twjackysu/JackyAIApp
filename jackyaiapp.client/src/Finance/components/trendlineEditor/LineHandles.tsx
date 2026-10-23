import { type PointerEvent as ReactPointerEvent } from 'react';

import { fmtPrice, trendlineColor as lineColor } from '../../utils/trendlineChartOption';

import { HANDLE_HIT_RADIUS, HANDLE_RADIUS } from './overlayGeometry';
import { PriceLabel } from './PriceLabel';

import type { ChartProjection } from './chartProjection';
import type { LineGeometry } from '../../utils/trendlineDrawings';

/** The selected line's end handles, to drag, and its price at the end. */
export function LineHandles({
  line,
  projection,
  halo,
  onPressEnd,
}: {
  line: LineGeometry;
  projection: ChartProjection;
  halo: string;
  onPressEnd: (e: ReactPointerEvent, part: 'start' | 'end') => void;
}) {
  const { x, y, plot } = projection;
  return (
    <g>
      {(['start', 'end'] as const).map((part) => {
        const cx = x(part === 'start' ? line.x1 : line.x2);
        const cy = y(part === 'start' ? line.y1 : line.y2);
        return (
          <g key={part}>
            <circle
              cx={cx}
              cy={cy}
              r={HANDLE_RADIUS}
              fill={lineColor(line.side)}
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
              onPointerDown={(e) => onPressEnd(e, part)}
            />
          </g>
        );
      })}
      <PriceLabel
        at={{ x: x(line.x2), y: y(line.y2) }}
        plotRight={plot.right}
        color={lineColor(line.side)}
        halo={halo}
        text={fmtPrice(line.y2)}
      />
    </g>
  );
}

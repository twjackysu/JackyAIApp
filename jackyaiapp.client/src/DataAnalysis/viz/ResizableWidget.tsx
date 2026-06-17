import Box from '@mui/material/Box';
import { useEffect, useRef, useState } from 'react';
import { GridLayout, noCompactor } from 'react-grid-layout';

import type { GridSize } from './types';
import type { ReactNode } from 'react';

import 'react-grid-layout/css/styles.css';

interface ResizableWidgetProps {
  /** Initial size in grid units (columns out of 12, rows of GRID_ROW_HEIGHT px). */
  gridDefault: GridSize;
  children: ReactNode;
}

// Grid geometry shared with the (future) dashboard so widgets size consistently.
export const GRID_COLS = 12;
export const GRID_ROW_HEIGHT = 40;

/**
 * Hosts a single visualization in a one-cell react-grid-layout (v2). This is the
 * same grid engine the dashboard will use, so resize behaviour is consistent.
 * Drag is off here (only one widget); the bottom-right handle resizes it.
 *
 * Not persisted — remounting on viz change (via a `key`) resets to default size.
 */
export function ResizableWidget({ gridDefault, children }: ResizableWidgetProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  const [width, setWidth] = useState(0);

  // react-grid-layout's base GridLayout needs an explicit pixel width.
  useEffect(() => {
    const el = containerRef.current;
    if (!el || typeof ResizeObserver === 'undefined') return;
    const measure = () => setWidth(el.clientWidth);
    measure();
    const ro = new ResizeObserver(measure);
    ro.observe(el);
    return () => ro.disconnect();
  }, []);

  const layout = [{ i: 'w', x: 0, y: 0, w: gridDefault.w, h: gridDefault.h, minW: 2, minH: 3 }];

  return (
    <Box
      ref={containerRef}
      sx={{
        '& .react-resizable-handle': {
          position: 'absolute',
          right: 3,
          bottom: 3,
          width: 16,
          height: 16,
          cursor: 'se-resize',
          background:
            'linear-gradient(135deg, transparent 0 45%, rgba(255,255,255,0.4) 45% 55%, transparent 55% 70%, rgba(255,255,255,0.4) 70% 80%, transparent 80%)',
        },
        '& .react-grid-item': {
          border: '1px solid rgba(255,255,255,0.12)',
          borderRadius: '8px',
          overflow: 'hidden',
          background: 'rgba(255,255,255,0.02)',
        },
      }}
    >
      {width > 0 && (
        <GridLayout
          width={width}
          layout={layout}
          gridConfig={{
            cols: GRID_COLS,
            rowHeight: GRID_ROW_HEIGHT,
            margin: [0, 0],
            containerPadding: [0, 0],
          }}
          dragConfig={{ enabled: false }}
          resizeConfig={{ enabled: true }}
          compactor={noCompactor}
          autoSize
        >
          <div key="w">
            <Box sx={{ width: '100%', height: '100%', overflow: 'auto', p: 1 }}>{children}</Box>
          </div>
        </GridLayout>
      )}
    </Box>
  );
}

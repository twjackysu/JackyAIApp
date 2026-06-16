import Box from '@mui/material/Box';
import { Resizable } from 're-resizable';

import type { VizSize } from './types';
import type { ReactNode } from 'react';

interface ResizableWidgetProps {
  defaultSize: VizSize;
  children: ReactNode;
}

// Bottom-right grip so it's obvious the widget can be dragged larger/smaller.
const grip = (
  <Box
    sx={{
      position: 'absolute',
      right: 2,
      bottom: 2,
      width: 14,
      height: 14,
      cursor: 'nwse-resize',
      background:
        'linear-gradient(135deg, transparent 0 45%, rgba(255,255,255,0.35) 45% 55%, transparent 55% 70%, rgba(255,255,255,0.35) 70% 80%, transparent 80%)',
    }}
  />
);

/**
 * Wraps a visualization in a freely resizable box (drag the bottom / right /
 * corner). Initial size comes from the viz's capability. Not persisted —
 * remounting on viz change (via a `key`) resets to the new viz's default.
 */
export function ResizableWidget({ defaultSize, children }: ResizableWidgetProps) {
  return (
    <Resizable
      defaultSize={{ width: defaultSize.w, height: defaultSize.h }}
      minWidth={320}
      minHeight={180}
      maxWidth="100%"
      enable={{ right: true, bottom: true, bottomRight: true }}
      handleComponent={{ bottomRight: grip }}
      style={{
        position: 'relative',
        border: '1px solid rgba(255,255,255,0.12)',
        borderRadius: 8,
        overflow: 'hidden',
        background: 'rgba(255,255,255,0.02)',
      }}
    >
      <Box sx={{ width: '100%', height: '100%', overflow: 'auto', p: 1 }}>{children}</Box>
    </Resizable>
  );
}

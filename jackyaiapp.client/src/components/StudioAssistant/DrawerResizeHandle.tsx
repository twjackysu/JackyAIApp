import Box from '@mui/material/Box';
import { useRef, type KeyboardEvent, type PointerEvent } from 'react';

import { DRAWER_WIDTH, DRAWER_WIDTH_STEP } from './drawerWidth';

import { ACCENT } from '@/components/aiChat/style';

interface Props {
  width: number;
  onChange: (width: number) => void;
}

/**
 * The left edge of Studio AI's drawer on a wide screen, dragged to make it wider or narrower (design/05 §1): a table
 * card or a long answer reads better wide. Arrow keys move it too, Home / End to its ends; a double click puts it back.
 */
export default function DrawerResizeHandle({ width, onChange }: Props) {
  const dragging = useRef(false);

  const down = (e: PointerEvent<HTMLDivElement>) => {
    dragging.current = true;
    e.currentTarget.setPointerCapture(e.pointerId);
    e.preventDefault();
  };
  const move = (e: PointerEvent<HTMLDivElement>) => {
    if (dragging.current) onChange(window.innerWidth - e.clientX);
  };
  const up = (e: PointerEvent<HTMLDivElement>) => {
    dragging.current = false;
    if (e.currentTarget.hasPointerCapture(e.pointerId))
      e.currentTarget.releasePointerCapture(e.pointerId);
  };
  const key = (e: KeyboardEvent<HTMLDivElement>) => {
    // The drawer is on the right: its left edge going left makes it wider.
    const next = {
      ArrowLeft: width + DRAWER_WIDTH_STEP,
      ArrowRight: width - DRAWER_WIDTH_STEP,
      Home: DRAWER_WIDTH.min,
      End: DRAWER_WIDTH.max,
    }[e.key];
    if (next === undefined) return;
    e.preventDefault();
    onChange(next);
  };

  return (
    <Box
      role="separator"
      aria-orientation="vertical"
      aria-label="調整 Studio AI 的寬度"
      aria-valuemin={DRAWER_WIDTH.min}
      aria-valuemax={DRAWER_WIDTH.max}
      aria-valuenow={width}
      tabIndex={0}
      onPointerDown={down}
      onPointerMove={move}
      onPointerUp={up}
      onPointerCancel={up}
      onKeyDown={key}
      onDoubleClick={() => onChange(DRAWER_WIDTH.initial)}
      sx={{
        position: 'absolute',
        left: 0,
        top: 0,
        bottom: 0,
        width: 8,
        zIndex: 2,
        cursor: 'col-resize',
        touchAction: 'none',
        '&::after': {
          content: '""',
          position: 'absolute',
          left: 0,
          top: 0,
          bottom: 0,
          width: 2,
          bgcolor: 'transparent',
          transition: 'background-color 120ms',
        },
        '&:hover::after, &:focus-visible::after': { bgcolor: ACCENT },
        '&:focus-visible': { outline: 'none' },
      }}
    />
  );
}

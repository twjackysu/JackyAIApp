/** The look shared by the top and bottom bars, which fade out while the mouse is still. */
export const chromeSx = (idle: boolean) => ({
  position: 'absolute' as const,
  left: 0,
  right: 0,
  zIndex: 5,
  px: 2,
  py: 1.25,
  display: 'flex',
  gap: 1.5,
  alignItems: 'center',
  flexWrap: 'wrap' as const,
  opacity: idle ? 0 : 1,
  pointerEvents: idle ? ('none' as const) : ('auto' as const),
  transition: 'opacity .25s',
});

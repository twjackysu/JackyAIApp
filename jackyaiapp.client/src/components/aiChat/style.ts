import { alpha, type Theme } from '@mui/material/styles';

/**
 * The AI assistant's own look, so it reads as one thing apart from the market data around it:
 * an emerald-to-indigo accent, used for its outline, its mark and its highlights.
 */
export const ACCENT = '#34d399';
export const ACCENT_GRADIENT = 'linear-gradient(135deg, #34d399 0%, #22d3ee 55%, #818cf8 100%)';

/**
 * The panel's own surface: a shade apart from the page. It is outlined by ACCENT_GRADIENT with
 * the surface in the padding box over the gradient in the border box, so a 1px transparent border
 * lets the gradient show through.
 */
export const panelSurface = (theme: Theme) =>
  theme.palette.mode === 'dark' ? '#14171c' : theme.palette.background.paper;

/** A wash of the accent for highlights (the open conversation, the reader's own messages). */
export const accentWash = (theme: Theme, strength = 0.12) =>
  alpha(theme.palette.mode === 'dark' ? ACCENT : '#059669', strength);

/**
 * For the element holding a message and its MessageActions: the actions fade in on hover and on
 * keyboard focus, and stay visible where the device cannot hover (phones).
 */
export const revealActionsOnHover = {
  '& .message-actions': { opacity: 0, transition: 'opacity 120ms' },
  '&:hover .message-actions, &:focus-within .message-actions': { opacity: 1 },
  // An answer being read aloud keeps its buttons, so 停止朗讀 can be pressed without finding it (D-120).
  '& .message-actions:has([aria-pressed="true"])': { opacity: 1 },
  '@media (hover: none)': { '& .message-actions': { opacity: 1 } },
};

/** Lines inside the panel: quieter than the theme's divider. */
export const hairline = (theme: Theme) =>
  theme.palette.mode === 'dark' ? 'rgba(255,255,255,0.07)' : 'rgba(0,0,0,0.08)';

/** Read by a screen reader, not drawn: a live region's text, a status said only in words. */
export const visuallyHidden = {
  position: 'absolute',
  width: '1px',
  height: '1px',
  margin: '-1px',
  padding: 0,
  border: 0,
  overflow: 'hidden',
  clip: 'rect(0 0 0 0)',
  whiteSpace: 'nowrap',
} as const;

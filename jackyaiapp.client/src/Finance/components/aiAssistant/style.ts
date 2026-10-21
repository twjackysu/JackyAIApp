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

/** Lines inside the panel: quieter than the theme's divider. */
export const hairline = (theme: Theme) =>
  theme.palette.mode === 'dark' ? 'rgba(255,255,255,0.07)' : 'rgba(0,0,0,0.08)';

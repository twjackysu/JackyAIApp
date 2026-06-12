import '@fontsource-variable/inter/index.css';

import { createTheme } from '@mui/material/styles';

// ── Design tokens ─────────────────────────────────────────────────────────────
// Scoped theme for the DataAnalysis demo page only (applied via a nested
// ThemeProvider in DataAnalysis/index.tsx) so the rest of the app keeps the
// global default MUI look.
//
// The aesthetic follows the "Linear / Vercel" formula: a near-black canvas, a
// narrow monochrome surface stack separated by 1px borders (not bright fills),
// quiet gray text, and a SINGLE accent color rationed to primary actions.
// Saturated color is reserved for status (success / warning / error) only.

const FONT_STACK = '"Inter Variable", "Inter", "Segoe UI", "Roboto", "Helvetica", sans-serif';

// Surface stack (canvas → elevated), all within a tight lightness range
const SURFACE_1 = '#17181C'; // cards, panels
const SURFACE_2 = '#1C1D22'; // dialogs, menus
const SURFACE_3 = '#26272D'; // tooltips, highest elevation

// Hairline borders do the separation work instead of fills
const BORDER = 'rgba(255, 255, 255, 0.08)';
const BORDER_HOVER = 'rgba(255, 255, 255, 0.16)';

// Monochrome interaction states
const HOVER_FILL = 'rgba(255, 255, 255, 0.04)';
const SELECTED_FILL = 'rgba(255, 255, 255, 0.08)';

// The one accent — muted indigo, used only for primary actions & focus
const ACCENT = '#5E6AD2';
const ACCENT_HOVER = '#6E79E0';

const TEXT_PRIMARY = '#EEEFF1';
const TEXT_SECONDARY = '#9B9DA5';

export const dataAnalysisTheme = createTheme({
  palette: {
    mode: 'dark',
    primary: { main: ACCENT, light: '#8B93E8', dark: '#4F59B8' },
    secondary: { main: TEXT_SECONDARY },
    success: { main: '#4CC38A' },
    warning: { main: '#E8AE53' },
    error: { main: '#E5534B' },
    divider: BORDER,
    background: { paper: SURFACE_1, default: '#121212' },
    text: { primary: TEXT_PRIMARY, secondary: TEXT_SECONDARY },
    action: {
      hover: HOVER_FILL,
      selected: SELECTED_FILL,
    },
  },
  shape: { borderRadius: 8 },
  typography: {
    fontFamily: FONT_STACK,
    h4: { fontWeight: 600, letterSpacing: '-0.02em' },
    h5: { fontWeight: 600, letterSpacing: '-0.015em' },
    h6: { fontWeight: 600, letterSpacing: '-0.01em' },
    subtitle1: { fontWeight: 500 },
    subtitle2: { fontWeight: 500, letterSpacing: 0 },
    button: { textTransform: 'none', fontWeight: 500, letterSpacing: 0 },
    caption: { letterSpacing: 0 },
  },
  components: {
    // ── Surfaces ──────────────────────────────────────────────────────────
    MuiPaper: {
      styleOverrides: {
        root: { backgroundImage: 'none' },
        outlined: {
          backgroundColor: SURFACE_1,
          borderColor: BORDER,
          borderRadius: 10,
          boxShadow: '0 1px 2px rgba(0, 0, 0, 0.25)',
        },
      },
    },
    MuiCard: {
      styleOverrides: {
        root: { backgroundImage: 'none', borderRadius: 10 },
      },
    },
    MuiDialog: {
      styleOverrides: {
        paper: {
          backgroundColor: SURFACE_2,
          borderRadius: 12,
          border: `1px solid ${BORDER}`,
          boxShadow: '0 16px 48px rgba(0, 0, 0, 0.5)',
        },
      },
    },
    MuiMenu: {
      styleOverrides: {
        paper: {
          backgroundColor: SURFACE_2,
          border: `1px solid ${BORDER}`,
          borderRadius: 10,
          boxShadow: '0 8px 24px rgba(0, 0, 0, 0.4)',
        },
      },
    },

    // ── Buttons & toggles ─────────────────────────────────────────────────
    MuiButton: {
      styleOverrides: {
        root: {
          borderRadius: 8,
          boxShadow: 'none',
          '&.MuiButton-containedPrimary': {
            backgroundColor: ACCENT,
            color: '#FFFFFF',
            '&:hover': { backgroundColor: ACCENT_HOVER, boxShadow: 'none' },
            '&.Mui-disabled': {
              backgroundColor: 'rgba(255, 255, 255, 0.06)',
              color: 'rgba(255, 255, 255, 0.3)',
            },
          },
          '&.MuiButton-outlined': {
            borderColor: BORDER,
            color: TEXT_PRIMARY,
            '&:hover': { borderColor: BORDER_HOVER, backgroundColor: HOVER_FILL },
          },
        },
      },
    },
    MuiIconButton: {
      styleOverrides: {
        root: {
          borderRadius: 8,
          '&:hover': { backgroundColor: HOVER_FILL },
        },
      },
    },
    MuiToggleButtonGroup: {
      styleOverrides: {
        root: {
          backgroundColor: 'rgba(255, 255, 255, 0.03)',
          border: `1px solid ${BORDER}`,
          borderRadius: 8,
          padding: 2,
        },
        grouped: {
          border: 0,
          borderRadius: '6px !important',
          margin: 1,
        },
      },
    },
    MuiToggleButton: {
      styleOverrides: {
        root: {
          textTransform: 'none',
          fontWeight: 500,
          color: TEXT_SECONDARY,
          '&:hover': { backgroundColor: HOVER_FILL },
          '&.Mui-selected': {
            backgroundColor: SELECTED_FILL,
            color: TEXT_PRIMARY,
            '&:hover': { backgroundColor: 'rgba(255, 255, 255, 0.1)' },
          },
        },
      },
    },

    // ── Tabs (quiet pills; the track is styled in index.tsx) ─────────────
    MuiTabs: {
      styleOverrides: {
        root: { minHeight: 40 },
        indicator: { display: 'none' },
      },
    },
    MuiTab: {
      styleOverrides: {
        root: {
          textTransform: 'none',
          fontWeight: 500,
          fontSize: '0.85rem',
          minHeight: 32,
          minWidth: 0,
          borderRadius: 6,
          margin: '4px 2px',
          padding: '4px 12px',
          color: TEXT_SECONDARY,
          transition: 'background-color 100ms ease, color 100ms ease',
          '&:hover': { backgroundColor: HOVER_FILL, color: TEXT_PRIMARY },
          '&.Mui-selected': { color: TEXT_PRIMARY, backgroundColor: SELECTED_FILL },
        },
      },
    },

    // ── Inputs ────────────────────────────────────────────────────────────
    MuiOutlinedInput: {
      styleOverrides: {
        root: {
          borderRadius: 8,
          backgroundColor: 'rgba(255, 255, 255, 0.02)',
          '& .MuiOutlinedInput-notchedOutline': { borderColor: BORDER },
          '&:hover .MuiOutlinedInput-notchedOutline': { borderColor: BORDER_HOVER },
          '&.Mui-focused .MuiOutlinedInput-notchedOutline': {
            borderColor: ACCENT,
            borderWidth: 1,
          },
        },
      },
    },
    MuiInputLabel: {
      styleOverrides: {
        root: { '&.Mui-focused': { color: TEXT_SECONDARY } },
      },
    },

    // ── Data display ──────────────────────────────────────────────────────
    MuiChip: {
      styleOverrides: {
        root: {
          borderRadius: 6,
          fontWeight: 500,
          '&.MuiChip-filledDefault': { backgroundColor: 'rgba(255, 255, 255, 0.07)' },
        },
        outlined: { borderColor: BORDER },
      },
    },
    MuiTableCell: {
      styleOverrides: {
        root: { borderBottom: '1px solid rgba(255, 255, 255, 0.05)' },
        head: {
          fontSize: '0.75rem',
          fontWeight: 500,
          color: TEXT_SECONDARY,
          backgroundColor: 'rgba(255, 255, 255, 0.02)',
          borderBottom: `1px solid ${BORDER}`,
        },
      },
    },
    MuiTableRow: {
      styleOverrides: {
        root: {
          '&.MuiTableRow-hover:hover': { backgroundColor: 'rgba(255, 255, 255, 0.03)' },
        },
      },
    },
    MuiAlert: {
      styleOverrides: {
        root: {
          borderRadius: 10,
          '&.MuiAlert-standardWarning': {
            border: '1px solid rgba(232, 174, 83, 0.2)',
            backgroundColor: 'rgba(232, 174, 83, 0.06)',
          },
          '&.MuiAlert-standardInfo': {
            border: '1px solid rgba(94, 106, 210, 0.25)',
            backgroundColor: 'rgba(94, 106, 210, 0.07)',
          },
          '&.MuiAlert-standardError': {
            border: '1px solid rgba(229, 83, 75, 0.2)',
            backgroundColor: 'rgba(229, 83, 75, 0.06)',
          },
          '&.MuiAlert-standardSuccess': {
            border: '1px solid rgba(76, 195, 138, 0.2)',
            backgroundColor: 'rgba(76, 195, 138, 0.06)',
          },
        },
      },
    },
    MuiTooltip: {
      styleOverrides: {
        tooltip: {
          backgroundColor: SURFACE_3,
          border: `1px solid ${BORDER}`,
          borderRadius: 6,
          fontSize: '0.75rem',
          fontWeight: 400,
        },
      },
    },
  },
});

import { createTheme } from '@mui/material/styles';

// ── Design tokens ─────────────────────────────────────────────────────────────
// Scoped theme for the DataAnalysis demo page only (applied via a nested
// ThemeProvider in DataAnalysis/index.tsx) so the rest of the app keeps the
// global default MUI look.

const BORDER_SUBTLE = 'rgba(148, 163, 184, 0.14)';
const BORDER_HOVER = 'rgba(148, 163, 184, 0.4)';
const SURFACE_TINT = 'rgba(148, 163, 184, 0.05)';
const SURFACE_TRACK = 'rgba(148, 163, 184, 0.08)';
const INDIGO_TINT = 'rgba(129, 140, 248, 0.18)';
const INDIGO_TINT_HOVER = 'rgba(129, 140, 248, 0.28)';

export const dataAnalysisTheme = createTheme({
  palette: {
    mode: 'dark',
    primary: { main: '#818CF8', light: '#A5B4FC', dark: '#6366F1' },
    secondary: { main: '#22D3EE' },
    success: { main: '#34D399' },
    warning: { main: '#FBBF24' },
    error: { main: '#F87171' },
    divider: BORDER_SUBTLE,
    background: { paper: '#161924', default: '#0F1117' },
    text: { primary: '#E2E8F0', secondary: '#94A3B8' },
  },
  shape: { borderRadius: 10 },
  typography: {
    button: { textTransform: 'none', fontWeight: 600 },
    subtitle2: { fontWeight: 600, letterSpacing: '0.02em' },
  },
  components: {
    // ── Surfaces ──────────────────────────────────────────────────────────
    MuiPaper: {
      styleOverrides: {
        root: { backgroundImage: 'none' },
        outlined: {
          backgroundColor: SURFACE_TINT,
          borderColor: BORDER_SUBTLE,
          borderRadius: 14,
        },
      },
    },
    MuiCard: {
      styleOverrides: {
        root: { backgroundImage: 'none', borderRadius: 14 },
      },
    },
    MuiDialog: {
      styleOverrides: {
        paper: { borderRadius: 16, border: `1px solid ${BORDER_SUBTLE}` },
      },
    },

    // ── Buttons & toggles ─────────────────────────────────────────────────
    MuiButton: {
      styleOverrides: {
        root: {
          borderRadius: 10,
          boxShadow: 'none',
          '&.MuiButton-containedPrimary': {
            background: 'linear-gradient(135deg, #6366F1 0%, #8B5CF6 100%)',
            '&:hover': {
              background: 'linear-gradient(135deg, #4F46E5 0%, #7C3AED 100%)',
              boxShadow: '0 4px 18px rgba(99, 102, 241, 0.35)',
            },
            '&.Mui-disabled': {
              background: 'rgba(148, 163, 184, 0.12)',
            },
          },
          '&.MuiButton-outlined': {
            borderColor: BORDER_SUBTLE,
            '&:hover': { borderColor: BORDER_HOVER, backgroundColor: SURFACE_TINT },
          },
        },
      },
    },
    MuiToggleButtonGroup: {
      styleOverrides: {
        root: {
          backgroundColor: SURFACE_TRACK,
          borderRadius: 10,
          padding: 3,
        },
        grouped: {
          border: 0,
          borderRadius: '8px !important',
          margin: 1,
        },
      },
    },
    MuiToggleButton: {
      styleOverrides: {
        root: {
          textTransform: 'none',
          fontWeight: 600,
          color: 'rgba(226, 232, 240, 0.6)',
          '&:hover': { backgroundColor: 'rgba(148, 163, 184, 0.1)' },
          '&.Mui-selected': {
            backgroundColor: INDIGO_TINT,
            color: '#C7D2FE',
            '&:hover': { backgroundColor: INDIGO_TINT_HOVER },
          },
        },
      },
    },

    // ── Tabs (segmented-control look; the track is styled in index.tsx) ──
    MuiTabs: {
      styleOverrides: {
        root: { minHeight: 44 },
        indicator: { display: 'none' },
      },
    },
    MuiTab: {
      styleOverrides: {
        root: {
          textTransform: 'none',
          fontWeight: 600,
          minHeight: 36,
          minWidth: 0,
          borderRadius: 9,
          margin: '4px 2px',
          padding: '6px 14px',
          color: 'rgba(226, 232, 240, 0.55)',
          transition: 'background-color 120ms ease, color 120ms ease',
          '&:hover': { backgroundColor: 'rgba(148, 163, 184, 0.1)', color: '#E2E8F0' },
          '&.Mui-selected': { color: '#C7D2FE', backgroundColor: INDIGO_TINT },
        },
      },
    },

    // ── Inputs ────────────────────────────────────────────────────────────
    MuiOutlinedInput: {
      styleOverrides: {
        root: {
          borderRadius: 10,
          '& .MuiOutlinedInput-notchedOutline': { borderColor: 'rgba(148, 163, 184, 0.25)' },
          '&:hover .MuiOutlinedInput-notchedOutline': { borderColor: BORDER_HOVER },
        },
      },
    },

    // ── Data display ──────────────────────────────────────────────────────
    MuiChip: {
      styleOverrides: {
        root: { borderRadius: 8, fontWeight: 500 },
        outlined: { borderColor: BORDER_SUBTLE },
      },
    },
    MuiTableCell: {
      styleOverrides: {
        root: { borderBottom: `1px solid rgba(148, 163, 184, 0.08)` },
        head: {
          fontSize: '0.72rem',
          textTransform: 'uppercase',
          letterSpacing: '0.07em',
          fontWeight: 700,
          color: '#94A3B8',
          backgroundColor: 'rgba(148, 163, 184, 0.06)',
          borderBottom: `1px solid ${BORDER_SUBTLE}`,
        },
      },
    },
    MuiTableRow: {
      styleOverrides: {
        root: {
          '&.MuiTableRow-hover:hover': { backgroundColor: 'rgba(129, 140, 248, 0.06)' },
        },
      },
    },
    MuiAlert: {
      styleOverrides: {
        root: {
          borderRadius: 12,
          '&.MuiAlert-standardWarning': {
            border: '1px solid rgba(251, 191, 36, 0.25)',
            backgroundColor: 'rgba(251, 191, 36, 0.08)',
          },
          '&.MuiAlert-standardInfo': {
            border: '1px solid rgba(34, 211, 238, 0.25)',
            backgroundColor: 'rgba(34, 211, 238, 0.07)',
          },
          '&.MuiAlert-standardError': {
            border: '1px solid rgba(248, 113, 113, 0.25)',
            backgroundColor: 'rgba(248, 113, 113, 0.08)',
          },
          '&.MuiAlert-standardSuccess': {
            border: '1px solid rgba(52, 211, 153, 0.25)',
            backgroundColor: 'rgba(52, 211, 153, 0.08)',
          },
        },
      },
    },
    MuiTooltip: {
      styleOverrides: {
        tooltip: { borderRadius: 8, fontSize: '0.75rem' },
      },
    },
  },
});

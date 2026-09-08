import Box from '@mui/material/Box';
import CircularProgress from '@mui/material/CircularProgress';

import type { SxProps, Theme } from '@mui/material/styles';

interface CenteredSpinnerProps {
  /** Spinner diameter in px. Omitted means MUI's default (40px). */
  size?: number;
  /**
   * Layout for the centering wrapper. Every caller wants the spinner horizontally centred; what
   * actually differs per surface is the room around it (`py: 6` under a chart, `mt: 8` on a full
   * page, `minHeight` for a panel that must not collapse), so that stays a per-call-site choice.
   */
  sx?: SxProps<Theme>;
}

/**
 * The "this panel is waiting for its data" spinner: a horizontally centred `<CircularProgress>`.
 * Two dozen places had grown their own copy of the same three lines of markup, which is how they
 * ended up disagreeing on details (`alignItems` set in some, absent in others) that were never
 * meant to differ.
 */
function CenteredSpinner({ size, sx }: CenteredSpinnerProps) {
  return (
    <Box sx={[{ display: 'flex', justifyContent: 'center' }, ...(Array.isArray(sx) ? sx : [sx])]}>
      <CircularProgress size={size} />
    </Box>
  );
}

export default CenteredSpinner;

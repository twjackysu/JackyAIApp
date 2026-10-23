import AutoAwesomeIcon from '@mui/icons-material/AutoAwesome';
import Box from '@mui/material/Box';

import { ACCENT_GRADIENT } from '../style';

/** The assistant's mark: a sparkle on the accent gradient, used wherever the assistant speaks. */
function AssistantMark({ size = 28 }: { size?: number }) {
  return (
    <Box
      aria-hidden
      sx={{
        width: size,
        height: size,
        flexShrink: 0,
        borderRadius: size >= 40 ? 3 : 2,
        display: 'grid',
        placeItems: 'center',
        backgroundImage: ACCENT_GRADIENT,
        color: '#0b1210',
        boxShadow: '0 4px 14px -4px rgba(52, 211, 153, 0.55)',
      }}
    >
      <AutoAwesomeIcon sx={{ fontSize: Math.round(size * 0.58) }} />
    </Box>
  );
}

export default AssistantMark;

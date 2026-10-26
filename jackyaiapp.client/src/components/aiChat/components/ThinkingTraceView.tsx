import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import PsychologyIcon from '@mui/icons-material/PsychologyOutlined';
import { Box, ButtonBase, Collapse, Typography } from '@mui/material';
import { useTheme } from '@mui/material/styles';
import { useEffect, useId, useState } from 'react';

import { hairline } from '../style';
import {
  isThinking,
  latestThinkingLine,
  thinkingLabel,
  type ThinkingTrace,
} from '../utils/thinking';

/**
 * The model's thinking above its answer (D-136), folded: 「思考中… 3 秒」 with its latest line while it thinks,
 * 「已思考 8 秒」 once the answer or a tool took over; a press unfolds the whole summary. Plain text only: the
 * summary can repeat what a tool read (someone else's words), so nothing in it is a link or markup.
 */
export default function ThinkingTraceView({ thinking }: { thinking: ThinkingTrace }) {
  const theme = useTheme();
  const [open, setOpen] = useState(false);
  const [now, setNow] = useState(() => Date.now());
  const live = isThinking(thinking);
  const bodyId = useId();

  // The seconds count on while it thinks; once it stops, the label is fixed.
  useEffect(() => {
    if (!live) return;
    setNow(Date.now());
    const timer = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(timer);
  }, [live]);

  const preview = live && !open ? latestThinkingLine(thinking.text) : '';

  return (
    <Box data-testid="thinking-trace" sx={{ mb: 1 }}>
      <ButtonBase
        onClick={() => setOpen((o) => !o)}
        aria-expanded={open}
        aria-controls={open ? bodyId : undefined}
        sx={{
          gap: 0.75,
          px: 0.5,
          py: 0.25,
          borderRadius: 1,
          color: 'text.secondary',
          '&:hover': { color: 'text.primary' },
        }}
      >
        <PsychologyIcon
          sx={{
            fontSize: 16,
            ...(live && {
              animation: 'thinkingPulse 1.6s ease-in-out infinite',
              '@keyframes thinkingPulse': { '0%, 100%': { opacity: 0.45 }, '50%': { opacity: 1 } },
            }),
          }}
        />
        <Typography variant="caption" sx={{ fontWeight: 500 }}>
          {thinkingLabel(thinking, now)}
        </Typography>
        <ExpandMoreIcon
          sx={{
            fontSize: 16,
            transition: 'transform 150ms',
            transform: open ? 'rotate(180deg)' : 'none',
          }}
        />
      </ButtonBase>
      {preview && (
        <Typography
          variant="caption"
          sx={{ display: 'block', pl: 3.25, color: 'text.disabled', fontStyle: 'italic' }}
          noWrap
        >
          {preview}
        </Typography>
      )}
      <Collapse in={open} unmountOnExit>
        <Typography
          id={bodyId}
          component="div"
          variant="body2"
          sx={{
            mt: 0.5,
            ml: 1.25,
            pl: 1.5,
            borderLeft: `2px solid ${hairline(theme)}`,
            color: 'text.secondary',
            whiteSpace: 'pre-wrap',
            overflowWrap: 'anywhere',
            maxHeight: 280,
            overflowY: 'auto',
          }}
        >
          {thinking.text}
        </Typography>
      </Collapse>
    </Box>
  );
}

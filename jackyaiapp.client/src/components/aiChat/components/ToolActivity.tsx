import CheckIcon from '@mui/icons-material/Check';
import ErrorOutlineIcon from '@mui/icons-material/ErrorOutlineOutlined';
import { Box, CircularProgress, Typography } from '@mui/material';
import { useTheme } from '@mui/material/styles';

import { accentWash, hairline, visuallyHidden } from '../style';
import { ToolStep, ToolStepStatus } from '../utils/toolActivity';

const STATUS_TEXT: Record<ToolStepStatus, string> = {
  running: '查詢中',
  done: '完成',
  failed: '失敗',
};

/**
 * The tools an answer used, above its text: one chip per call, spinning while it runs, then a
 * check or a warning. So the reader sees the assistant looking things up rather than a pause, and
 * which data the answer rests on.
 */
export default function ToolActivity({ steps }: { steps: readonly ToolStep[] }) {
  const theme = useTheme();
  if (steps.length === 0) return null;

  return (
    <Box
      component="ul"
      data-testid="tool-activity"
      aria-label="使用的工具"
      sx={{ listStyle: 'none', m: 0, mb: 1, p: 0, display: 'flex', flexWrap: 'wrap', gap: 0.75 }}
    >
      {steps.map((step) => (
        <Box
          component="li"
          key={step.key}
          sx={{
            display: 'inline-flex',
            alignItems: 'center',
            gap: 0.75,
            px: 1.25,
            py: 0.375,
            borderRadius: 999,
            border: `1px solid ${hairline(theme)}`,
            bgcolor: step.status === 'done' ? accentWash(theme, 0.08) : 'transparent',
          }}
        >
          {step.status === 'running' ? (
            <CircularProgress size={12} thickness={5} />
          ) : step.status === 'done' ? (
            <CheckIcon sx={{ fontSize: 14, color: 'success.main' }} />
          ) : (
            <ErrorOutlineIcon sx={{ fontSize: 14, color: 'warning.main' }} />
          )}
          <Typography variant="caption" color="text.secondary" noWrap sx={{ maxWidth: 220 }}>
            {step.label}
          </Typography>
          {/* In the item's text: many screen readers read a list item's text and skip an aria-label on it. */}
          <Box component="span" sx={visuallyHidden}>
            （{STATUS_TEXT[step.status]}）
          </Box>
        </Box>
      ))}
    </Box>
  );
}

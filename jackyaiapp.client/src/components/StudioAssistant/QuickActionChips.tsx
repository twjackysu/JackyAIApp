import ArrowOutwardIcon from '@mui/icons-material/ArrowOutward';
import DashboardIcon from '@mui/icons-material/DashboardOutlined';
import MenuBookIcon from '@mui/icons-material/MenuBookOutlined';
import { Box, ButtonBase, Typography } from '@mui/material';
import { useTheme } from '@mui/material/styles';

import type { QuickAction } from './quickActions';

import { ACCENT, accentWash, hairline } from '@/components/aiChat/style';

const ICONS = { page: ArrowOutwardIcon, dashboard: DashboardIcon, word: MenuBookIcon } as const;

/**
 * What Studio AI can do at once for what is being typed (`quickActionsFor`), over the message box: a
 * press goes there without asking the AI. Nothing is shown when nothing fits.
 */
export default function QuickActionChips({
  actions,
  onPick,
}: {
  actions: readonly QuickAction[];
  onPick: (action: QuickAction) => void;
}) {
  const theme = useTheme();
  // Always there, so a screen reader announces the actions as they appear while the reader types.
  return (
    <Box aria-live="polite">
      {actions.length > 0 && (
        <Box
          role="group"
          aria-label="直接前往（不用問 AI）"
          data-testid="quick-actions"
          sx={{
            display: 'flex',
            flexWrap: 'wrap',
            alignItems: 'center',
            gap: 0.75,
            px: { xs: 1.5, sm: 3 },
            pt: 0.5,
          }}
        >
          <Typography component="span" variant="caption" color="text.secondary" sx={{ mr: 0.25 }}>
            不用問 AI，直接：
          </Typography>
          {actions.map((action) => {
            const Icon = ICONS[action.kind];
            return (
              <ButtonBase
                key={action.key}
                onClick={() => onPick(action)}
                sx={{
                  gap: 0.5,
                  px: 1.25,
                  py: 0.5,
                  borderRadius: 99,
                  border: `1px solid ${hairline(theme)}`,
                  color: 'text.primary',
                  bgcolor: accentWash(theme, 0.04),
                  transition: 'border-color 120ms, background-color 120ms',
                  '&:hover, &.Mui-focusVisible': {
                    borderColor: ACCENT,
                    bgcolor: accentWash(theme, 0.12),
                  },
                }}
              >
                <Icon sx={{ fontSize: 15, color: ACCENT }} />
                <Typography component="span" variant="body2" sx={{ fontSize: 13 }}>
                  {action.label}
                </Typography>
              </ButtonBase>
            );
          })}
        </Box>
      )}
    </Box>
  );
}

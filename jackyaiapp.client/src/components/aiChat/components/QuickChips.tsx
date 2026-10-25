import ArrowOutwardIcon from '@mui/icons-material/ArrowOutward';
import DashboardIcon from '@mui/icons-material/DashboardOutlined';
import MenuBookIcon from '@mui/icons-material/MenuBookOutlined';
import SendIcon from '@mui/icons-material/SendOutlined';
import { Box, ButtonBase, Typography } from '@mui/material';
import { useTheme } from '@mui/material/styles';

import { ACCENT, accentWash, hairline } from '../style';

const ICONS = {
  page: ArrowOutwardIcon,
  dashboard: DashboardIcon,
  word: MenuBookIcon,
  command: SendIcon,
} as const;

/**
 * A chip over the message box: a ready-made request (`command`, a `/` draft, sent to the AI; every assistant's),
 * or one of Studio AI's quick actions, which go somewhere without the AI (a page, a dashboard, a word).
 */
export interface QuickChipItem {
  key: string;
  label: string;
  kind: keyof typeof ICONS;
}

/**
 * What the assistant can do at once for what is being typed, over the message box (the panel's
 * `inputAccessory`): Studio AI's quick actions (`quickActionsFor`), which a press follows without asking the AI;
 * or, for a draft starting with `/`, the assistant's ready-made requests (`matchSlashCommands`), which a press
 * sends. Nothing is shown when nothing fits.
 */
export default function QuickChips<T extends QuickChipItem>({
  actions,
  onPick,
  enterPicksFirst = false,
}: {
  actions: readonly T[];
  onPick: (action: T) => void;
  /** Enter in the box picks the first ready-made request (the owner's `onSendDraft`, D-115): it is marked so. */
  enterPicksFirst?: boolean;
}) {
  const theme = useTheme();
  const commands = actions.some((a) => a.kind === 'command');
  // Always there, so a screen reader announces the actions as they appear while the reader types.
  return (
    <Box aria-live="polite">
      {actions.length > 0 && (
        <Box
          role="group"
          aria-label={commands ? '現成指令' : '直接前往（不用問 AI）'}
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
            {commands ? '現成指令：' : '不用問 AI，直接：'}
          </Typography>
          {actions.map((action, i) => {
            const Icon = ICONS[action.kind];
            // What Enter sends: outlined, and on a keyboard's screen marked ↵ (a pseudo-element, so the name stays the label).
            const onEnter = enterPicksFirst && commands && i === 0;
            return (
              <ButtonBase
                key={action.key}
                onClick={() => onPick(action)}
                aria-keyshortcuts={onEnter ? 'Enter' : undefined}
                data-enter={onEnter || undefined}
                sx={{
                  ...(onEnter && {
                    '&::after': {
                      content: '"↵"',
                      display: { xs: 'none', sm: 'inline' },
                      ml: 0.25,
                      fontSize: 12,
                      color: 'text.secondary',
                    },
                  }),
                  gap: 0.5,
                  px: 1.25,
                  py: 0.5,
                  borderRadius: 99,
                  border: `1px solid ${onEnter ? ACCENT : hairline(theme)}`,
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

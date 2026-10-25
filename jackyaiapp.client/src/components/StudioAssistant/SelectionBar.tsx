import AutoAwesomeIcon from '@mui/icons-material/AutoAwesome';
import { Box, ButtonBase, Paper, Typography } from '@mui/material';
import { useTheme } from '@mui/material/styles';
import { useCallback, useEffect, useState } from 'react';

import { readSelection, SELECTION_SETTLE_MS, type Selected } from './readSelection';
import {
  selectionActionsFor,
  type SelectionAction,
  type SelectionActionKind,
} from './selectionActions';

import { ACCENT, ACCENT_GRADIENT, accentWash, hairline } from '@/components/aiChat/style';

/**
 * The bar over what the reader selected on a page or in Studio AI's answers (`selectionActions`): explain,
 * translate or ask about it,
 * or for one English word open its dictionary page or add it to the repository. Shown once the selection
 * settles, gone when it is cleared, the page scrolls, or Escape; pressing an action keeps the selection
 * until it has been handed over, then clears it.
 */
export default function SelectionBar({
  enabled,
  onAction,
}: {
  enabled: boolean;
  /** `inStudio`: selected in one of Studio AI's answers rather than on the page. */
  onAction: (kind: SelectionActionKind, text: string, inStudio: boolean) => void;
}) {
  const theme = useTheme();
  const [selected, setSelected] = useState<Selected | null>(null);

  useEffect(() => {
    if (!enabled) {
      setSelected(null);
      return;
    }
    let timer: ReturnType<typeof setTimeout> | undefined;
    const onChange = () => {
      clearTimeout(timer);
      setSelected(null);
      timer = setTimeout(() => setSelected(readSelection()), SELECTION_SETTLE_MS);
    };
    const hide = () => setSelected(null);
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') hide();
    };
    document.addEventListener('selectionchange', onChange);
    window.addEventListener('scroll', hide, true);
    window.addEventListener('resize', hide);
    window.addEventListener('keydown', onKey);
    return () => {
      clearTimeout(timer);
      document.removeEventListener('selectionchange', onChange);
      window.removeEventListener('scroll', hide, true);
      window.removeEventListener('resize', hide);
      window.removeEventListener('keydown', onKey);
    };
  }, [enabled]);

  const pick = useCallback(
    (action: SelectionAction) => {
      if (!selected) return;
      onAction(action.kind, selected.text, selected.inStudio);
      setSelected(null);
      window.getSelection?.()?.removeAllRanges();
    },
    [selected, onAction],
  );

  if (!enabled || !selected) return null;
  const actions = selectionActionsFor(selected.text);
  return (
    <Paper
      role="toolbar"
      aria-label="用 Studio AI 處理選取的文字"
      data-testid="selection-bar"
      elevation={6}
      // A press must not move the focus or clear the selection before it is read.
      onMouseDown={(e) => e.preventDefault()}
      sx={{
        position: 'fixed',
        left: selected.left,
        top: selected.top,
        transform: `translate(-50%, ${selected.below ? '0' : '-100%'})`,
        zIndex: theme.zIndex.tooltip,
        display: 'flex',
        alignItems: 'center',
        gap: 0.25,
        p: 0.5,
        borderRadius: 99,
        border: `1px solid ${hairline(theme)}`,
        whiteSpace: 'nowrap',
      }}
    >
      <Box
        aria-hidden
        sx={{
          display: 'grid',
          placeItems: 'center',
          width: 26,
          height: 26,
          borderRadius: '50%',
          backgroundImage: ACCENT_GRADIENT,
          color: '#fff',
          mx: 0.25,
        }}
      >
        <AutoAwesomeIcon sx={{ fontSize: 15 }} />
      </Box>
      {actions.map((action) => (
        <ButtonBase
          key={action.kind}
          onClick={() => pick(action)}
          sx={{
            px: 1.25,
            py: 0.5,
            borderRadius: 99,
            '&:hover, &.Mui-focusVisible': { bgcolor: accentWash(theme, 0.14), color: ACCENT },
          }}
        >
          <Typography component="span" variant="body2" sx={{ fontSize: 13, fontWeight: 500 }}>
            {action.label}
          </Typography>
        </ButtonBase>
      ))}
    </Paper>
  );
}

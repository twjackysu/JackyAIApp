import AutoAwesomeIcon from '@mui/icons-material/AutoAwesome';
import { Box, ButtonBase, Paper, Typography } from '@mui/material';
import { useTheme } from '@mui/material/styles';
import { useCallback, useEffect, useRef, useState } from 'react';

import { readSelection, SELECTION_SETTLE_MS, type Selected } from './readSelection';
import {
  selectionActionsFor,
  type SelectionAction,
  type SelectionActionKind,
} from './selectionActions';

import { ACCENT, ACCENT_GRADIENT, accentWash, hairline } from '@/components/aiChat/style';

/**
 * The bar over what the reader selected on a page or in its assistant's answers (`selectionActions`): explain,
 * translate or ask about it, or for one English word open its dictionary page or (Studio AI, which has the tool)
 * add it to the repository. Shown once the selection
 * settles, gone when it is cleared, the page scrolls, or Escape; pressing an action keeps the selection
 * until it has been handed over, then clears it.
 */
export default function SelectionBar({
  enabled,
  onAction,
  assistantName = 'Studio AI',
  canSaveWord = true,
}: {
  enabled: boolean;
  /** Who the bar hands the text to, for its label. */
  assistantName?: string;
  /** The assistant can add a word to the repository (Studio AI's `add_word_to_repository`). */
  canSaveWord?: boolean;
  /** `inAssistant`: selected in one of the assistant's answers rather than on the page. */
  onAction: (kind: SelectionActionKind, text: string, inAssistant: boolean) => void;
}) {
  const theme = useTheme();
  const [selected, setSelected] = useState<Selected | null>(null);
  // The bar is shown (for Escape) and being pressed: a touch browser may clear the selection on the tap,
  // before the click, and the bar must still be there for it.
  const shownRef = useRef(false);
  const pressing = useRef(false);
  useEffect(() => {
    shownRef.current = selected !== null;
  }, [selected]);

  useEffect(() => {
    if (!enabled) {
      setSelected(null);
      return;
    }
    let timer: ReturnType<typeof setTimeout> | undefined;
    const onChange = () => {
      if (pressing.current) return;
      clearTimeout(timer);
      setSelected(null);
      timer = setTimeout(() => setSelected(readSelection()), SELECTION_SETTLE_MS);
    };
    // Scrolling (the page, or the assistant's answer growing under the selection) moves the bar with it.
    const follow = () => {
      if (!pressing.current && shownRef.current) setSelected(readSelection());
    };
    const released = () => {
      // After the click that the press makes, if any.
      setTimeout(() => (pressing.current = false), 0);
    };
    // Escape takes the bar away, not the drawer the selection is in (the capture runs before MUI's).
    const onKey = (e: KeyboardEvent) => {
      if (e.key !== 'Escape' || !shownRef.current) return;
      e.stopPropagation();
      setSelected(null);
    };
    document.addEventListener('selectionchange', onChange);
    window.addEventListener('scroll', follow, true);
    window.addEventListener('resize', follow);
    window.addEventListener('pointerup', released);
    window.addEventListener('pointercancel', released);
    document.addEventListener('keydown', onKey, true);
    return () => {
      clearTimeout(timer);
      document.removeEventListener('selectionchange', onChange);
      window.removeEventListener('scroll', follow, true);
      window.removeEventListener('resize', follow);
      window.removeEventListener('pointerup', released);
      window.removeEventListener('pointercancel', released);
      document.removeEventListener('keydown', onKey, true);
    };
  }, [enabled]);

  const pick = useCallback(
    (action: SelectionAction) => {
      if (!selected) return;
      onAction(action.kind, selected.text, selected.inAssistant);
      pressing.current = false;
      setSelected(null);
      window.getSelection?.()?.removeAllRanges();
    },
    [selected, onAction],
  );

  if (!enabled || !selected) return null;
  const actions = selectionActionsFor(selected.text, { canSaveWord });
  return (
    <Paper
      role="toolbar"
      aria-label={`用 ${assistantName} 處理選取的文字`}
      data-testid="selection-bar"
      elevation={6}
      // A press must not move the focus or clear the selection before it is read.
      onMouseDown={(e) => e.preventDefault()}
      onPointerDown={() => (pressing.current = true)}
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

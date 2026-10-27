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
import { isSelectionBarShortcut, SELECTION_BAR_SHORTCUT, selectionHint } from './selectionShortcut';

import {
  ACCENT,
  ACCENT_GRADIENT,
  accentWash,
  hairline,
  visuallyHidden,
} from '@/components/aiChat/style';

/**
 * The bar over what the reader selected on a page or in its assistant's answers (`selectionActions`): explain,
 * translate or ask about it, or for one English word open its dictionary page or (Studio AI, which has the tool)
 * add it to the repository. Shown once the selection
 * settles, gone when it is cleared, the page scrolls, or Escape; pressing an action keeps the selection
 * until it has been handed over, then clears it. From the keyboard, Alt+A goes into it (the arrows move between its
 * buttons, Escape goes back to where the focus was), and a hidden status says so when a selection settles (D-188).
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
  // The focus is in the bar (Alt+A): a selection change meanwhile is not the reader's, and Escape gives the focus back.
  const barRef = useRef<HTMLDivElement>(null);
  const focusInBar = useRef(false);
  const returnFocus = useRef<HTMLElement | null>(null);
  useEffect(() => {
    shownRef.current = selected !== null;
    // Gone with the focus in it (the selection left the screen): a later selection is the reader's again.
    if (!selected) focusInBar.current = false;
  }, [selected]);

  useEffect(() => {
    if (!enabled) {
      setSelected(null);
      return;
    }
    let timer: ReturnType<typeof setTimeout> | undefined;
    const onChange = () => {
      if (pressing.current || focusInBar.current) return;
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
      if (!shownRef.current) return;
      if (isSelectionBarShortcut(e)) {
        e.preventDefault();
        e.stopPropagation();
        const at = document.activeElement;
        // Pressed again from inside the bar: where to go back to is still where the reader was before.
        if (barRef.current?.contains(at)) return;
        returnFocus.current = at instanceof HTMLElement && at !== document.body ? at : null;
        barRef.current?.querySelector<HTMLElement>('button')?.focus();
        return;
      }
      if (e.key !== 'Escape') return;
      e.stopPropagation();
      if (focusInBar.current) {
        focusInBar.current = false;
        returnFocus.current?.focus();
      }
      returnFocus.current = null;
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
      // Picked from the keyboard: the focus goes back where it was before the bar goes (an assistant that opens for
      // the action takes it from there).
      const back = focusInBar.current ? returnFocus.current : null;
      focusInBar.current = false;
      returnFocus.current = null;
      back?.focus();
      onAction(action.kind, selected.text, selected.inAssistant);
      pressing.current = false;
      setSelected(null);
      window.getSelection?.()?.removeAllRanges();
    },
    [selected, onAction],
  );

  // Left/Right (and Home/End) move between the buttons, as in any toolbar.
  const moveFocus = (e: React.KeyboardEvent) => {
    const all = [...(barRef.current?.querySelectorAll<HTMLElement>('button') ?? [])];
    const at = all.indexOf(document.activeElement as HTMLElement);
    if (at < 0) return;
    const to =
      e.key === 'ArrowRight'
        ? (at + 1) % all.length
        : e.key === 'ArrowLeft'
          ? (at - 1 + all.length) % all.length
          : e.key === 'Home'
            ? 0
            : e.key === 'End'
              ? all.length - 1
              : -1;
    if (to < 0) return;
    e.preventDefault();
    all[to].focus();
  };

  if (!enabled) return null;
  // Always there while enabled, so the words are read when they come (a live region mounted with its text often is not).
  const hint = (
    <Box component="span" role="status" sx={visuallyHidden}>
      {selected ? selectionHint(assistantName) : ''}
    </Box>
  );
  if (!selected) return hint;
  const actions = selectionActionsFor(selected.text, { canSaveWord });
  return (
    <>
      {hint}
      <Paper
        ref={barRef}
        role="toolbar"
        aria-label={`用 ${assistantName} 處理選取的文字`}
        aria-keyshortcuts={SELECTION_BAR_SHORTCUT}
        data-testid="selection-bar"
        elevation={6}
        onFocus={() => (focusInBar.current = true)}
        onBlur={(e) => {
          if (!barRef.current?.contains(e.relatedTarget as Node | null)) focusInBar.current = false;
        }}
        onKeyDown={moveFocus}
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
        {actions.map((action, i) => (
          <ButtonBase
            key={action.kind}
            // One Tab stop for the bar, as in any toolbar: the arrows move along it.
            tabIndex={i === 0 ? 0 : -1}
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
    </>
  );
}

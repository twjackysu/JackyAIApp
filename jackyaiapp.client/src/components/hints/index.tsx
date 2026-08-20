import HelpOutlineIcon from '@mui/icons-material/HelpOutlineOutlined';
import Box from '@mui/material/Box';
import IconButton from '@mui/material/IconButton';
import Popover from '@mui/material/Popover';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';
import useMediaQuery from '@mui/material/useMediaQuery';
import { cloneElement, isValidElement, useState } from 'react';

import type { TooltipProps } from '@mui/material/Tooltip';
import type { KeyboardEvent, MouseEvent, ReactElement, ReactNode } from 'react';

/**
 * Hover is a mouse-only affordance: on a touch screen a MUI <Tooltip> only opens after a
 * ~700ms long press, which nobody discovers and which fights the browser's own long-press
 * menu. Everything a tooltip used to carry in this app falls into one of two buckets, and
 * each gets its own component here:
 *
 *  - `ActionTooltip` labels an icon-only control ("Refresh", "Close"). On touch the label is
 *    redundant — tapping just performs the action — so it renders the child untouched and
 *    only contributes an `aria-label` for assistive tech.
 *  - `InfoPopover` / `ClickPopover` carry explanation that exists nowhere else on screen.
 *    They open a Popover on click, so mouse, keyboard and touch users all reach the content.
 */

/** True on devices with a real pointer that can hover (desktop mice, trackpads). */
function useCanHover(): boolean {
  return useMediaQuery('(hover: hover) and (pointer: fine)', { noSsr: true });
}

type LabelledChild = ReactElement<{ 'aria-label'?: string }>;

interface ActionTooltipProps extends Omit<TooltipProps, 'children' | 'title'> {
  /** Short name of the control. Only strings can survive as an aria-label on touch. */
  title: string;
  children: LabelledChild;
}

/**
 * Hover label for an icon-only control. Renders a normal tooltip where hover exists and
 * steps out of the way (leaving an aria-label behind) where it does not.
 */
export function ActionTooltip({ title, children, ...tooltipProps }: ActionTooltipProps) {
  const canHover = useCanHover();

  if (!canHover) {
    if (!title) return children;
    return cloneElement(children, {
      'aria-label': children.props['aria-label'] ?? title,
    });
  }

  return (
    <Tooltip title={title} {...tooltipProps}>
      {children}
    </Tooltip>
  );
}

const POPOVER_PAPER_SX = {
  p: 1.5,
  maxWidth: 320,
  whiteSpace: 'pre-line' as const,
};

function PopoverBody({ content }: { content: ReactNode }) {
  if (typeof content === 'string') {
    return <Typography variant="body2">{content}</Typography>;
  }
  return <Box sx={{ fontSize: '0.875rem' }}>{content}</Box>;
}

interface InfoPopoverProps {
  /** The explanation itself. Strings are rendered as body text, nodes as-is. */
  content: ReactNode;
  /** Accessible name for the trigger button. */
  ariaLabel?: string;
  /** Icon font size in px; matches the surrounding text by default. */
  iconSize?: number;
  /** Forwarded to the trigger so guided-tour selectors can still find it. */
  'data-tour'?: string;
}

/**
 * A "?" button that opens its explanation on click. Use this next to a label whose meaning
 * needs spelling out (formulas, grading scales, why a toggle exists).
 */
export function InfoPopover({
  content,
  ariaLabel = '說明',
  iconSize = 16,
  'data-tour': dataTour,
}: InfoPopoverProps) {
  const [anchorEl, setAnchorEl] = useState<HTMLElement | null>(null);

  return (
    <>
      <IconButton
        size="small"
        aria-label={ariaLabel}
        data-tour={dataTour}
        onClick={(event) => setAnchorEl(event.currentTarget)}
        sx={{ p: 0.25, color: 'text.disabled' }}
      >
        <HelpOutlineIcon sx={{ fontSize: iconSize }} />
      </IconButton>
      <Popover
        open={Boolean(anchorEl)}
        anchorEl={anchorEl}
        onClose={() => setAnchorEl(null)}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'left' }}
        transformOrigin={{ vertical: 'top', horizontal: 'left' }}
        slotProps={{ paper: { sx: POPOVER_PAPER_SX } }}
      >
        <PopoverBody content={content} />
      </Popover>
    </>
  );
}

type ClickableChild = ReactElement<{
  onClick?: (event: MouseEvent<HTMLElement>) => void;
  onKeyDown?: (event: KeyboardEvent<HTMLElement>) => void;
}>;

interface ClickPopoverProps {
  /** The explanation itself. When empty the child is rendered with no popover attached. */
  content: ReactNode;
  /**
   * Element the popover hangs off. It must forward `onClick` and a ref to a DOM node —
   * the same requirement MUI's own Tooltip places on its child.
   */
  children: ClickableChild;
  /** Where the popover opens relative to the child. */
  placement?: 'top' | 'bottom';
  /**
   * Put the trigger in the tab order. Turn this off for elements that repeat many times
   * (heatmap cells), where a stop per element would swamp keyboard navigation.
   */
  focusable?: boolean;
}

/**
 * Makes an otherwise non-interactive element (a stat card, a chip, a heatmap cell) tappable
 * to reveal its explanation. For elements that already do something on click, put an
 * `InfoPopover` beside them instead of wrapping them in this.
 */
export function ClickPopover({
  content,
  children,
  placement = 'bottom',
  focusable = true,
}: ClickPopoverProps) {
  const [anchorEl, setAnchorEl] = useState<HTMLElement | null>(null);

  if (!content || !isValidElement(children)) return children;

  const trigger = cloneElement(children, {
    onClick: (event: MouseEvent<HTMLElement>) => {
      children.props.onClick?.(event);
      setAnchorEl(event.currentTarget);
    },
    onKeyDown: (event: KeyboardEvent<HTMLElement>) => {
      children.props.onKeyDown?.(event);
      if (event.key === 'Enter' || event.key === ' ') {
        event.preventDefault();
        setAnchorEl(event.currentTarget);
      }
    },
    role: 'button',
    tabIndex: focusable ? 0 : -1,
    // A pointer cursor is the only hint that these otherwise inert elements do something.
    sx: { cursor: 'pointer', ...(children.props as { sx?: object }).sx },
  } as Partial<ClickableChild['props']>);

  const above = placement === 'top';

  return (
    <>
      {trigger}
      <Popover
        open={Boolean(anchorEl)}
        anchorEl={anchorEl}
        onClose={() => setAnchorEl(null)}
        anchorOrigin={{ vertical: above ? 'top' : 'bottom', horizontal: 'center' }}
        transformOrigin={{ vertical: above ? 'bottom' : 'top', horizontal: 'center' }}
        slotProps={{ paper: { sx: POPOVER_PAPER_SX } }}
      >
        <PopoverBody content={content} />
      </Popover>
    </>
  );
}

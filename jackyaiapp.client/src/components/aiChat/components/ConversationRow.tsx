import DeleteOutlineIcon from '@mui/icons-material/DeleteOutlineOutlined';
import EditOutlinedIcon from '@mui/icons-material/EditOutlined';
import { Box, IconButton, ListItem, ListItemButton, Typography } from '@mui/material';
import { useTheme } from '@mui/material/styles';

import { ACCENT, accentWash } from '../style';

import { ChatConversation } from '@/apis/chatbotApis/types';

interface ConversationRowProps {
  conversation: ChatConversation;
  active: boolean;
  /** A reply is being written: only the open conversation's row may be used. */
  busy: boolean;
  deleting: boolean;
  onSelect: () => void;
  onEdit: () => void;
  /** `byKey`: pressed from the keyboard (Enter or Space), so the focus has to go somewhere (D-187). */
  onDelete: (byKey: boolean) => void;
  /** Called with the row's button on every render, so the list can give it the focus on the commit it asks for. */
  selectRef: (el: HTMLDivElement | null) => void;
  /** The same for the pencil (D-146). */
  pencilRef: (el: HTMLButtonElement | null) => void;
}

/** Room the two buttons take at the row's right, kept free of the title even while they are hidden. */
const ACTIONS_WIDTH = 62;
const ROW_HEIGHT = 40;

/**
 * One conversation in the history column: a list item holding the button that opens it and, beside it rather than in
 * it, its rename and delete buttons (D-186) — a button inside a button is announced as one control, and its inner
 * buttons are unreachable or misnamed by some screen readers. They show on hover, on keyboard focus anywhere in the
 * row, on the open conversation, and always where there is no hover (touch screens).
 */
const ConversationRow = ({
  conversation: c,
  active,
  busy,
  deleting,
  onSelect,
  onEdit,
  onDelete,
  selectRef,
  pencilRef,
}: ConversationRowProps) => {
  const theme = useTheme();
  const locked = busy && !active;
  return (
    <ListItem
      disablePadding
      sx={{
        position: 'relative',
        mb: 0.25,
        '& .conversation-action': { opacity: 0, transition: 'opacity 120ms' },
        '&:hover .conversation-action, &:focus-within .conversation-action': { opacity: 1 },
        // The mouse on a button beside the row's own still lights the row, as when they were inside it.
        '&:hover .MuiListItemButton-root:not(.Mui-disabled)': {
          bgcolor: active ? accentWash(theme, 0.14) : theme.palette.action.hover,
        },
        ...(active && { '& .conversation-action': { opacity: 1 } }),
        '@media (hover: none)': { '& .conversation-action': { opacity: 1 } },
      }}
    >
      <ListItemButton
        selected={active}
        disabled={locked}
        onClick={onSelect}
        ref={selectRef}
        sx={{
          position: 'relative',
          borderRadius: 2,
          py: 0.75,
          pl: 1.25,
          pr: `${ACTIONS_WIDTH}px`,
          // The height the two buttons gave the row when they were inside it (6px above and below a 28px button).
          minHeight: ROW_HEIGHT,
          '&.Mui-selected, &.Mui-selected:hover': {
            bgcolor: accentWash(theme, 0.14),
          },
          '&.Mui-selected::before': {
            content: '""',
            position: 'absolute',
            left: 0,
            top: 8,
            bottom: 8,
            width: 3,
            borderRadius: 3,
            bgcolor: ACCENT,
          },
        }}
      >
        <Typography
          variant="body2"
          noWrap
          title={c.title}
          sx={{ flex: 1, minWidth: 0, fontWeight: active ? 600 : 400 }}
        >
          {c.title}
        </Typography>
      </ListItemButton>
      <Box
        sx={{
          position: 'absolute',
          right: 4,
          top: '50%',
          transform: 'translateY(-50%)',
          display: 'flex',
          alignItems: 'center',
        }}
      >
        <IconButton
          className="conversation-action"
          size="small"
          disabled={locked}
          onClick={onEdit}
          ref={pencilRef}
          aria-label="重新命名對話"
          sx={{ color: 'text.secondary', '&:hover': { color: ACCENT } }}
        >
          <EditOutlinedIcon sx={{ fontSize: 16 }} />
        </IconButton>
        <IconButton
          className="conversation-action"
          size="small"
          disabled={deleting || busy}
          // A click from the keyboard has no pointer, so `detail` (the mouse's click count) is 0.
          onClick={(e) => onDelete(e.detail === 0)}
          aria-label="刪除對話"
          sx={{ color: 'text.secondary', '&:hover': { color: 'error.main' } }}
        >
          <DeleteOutlineIcon sx={{ fontSize: 18 }} />
        </IconButton>
      </Box>
    </ListItem>
  );
};

export default ConversationRow;

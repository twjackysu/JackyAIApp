import { Drawer } from '@mui/material';

import { panelSurface } from '../style';

import ConversationList from './ConversationList';

interface ConversationDrawerProps {
  open: boolean;
  onClose: () => void;
  activeConversationId: string;
  onSelect: (conversationId: string) => void;
  onNewChat: () => void;
  busy?: boolean;
  /**
   * The drawer renders inside this element (the chat panel) instead of over the whole screen —
   * so its backdrop dims only the chat rather than the page around it. The element must be
   * `position: relative`.
   */
  containerEl: HTMLElement | null;
}

/**
 * The conversation history as a slide-over, for a panel too narrow to keep it beside the chat
 * (on wide screens the panel shows it as a fixed column instead).
 */
const ConversationDrawer = ({
  open,
  onClose,
  activeConversationId,
  onSelect,
  onNewChat,
  busy,
  containerEl,
}: ConversationDrawerProps) => (
  <Drawer
    anchor="left"
    open={open}
    onClose={onClose}
    variant="temporary"
    slotProps={{
      // Position the modal, its backdrop and its paper absolutely so they stay within the panel.
      root: { container: containerEl, style: { position: 'absolute' } },
      backdrop: { style: { position: 'absolute' } },
      paper: {
        sx: (theme) => ({
          width: { xs: '85%', sm: 280 },
          position: 'absolute',
          backgroundImage: 'none',
          bgcolor: panelSurface(theme),
        }),
      },
    }}
  >
    <ConversationList
      activeConversationId={activeConversationId}
      onSelect={onSelect}
      onNewChat={onNewChat}
      busy={busy}
      onDone={onClose}
    />
  </Drawer>
);

export default ConversationDrawer;

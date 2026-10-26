import ContentCopyIcon from '@mui/icons-material/ContentCopy';
import DownloadIcon from '@mui/icons-material/FileDownloadOutlined';
import IosShareIcon from '@mui/icons-material/IosShare';
import { IconButton, ListItemIcon, ListItemText, Menu, MenuItem, Snackbar } from '@mui/material';
import { useState } from 'react';

import { useAiChatConfig } from '../config';
import { Message } from '../types';
import { copyText } from '../utils/clipboard';
import {
  canExportConversation,
  conversationFileName,
  conversationMarkdown,
} from '../utils/conversationMarkdown';

import { useGetConversationsQuery } from '@/apis/chatbotApis';
import { ActionTooltip } from '@/components/hints';

interface ExportConversationButtonProps {
  messages: readonly Message[];
  /** The conversation's id once the server has stored it, for its title in the history. */
  conversationId: string;
  /** An answer is being written: what it has so far is not worth keeping yet. */
  busy: boolean;
}

/**
 * The header's 匯出對話 (D-147): the conversation on screen as Markdown, copied or downloaded as a .md file.
 * Nothing goes to the server and nothing is charged; the title comes from the history list (read when the
 * menu opens, from the cache when it is there).
 */
const ExportConversationButton = ({
  messages,
  conversationId,
  busy,
}: ExportConversationButtonProps) => {
  const { profile, title: assistant = 'AI 助手' } = useAiChatConfig();
  // The menu is open while it has the button to hang from.
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const open = anchor !== null;
  const close = () => setAnchor(null);
  const [notice, setNotice] = useState('');
  const { data } = useGetConversationsQuery(profile, { skip: !open || !conversationId });
  const title = data?.data.find((c) => c.id === conversationId)?.title;

  const markdown = () => {
    const exportedAt = new Date();
    return {
      exportedAt,
      text: conversationMarkdown(messages, {
        title,
        assistant,
        exportedAt,
        origin: window.location.origin,
      }),
    };
  };

  const copy = async () => {
    close();
    try {
      await copyText(markdown().text);
      setNotice('已複製整段對話（Markdown）');
    } catch {
      setNotice('沒有複製成功，請改用下載');
    }
  };

  const download = () => {
    close();
    const { text, exportedAt } = markdown();
    const url = URL.createObjectURL(new Blob([text], { type: 'text/markdown;charset=utf-8' }));
    const link = document.createElement('a');
    link.href = url;
    link.download = conversationFileName(title, exportedAt);
    document.body.appendChild(link);
    link.click();
    link.remove();
    // Revoked a moment later: some browsers start the download after the click returns.
    setTimeout(() => URL.revokeObjectURL(url), 10_000);
  };

  return (
    <>
      <ActionTooltip title="匯出對話">
        <span>
          <IconButton
            onClick={(e) => setAnchor(e.currentTarget)}
            aria-label="匯出對話"
            aria-haspopup="menu"
            disabled={busy || !canExportConversation(messages)}
            size="small"
            sx={{ color: 'text.secondary' }}
          >
            <IosShareIcon fontSize="small" />
          </IconButton>
        </span>
      </ActionTooltip>
      <Menu
        anchorEl={anchor}
        open={open}
        onClose={close}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'right' }}
        transformOrigin={{ vertical: 'top', horizontal: 'right' }}
      >
        <MenuItem onClick={() => void copy()}>
          <ListItemIcon>
            <ContentCopyIcon fontSize="small" />
          </ListItemIcon>
          <ListItemText primary="複製成 Markdown" secondary="貼到筆記、信件或文件" />
        </MenuItem>
        <MenuItem onClick={download}>
          <ListItemIcon>
            <DownloadIcon fontSize="small" />
          </ListItemIcon>
          <ListItemText primary="下載 .md 檔" secondary="表格與連結一起保留" />
        </MenuItem>
      </Menu>
      <Snackbar
        open={!!notice}
        autoHideDuration={3000}
        onClose={() => setNotice('')}
        message={notice}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}
      />
    </>
  );
};

export default ExportConversationButton;

import CopyIcon from '@mui/icons-material/ContentCopyOutlined';
import DownloadIcon from '@mui/icons-material/FileDownloadOutlined';
import { Box, IconButton, Typography } from '@mui/material';
import { useEffect, useState } from 'react';

import { copyText } from '../utils/clipboard';

import { csvBlob, exportedRowsNotice, tableFileName, tableTsv } from './tableExport';

import type { TableCard } from './toolCard';

import { ActionTooltip } from '@/components/hints';

/** How long what was done stays said beside the buttons. */
export const NOTICE_MS = 3000;

/**
 * Under a card that is a table (a table card, or an app's card it says how to read as one, `AiChatConfig.cardTables`):
 * copy it for a spreadsheet, or download it as CSV (D-150). On the reader's device only: nothing goes to the server
 * and nothing is charged.
 */
export default function TableCardActions({ table }: { table: TableCard }) {
  // Each press says it anew, its own full time, even when the words are the same as the last one's.
  const [notice, setNoticeState] = useState({ text: '', n: 0 });
  const setNotice = (text: string) => setNoticeState((prev) => ({ text, n: prev.n + 1 }));
  useEffect(() => {
    if (!notice.text) return;
    const timer = setTimeout(() => setNoticeState((prev) => ({ ...prev, text: '' })), NOTICE_MS);
    return () => clearTimeout(timer);
  }, [notice]);

  const copy = async () => {
    try {
      await copyText(tableTsv(table));
      setNotice(exportedRowsNotice(table, '已複製'));
    } catch {
      setNotice('沒有複製成功，請改用下載 CSV');
    }
  };

  const download = () => {
    const url = URL.createObjectURL(csvBlob(table));
    const link = document.createElement('a');
    link.href = url;
    link.download = tableFileName(table.title, new Date());
    document.body.appendChild(link);
    link.click();
    link.remove();
    // Revoked a moment later: some browsers start the download after the click returns.
    setTimeout(() => URL.revokeObjectURL(url), 10_000);
    setNotice(exportedRowsNotice(table, '已下載'));
  };

  return (
    <Box
      sx={{ display: 'flex', alignItems: 'center', justifyContent: 'flex-end', gap: 0.25, mt: 0.5 }}
    >
      <Typography
        role="status"
        variant="caption"
        color="text.secondary"
        sx={{ mr: 0.5, minWidth: 0 }}
      >
        {notice.text}
      </Typography>
      <ActionTooltip title="複製表格（可貼到 Excel、Google 試算表）">
        <IconButton
          size="small"
          aria-label="複製表格"
          onClick={() => void copy()}
          sx={{ color: 'text.secondary' }}
        >
          <CopyIcon sx={{ fontSize: 16 }} />
        </IconButton>
      </ActionTooltip>
      <ActionTooltip title="下載 CSV">
        <IconButton
          size="small"
          aria-label="下載 CSV"
          onClick={download}
          sx={{ color: 'text.secondary' }}
        >
          <DownloadIcon sx={{ fontSize: 16 }} />
        </IconButton>
      </ActionTooltip>
    </Box>
  );
}

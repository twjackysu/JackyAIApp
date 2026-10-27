import ChartIcon from '@mui/icons-material/BarChartOutlined';
import CopyIcon from '@mui/icons-material/ContentCopyOutlined';
import DocumentIcon from '@mui/icons-material/DescriptionOutlined';
import DownloadIcon from '@mui/icons-material/FileDownloadOutlined';
import TableIcon from '@mui/icons-material/TableChartOutlined';
import { Box, IconButton, Typography } from '@mui/material';
import { useEffect, useState } from 'react';

import { copyHtml, copyText } from '../utils/clipboard';

import { csvBlob, exportedRowsNotice, tableFileName, tableHtml, tableTsv } from './tableExport';

import type { TableCard } from './toolCard';

import { ActionTooltip } from '@/components/hints';

/** How long what was done stays said beside the buttons. */
export const NOTICE_MS = 3000;

/** 畫成圖 (D-153): offered by a table card whose rows can be drawn; `on` while the chart is shown in the table's place. */
export interface TableChartToggle {
  on: boolean;
  toggle: () => void;
}

/**
 * Under a card that is a table (a table card, or an app's card it says how to read as one, `AiChatConfig.cardTables`):
 * copy it for a spreadsheet, or download it as CSV (D-150). On the reader's device only: nothing goes to the server
 * and nothing is charged.
 */
export default function TableCardActions({
  table,
  chart,
}: {
  table: TableCard;
  chart?: TableChartToggle;
}) {
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

  // For a document or a mail (D-168): the rich copy, the tab-separated one beside it for a place that takes text only.
  const copyForDocument = async () => {
    try {
      await copyHtml(tableHtml(table), tableTsv(table));
      setNotice(`${exportedRowsNotice(table, '已複製')}，可貼到文件`);
    } catch {
      setNotice('這個瀏覽器不能複製成文件表格，請改用複製表格');
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
      {chart && (
        <ActionTooltip title={chart.on ? '看表格' : '畫成圖（不問 AI）'}>
          <IconButton
            size="small"
            // One name, pressed or not: a screen reader says 畫成圖, pressed.
            aria-label="畫成圖"
            aria-pressed={chart.on}
            onClick={chart.toggle}
            sx={{ color: chart.on ? 'primary.main' : 'text.secondary' }}
          >
            {chart.on ? <TableIcon sx={{ fontSize: 16 }} /> : <ChartIcon sx={{ fontSize: 16 }} />}
          </IconButton>
        </ActionTooltip>
      )}
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
      <ActionTooltip title="複製成文件表格（可貼到 Word、Gmail、Google 文件）">
        <IconButton
          size="small"
          aria-label="複製成文件表格"
          onClick={() => void copyForDocument()}
          sx={{ color: 'text.secondary' }}
        >
          <DocumentIcon sx={{ fontSize: 16 }} />
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

import FileDownloadOutlinedIcon from '@mui/icons-material/FileDownloadOutlined';
import IconButton from '@mui/material/IconButton';
import Menu from '@mui/material/Menu';
import MenuItem from '@mui/material/MenuItem';
import { useMemo, useState } from 'react';

import { exportNetWorthCsv, exportPortfolioCsv } from './portfolioExport';
import { buildStoryAllocation } from './storyAllocation';
import { StoryImageDialog } from './StoryImageDialog';

import { useLazyGetNetWorthHistoryQuery } from '@/apis/portfolioApis';
import { PortfolioSummaryDto } from '@/apis/portfolioApis/types';
import { ActionTooltip } from '@/components/hints';
import { todayISO } from '@/utils/date';

/** 匯出：目前持倉與淨值歷史的 CSV，以及持股比例的 IG 限動圖。 */
export function ExportMenu({ summary }: { summary: PortfolioSummaryDto }) {
  const [anchor, setAnchor] = useState<HTMLButtonElement | null>(null);
  const [fetchHistory, { isFetching }] = useLazyGetNetWorthHistoryQuery();
  const [storyOpen, setStoryOpen] = useState(false);
  const storyAlloc = useMemo(() => buildStoryAllocation(summary.positions), [summary.positions]);
  const hasHoldings = !!(storyAlloc.tw || storyAlloc.us || storyAlloc.fund);

  const close = () => setAnchor(null);

  const exportHistory = async () => {
    close();
    // 180 = the widest range the backend supports; gives the full available series.
    // groupBy defaults to assetType server-side — CSV export wants the classic 5-column breakdown.
    const res = await fetchHistory({ days: 180 }).unwrap();
    exportNetWorthCsv(res.data ?? []);
  };

  return (
    <>
      <ActionTooltip title="匯出 (CSV / 限動圖)">
        <IconButton onClick={(e) => setAnchor(e.currentTarget)} size="small" disabled={isFetching}>
          <FileDownloadOutlinedIcon />
        </IconButton>
      </ActionTooltip>
      <Menu anchorEl={anchor} open={!!anchor} onClose={close}>
        <MenuItem
          onClick={() => {
            close();
            exportPortfolioCsv(summary);
          }}
        >
          目前持倉
        </MenuItem>
        <MenuItem onClick={exportHistory}>淨值歷史 (時間序列)</MenuItem>
        <MenuItem
          disabled={!hasHoldings}
          onClick={() => {
            close();
            setStoryOpen(true);
          }}
        >
          IG 限動圖 (持股比例)
        </MenuItem>
      </Menu>
      <StoryImageDialog
        open={storyOpen}
        onClose={() => setStoryOpen(false)}
        alloc={storyAlloc}
        date={summary.effectiveDate ?? todayISO()}
      />
    </>
  );
}

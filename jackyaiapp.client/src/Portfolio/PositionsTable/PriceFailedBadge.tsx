import RefreshIcon from '@mui/icons-material/Refresh';
import CircularProgress from '@mui/material/CircularProgress';
import IconButton from '@mui/material/IconButton';

import { ActionTooltip } from '@/components/hints';

interface PriceFailedBadgeProps {
  symbol: string;
  onRefresh: (symbol: string) => void;
  refreshing: boolean;
}

/** Refetches a holding's quote after it failed. Shared by the desktop table and the mobile card
 * list; it stops the click from also opening the holding's detail. */
export function PriceFailedBadge({ symbol, onRefresh, refreshing }: PriceFailedBadgeProps) {
  return (
    <ActionTooltip title="重新抓取報價">
      <span>
        <IconButton
          size="small"
          disabled={refreshing}
          onClick={(e) => {
            e.stopPropagation();
            onRefresh(symbol);
          }}
          sx={{ p: 0.25 }}
        >
          {refreshing ? <CircularProgress size={14} /> : <RefreshIcon sx={{ fontSize: 14 }} />}
        </IconButton>
      </span>
    </ActionTooltip>
  );
}

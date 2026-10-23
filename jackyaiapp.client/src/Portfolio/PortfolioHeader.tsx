import HelpOutlineIcon from '@mui/icons-material/HelpOutlineOutlined';
import ShowChartIcon from '@mui/icons-material/ShowChart';
import VisibilityIcon from '@mui/icons-material/Visibility';
import VisibilityOffIcon from '@mui/icons-material/VisibilityOff';
import Box from '@mui/material/Box';
import IconButton from '@mui/material/IconButton';
import Typography from '@mui/material/Typography';

import { ExportMenu } from './ExportMenu';
import { FeeSettingsDialog } from './FeeSettingsDialog';
import { HoldingsEditor } from './HoldingsEditor';
import { usePrivacy } from './privacy';

import { PortfolioSummaryDto } from '@/apis/portfolioApis/types';
import { ActionTooltip } from '@/components/hints';

function PrivacyToggle() {
  const { mode, cycle } = usePrivacy();
  const cfg = {
    full: { icon: <VisibilityIcon />, title: '完整顯示（點擊：只看損益）' },
    changes: { icon: <ShowChartIcon />, title: '只看損益（點擊：隱藏金額）' },
    hidden: { icon: <VisibilityOffIcon />, title: '隱藏金額（點擊：完整顯示）' },
  }[mode];
  return (
    <ActionTooltip title={cfg.title}>
      <IconButton onClick={cycle} size="small" aria-label={cfg.title}>
        {cfg.icon}
      </IconButton>
    </ActionTooltip>
  );
}

interface PortfolioHeaderProps {
  summary: PortfolioSummaryDto | undefined;
  hasHoldings: boolean;
  isRefetching: boolean;
  feeDiscountRate: number;
  onSaveFeeDiscountRate: (rate: number) => void;
  onStartTour: () => void;
}

/** 標題與頁面動作：匯出、手續費、導覽、隱私、編輯持倉。 */
export function PortfolioHeader({
  summary,
  hasHoldings,
  isRefetching,
  feeDiscountRate,
  onSaveFeeDiscountRate,
  onStartTour,
}: PortfolioHeaderProps) {
  return (
    // Stacked on mobile instead of one row: squeezing the title next to 5 action controls was
    // compressing the "更新紀錄"/"更新持倉" buttons enough to wrap their own label text mid-word.
    <Box
      sx={{
        display: 'flex',
        flexDirection: { xs: 'column', sm: 'row' },
        alignItems: { xs: 'stretch', sm: 'center' },
        justifyContent: 'space-between',
        gap: { xs: 1.5, sm: 0 },
        mb: 3,
      }}
    >
      <Box>
        <Typography variant="h5" sx={{ fontWeight: 700 }}>
          Wealth Portfolio
        </Typography>
        <Typography variant="body2" color="text.secondary">
          台股 · 美股 · 現金 · 基金 統一以台幣淨值追蹤
          {summary?.effectiveDate ? ` · 持倉生效日 ${summary.effectiveDate}` : ''}
          {isRefetching ? ' · 更新中…' : ''}
        </Typography>
      </Box>
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, flexWrap: 'wrap' }}>
        {hasHoldings && summary && <ExportMenu summary={summary} />}
        <FeeSettingsDialog feeDiscountRate={feeDiscountRate} onSave={onSaveFeeDiscountRate} />
        <ActionTooltip title="重新看導覽教學">
          <IconButton onClick={onStartTour} size="small" aria-label="重新看導覽教學">
            <HelpOutlineIcon />
          </IconButton>
        </ActionTooltip>
        <PrivacyToggle />
        <HoldingsEditor />
      </Box>
    </Box>
  );
}

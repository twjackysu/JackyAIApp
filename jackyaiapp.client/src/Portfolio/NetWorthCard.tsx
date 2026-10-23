import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Chip from '@mui/material/Chip';
import { alpha } from '@mui/material/styles';
import Typography from '@mui/material/Typography';
import { ReactNode } from 'react';

import { usePrivacy } from './privacy';

import { PortfolioSummaryDto } from '@/apis/portfolioApis/types';
import { fmtNum as fmt, fmtSignedPct, GAIN_COLOR, LOSS_COLOR, pnlColor } from '@/utils/format';

/** The caption trailing each P&L row wraps onto its own line on phones, where the amount and the
 * percentage badge already fill the row. */
const PNL_CAPTION_SX = {
  flexBasis: { xs: '100%', sm: 'auto' },
  fontSize: { xs: '0.7rem', sm: '0.75rem' },
} as const;

/** Signed-percentage badge. Zero counts as a gain here (matching the ▲ threshold of the row it
 * sits in), so it never renders in the loss colour without a "-" in the label. */
function PctChip({
  pct,
  label = '',
  outlined = false,
}: {
  pct: number;
  label?: string;
  outlined?: boolean;
}) {
  const color = pct >= 0 ? GAIN_COLOR : LOSS_COLOR;
  return (
    <Chip
      label={`${label}${fmtSignedPct(pct)}`}
      size="small"
      variant={outlined ? 'outlined' : 'filled'}
      sx={
        outlined
          ? { borderColor: alpha(color, 0.5), color, fontWeight: 700 }
          : { bgcolor: alpha(color, 0.15), color, fontWeight: 700, border: 'none' }
      }
    />
  );
}

/**
 * One "▲ NT$x,xxx  (+y.yy%)  <caption>" line of the net-worth card — 單日異動, 未實現損益 and
 * 含息損益 are the same row with different numbers behind them. The amount honours the privacy
 * mode's `change` mask; the badge is dropped whenever ratios are hidden or `pct` is unknown.
 */
function PnlRow({
  amount,
  pct,
  caption,
}: {
  amount: number;
  pct?: number | null;
  caption: ReactNode;
}) {
  const { change, showRatio } = usePrivacy();
  return (
    <Box sx={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 1, mt: 1 }}>
      <Typography
        variant="body1"
        sx={{ color: pnlColor(amount, 'inherit'), fontWeight: 600 }}
        component="span"
      >
        {amount >= 0 ? '▲' : '▼'} NT${change(fmt(Math.abs(amount)))}
      </Typography>
      {showRatio && pct != null && <PctChip pct={pct} />}
      <Typography variant="caption" color="text.secondary" sx={PNL_CAPTION_SX}>
        {caption}
      </Typography>
    </Box>
  );
}

/** The headline card: liquid assets, with the day's change and the P&L rows under it. */
export function NetWorthCard({ summary }: { summary: PortfolioSummaryDto }) {
  const { value, change, showRatio } = usePrivacy();
  const {
    totalNetWorthTWD: total,
    realEstateTWD: realEstate,
    liquidGrossAssetsTWD: liquidGrossAssets,
    estimatedSellCostTWD: estimatedSellCost,
    totalDividendsTWD: totalDividends,
    annualizedReturnPct: annualized,
    dayChangeTWD: dayChange,
  } = summary;
  // P&L rows only once some cost is filled in: without one there is nothing to measure against.
  const hasPnl = summary.costBasisTWD > 0;
  const liquidNetWorth = total - realEstate;
  return (
    <Card
      data-tour="portfolio-tour-networth"
      elevation={0}
      sx={{
        borderRadius: 3,
        border: '1px solid',
        borderColor: 'divider',
        background: (theme) =>
          `linear-gradient(135deg, ${alpha('#F59E0B', 0.12)} 0%, ${alpha(
            theme.palette.background.paper,
            0.4,
          )} 60%)`,
      }}
    >
      <CardContent sx={{ p: { xs: 2, sm: 3 } }}>
        <Typography variant="overline" color="text.secondary" sx={{ letterSpacing: 2 }}>
          流動資產
        </Typography>
        {/* Headline is gross liquid assets (excl. real estate, not net of liability), not total
            net worth: 單日異動/未實現/含息損益 % below are computed against this same figure (see
            PortfolioService.LiquidGrossAssetsTWD); 單日異動's amount is the change in liquid net
            assets (流動資產淨值) vs the previous trading day. A self-occupied home that's
            never getting sold — plus the debt against it — doesn't belong in "what can I actually
            deploy" the way it does in a legal/accounting net-worth figure. Responsive font size
            instead of a fixed h3 — at the theme's default h3 size, a 8-digit TWD figure ran wider
            than a phone screen and got clipped. */}
        <Typography
          sx={{
            fontWeight: 800,
            mt: 0.5,
            letterSpacing: -1,
            fontSize: { xs: '2rem', sm: '2.5rem', md: '3rem' },
            wordBreak: 'break-all',
          }}
        >
          NT${value(fmt(liquidGrossAssets))}
        </Typography>
        {/* Only worth surfacing once they diverge from the headline — otherwise these would just
            repeat the same number. */}
        {total !== liquidGrossAssets && (
          <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5 }}>
            總資產淨值 NT${value(fmt(total))}
          </Typography>
        )}
        {liquidNetWorth !== liquidGrossAssets && (
          <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5 }}>
            流動資產淨值 NT${value(fmt(liquidNetWorth))}
          </Typography>
        )}
        {dayChange != null && (
          <PnlRow
            amount={dayChange}
            pct={summary.dayChangePct}
            caption="單日異動 (與前一交易日比)"
          />
        )}
        {hasPnl && (
          <PnlRow
            amount={summary.unrealizedPnlTWD}
            pct={summary.unrealizedPnlPct}
            caption={
              <>
                未實現損益(已填成本部分)
                {estimatedSellCost > 0
                  ? `，已扣估計台股賣出費用 NT$${change(fmt(estimatedSellCost))}`
                  : ''}
              </>
            }
          />
        )}
        {hasPnl && totalDividends !== 0 && (
          <PnlRow
            amount={summary.totalReturnPnlTWD}
            pct={summary.totalReturnPct}
            caption={`含息損益(含累計股息 NT$${change(fmt(totalDividends))}，稅前)`}
          />
        )}
        {hasPnl && annualized != null && showRatio && (
          <Box sx={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 1, mt: 0.5 }}>
            <PctChip pct={annualized} label="年化 " outlined />
            <Typography variant="caption" color="text.secondary" sx={PNL_CAPTION_SX}>
              年化報酬率 (XIRR·含息·估)
            </Typography>
          </Box>
        )}
      </CardContent>
    </Card>
  );
}

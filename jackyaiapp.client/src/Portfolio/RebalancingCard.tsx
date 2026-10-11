import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Grid from '@mui/material/Grid';
import InputAdornment from '@mui/material/InputAdornment';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { useState } from 'react';

import { usePrivacy } from './privacy';
import {
  computeCurrentAllocation,
  computeRebalancingDeltas,
  CurrentAllocationEntry,
  loadRebalancingTarget,
  rebalancingBaseTWD,
  REBALANCING_BUCKETS,
  RebalancingBucket,
  RebalancingTarget,
  saveRebalancingTarget,
} from './rebalancingTarget';

import { PortfolioSummaryDto } from '@/apis/portfolioApis/types';
import { fmtNum, GAIN_COLOR, LOSS_COLOR, WARN_COLOR } from '@/utils/format';

const BUCKET_LABEL: Record<RebalancingBucket, string> = {
  twStock: '台股',
  usStock: '美股',
  fund: '基金',
  cash: '現金',
};

// Below this, a "buy"/"sell" instruction would just be rounding noise on a real trade.
const DELTA_TOLERANCE_TWD = 100;
// Outside this band the sum is far enough from 100% that it's worth a soft nudge — still just a
// nudge, since deliberately under/over-100% (e.g. "raise cash, haven't decided the rest") is valid.
const SUM_WARNING_LOW = 95;
const SUM_WARNING_HIGH = 105;

function toTarget(current: CurrentAllocationEntry[]): RebalancingTarget {
  const target = {} as RebalancingTarget;
  for (const { bucket, currentPct } of current) {
    target[bucket] = Math.round(currentPct * 10) / 10;
  }
  return target;
}

interface RebalancingCardProps {
  summary: PortfolioSummaryDto;
}

/** Target allocation is a per-browser localStorage preference (see `rebalancingTarget.ts`), not
 * account data — this card is the only place that reads/writes it. Reuses the same
 * `PortfolioSummaryDto` the page already fetched for `ConcentrationCard`/`AllocationCard`; no
 * second request, no AI call. */
export function RebalancingCard({ summary }: RebalancingCardProps) {
  const { value, showRatio } = usePrivacy();
  const current = computeCurrentAllocation(summary);
  const totalAssetsTWD = rebalancingBaseTWD(summary);

  // First-run (nothing saved yet): seed with the current allocation so a first-time viewer sees
  // "already balanced" everywhere, not a wall of "sell everything" before expressing a preference.
  const [target, setTarget] = useState<RebalancingTarget>(
    () => loadRebalancingTarget() ?? toTarget(current),
  );

  const updateBucket = (bucket: RebalancingBucket, raw: string) => {
    const parsed = parseFloat(raw);
    const next = { ...target, [bucket]: Number.isFinite(parsed) ? parsed : 0 };
    setTarget(next);
    saveRebalancingTarget(next);
  };

  const resetToCurrent = () => {
    const next = toTarget(current);
    setTarget(next);
    saveRebalancingTarget(next);
  };

  const deltas = computeRebalancingDeltas(current, target, totalAssetsTWD);
  const sum = REBALANCING_BUCKETS.reduce((s, bucket) => s + (target[bucket] || 0), 0);
  const sumOutOfRange = sum < SUM_WARNING_LOW || sum > SUM_WARNING_HIGH;

  return (
    <Box>
      <Box
        sx={{
          display: 'flex',
          justifyContent: 'space-between',
          alignItems: 'center',
          flexWrap: 'wrap',
          gap: 1,
          mb: 2,
        }}
      >
        <Typography variant="body2" color="text.secondary">
          設定目標配置百分比，對照目前配置，看看該買進或賣出多少
        </Typography>
        <Button size="small" variant="outlined" onClick={resetToCurrent}>
          重設為目前配置
        </Button>
      </Box>

      <Grid container spacing={1.5} sx={{ mb: 1 }}>
        {REBALANCING_BUCKETS.map((bucket) => (
          <Grid key={bucket} size={{ xs: 6, sm: 3 }}>
            <TextField
              size="small"
              type="number"
              label={BUCKET_LABEL[bucket]}
              value={target[bucket]}
              onChange={(e) => updateBucket(bucket, e.target.value)}
              fullWidth
              slotProps={{
                input: { endAdornment: <InputAdornment position="end">%</InputAdornment> },
                htmlInput: { min: 0, max: 100, step: 1 },
              }}
            />
          </Grid>
        ))}
      </Grid>

      <Typography
        variant="caption"
        sx={{
          display: 'block',
          mb: 2,
          color: sumOutOfRange ? WARN_COLOR : 'text.secondary',
          fontWeight: sumOutOfRange ? 700 : 400,
        }}
      >
        目標合計 {fmtNum(sum, 1)}%{sumOutOfRange ? '（未達或超過 100%，僅供提醒，仍可儲存）' : ''}
      </Typography>

      <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1 }}>
        {deltas.map((d) => {
          const isBalanced = Math.abs(d.deltaTWD) < DELTA_TOLERANCE_TWD;
          return (
            <Box
              key={d.bucket}
              sx={{ display: 'flex', alignItems: 'center', gap: 1, flexWrap: 'wrap' }}
            >
              <Typography variant="body2" sx={{ width: 56, flexShrink: 0, fontWeight: 600 }}>
                {BUCKET_LABEL[d.bucket]}
              </Typography>
              <Typography variant="body2" color="text.secondary" sx={{ width: 130, flexShrink: 0 }}>
                {showRatio ? `${d.currentPct.toFixed(1)}% → ${d.targetPct.toFixed(1)}%` : '—'}
              </Typography>
              <Typography
                variant="body2"
                sx={{
                  fontWeight: 600,
                  color: isBalanced ? 'text.secondary' : d.deltaTWD > 0 ? GAIN_COLOR : LOSS_COLOR,
                }}
              >
                {isBalanced
                  ? '±持平'
                  : `${d.deltaTWD > 0 ? '買進' : '賣出'} NT$${value(fmtNum(Math.abs(d.deltaTWD)))}`}
              </Typography>
            </Box>
          );
        })}
      </Box>

      <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 2 }}>
        分母為「流動總資產」（台股＋美股＋基金＋現金），不含不動產與負債。目標配置僅儲存在這個瀏覽器，不會同步到其他裝置。
      </Typography>
    </Box>
  );
}

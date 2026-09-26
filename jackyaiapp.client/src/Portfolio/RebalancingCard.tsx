import RestartAltIcon from '@mui/icons-material/RestartAlt';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Grid from '@mui/material/Grid';
import InputAdornment from '@mui/material/InputAdornment';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { useState } from 'react';

import {
  BUCKET_LABEL,
  BUCKETS,
  computeCurrentAllocation,
  computeRebalancingDeltas,
  loadRebalancingTarget,
  RebalancingTarget,
  saveRebalancingTarget,
  totalAssetsTWD,
} from './rebalancingTarget';

import { PortfolioSummaryDto } from '@/apis/portfolioApis/types';
import { fmtNum, GAIN_COLOR, LOSS_COLOR, WARN_COLOR } from '@/utils/format';

interface RebalancingCardProps {
  summary: PortfolioSummaryDto;
}

// Soft warning band only — a deliberately under/over-100% target (e.g. "raise cash, haven't
// decided the rest yet") is a legitimate use, not an error, so nothing here blocks saving.
const SUM_WARN_LOW = 95;
const SUM_WARN_HIGH = 105;
// Below this, the delta reads as "already at target" rather than a stray rounding difference.
const FLAT_DELTA_TWD = 100;

function seedFromCurrent(summary: PortfolioSummaryDto): RebalancingTarget {
  const target = {} as RebalancingTarget;
  computeCurrentAllocation(summary).forEach(({ bucket, currentPct }) => {
    target[bucket] = Math.round(currentPct * 10) / 10;
  });
  return target;
}

/**
 * "How is my money spread out vs. where I want it" planning card, sitting next to
 * ConcentrationCard on the Portfolio summary page. The target allocation is a per-browser
 * localStorage preference (see rebalancingTarget.ts's own comment) — not account data, so
 * there's no save/load round trip through the backend.
 */
export function RebalancingCard({ summary }: RebalancingCardProps) {
  const [target, setTarget] = useState<RebalancingTarget>(
    () => loadRebalancingTarget() ?? seedFromCurrent(summary),
  );

  const current = computeCurrentAllocation(summary);
  const total = totalAssetsTWD(summary);
  const deltas = computeRebalancingDeltas(current, target, total);
  const sum = BUCKETS.reduce((s, bucket) => s + (target[bucket] || 0), 0);
  const sumOutOfRange = sum < SUM_WARN_LOW || sum > SUM_WARN_HIGH;

  const updateBucket = (bucket: keyof RebalancingTarget, value: string) => {
    const parsed = parseFloat(value);
    const next = { ...target, [bucket]: Number.isFinite(parsed) ? parsed : 0 };
    setTarget(next);
    saveRebalancingTarget(next);
  };

  const handleReset = () => {
    const next = seedFromCurrent(summary);
    setTarget(next);
    saveRebalancingTarget(next);
  };

  return (
    <Box>
      <Box
        sx={{
          display: 'flex',
          justifyContent: 'space-between',
          alignItems: 'center',
          mb: 2,
          gap: 1,
        }}
      >
        <Typography
          variant="body2"
          sx={{
            color: sumOutOfRange ? WARN_COLOR : 'text.secondary',
            fontWeight: sumOutOfRange ? 700 : 400,
          }}
        >
          目標合計：{fmtNum(sum, 1)}%{sumOutOfRange ? '（未接近 100%，僅供提醒）' : ''}
        </Typography>
        <Button size="small" startIcon={<RestartAltIcon />} onClick={handleReset}>
          重設為目前配置
        </Button>
      </Box>

      <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1.5 }}>
        {deltas.map((d) => {
          const flat = Math.abs(d.deltaTWD) < FLAT_DELTA_TWD;
          const deltaColor = flat ? 'text.secondary' : d.deltaTWD > 0 ? GAIN_COLOR : LOSS_COLOR;
          const deltaText = flat
            ? '±持平'
            : d.deltaTWD > 0
              ? `買入 $${fmtNum(d.deltaTWD)}`
              : `賣出 $${fmtNum(-d.deltaTWD)}`;

          return (
            <Grid container key={d.bucket} spacing={1} sx={{ alignItems: 'center' }}>
              <Grid size={{ xs: 12, sm: 2 }}>
                <Typography variant="body2">{BUCKET_LABEL[d.bucket]}</Typography>
              </Grid>
              <Grid size={{ xs: 6, sm: 2 }}>
                <Typography variant="body2" color="text.secondary">
                  目前 {d.currentPct.toFixed(1)}%
                </Typography>
              </Grid>
              <Grid size={{ xs: 6, sm: 3 }}>
                <TextField
                  size="small"
                  type="number"
                  label="目標 %"
                  value={target[d.bucket]}
                  onChange={(e) => updateBucket(d.bucket, e.target.value)}
                  fullWidth
                  slotProps={{
                    htmlInput: { min: 0, max: 100, step: 0.5 },
                    input: { endAdornment: <InputAdornment position="end">%</InputAdornment> },
                  }}
                />
              </Grid>
              <Grid size={{ xs: 12, sm: 5 }}>
                <Typography variant="body2" sx={{ color: deltaColor, fontWeight: 700 }}>
                  {deltaText}
                </Typography>
              </Grid>
            </Grid>
          );
        })}
      </Box>

      <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 2 }}>
        目標僅儲存在此瀏覽器；換裝置或清除瀏覽器資料需重新設定。買賣金額 = 目標% ×
        總資產（現金+股票+基金+不動產，不含負債）－目前市值。
      </Typography>
    </Box>
  );
}

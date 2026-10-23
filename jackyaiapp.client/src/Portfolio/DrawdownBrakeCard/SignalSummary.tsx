import Box from '@mui/material/Box';
import Chip from '@mui/material/Chip';
import Typography from '@mui/material/Typography';

import { STATE_LABEL } from '../drawdownBrakeCopy';

import { ActiveSignal } from './activeSignal';
import { StatItem } from './StatItem';

import { DrawdownBrakeDto } from '@/apis/portfolioApis/types';

function formatEstimateTime(iso: string): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return iso;
  return date.toLocaleString('zh-TW', {
    hour12: false,
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  });
}

export function SignalSummary({
  signal,
  active,
}: {
  signal: DrawdownBrakeDto;
  active: ActiveSignal;
}) {
  const distance = active.distanceToTriggerPct;
  const estimate = signal.estimate;
  return (
    <Box sx={{ mb: 2 }}>
      <Box sx={{ display: 'flex', alignItems: 'center', flexWrap: 'wrap', gap: 1, mb: 1.5 }}>
        <Typography variant="body2" color="text.secondary">
          目前狀態
        </Typography>
        <Chip
          size="small"
          label={STATE_LABEL[active.state]}
          color={active.state === 'OFFENSE' ? 'success' : 'warning'}
        />
        {active.isEstimate && (
          <Chip size="small" variant="outlined" color="info" label="預估訊號（盤中）" />
        )}
      </Box>
      <Box
        sx={{
          display: 'grid',
          gridTemplateColumns: 'repeat(auto-fit, minmax(140px, 1fr))',
          gap: 1.5,
        }}
      >
        <StatItem label="資料截止日" value={signal.asOfDate ?? '—'} />
        <StatItem label="距近一年高點" value={`${active.drawdownPct.toFixed(1)}%`} />
        <StatItem
          label="距煞車觸發點"
          value={
            distance >= 0 ? `${distance.toFixed(1)}%` : `已低於 ${Math.abs(distance).toFixed(1)}%`
          }
        />
        <StatItem label="最近切換日" value={signal.lastSwitchDate ?? '—'} />
      </Box>
      {estimate && (
        <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 1 }}>
          盤中預估（{formatEstimateTime(estimate.asOfTime)}），收盤後以正式數據為準。
        </Typography>
      )}
    </Box>
  );
}

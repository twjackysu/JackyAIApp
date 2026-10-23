import Chip from '@mui/material/Chip';
import Stack from '@mui/material/Stack';

import {
  distancePct,
  fmtPrice,
  RESISTANCE_COLOR,
  SUPPORT_COLOR,
} from '../../utils/trendlineChartOption';

import { TRENDLINE_METHOD } from './copy';

import type { CurrentLevel } from '../../utils/trendlineDrawings';

import { InfoPopover } from '@/components/hints';

function LevelChip({
  label,
  color,
  current,
  lastClose,
}: {
  label: string;
  color: string;
  current: CurrentLevel | null;
  lastClose: number;
}) {
  return (
    <Chip
      size="small"
      variant="outlined"
      // Wraps instead of cutting the distance off on a phone.
      sx={{
        borderColor: color,
        color,
        fontWeight: 600,
        height: 'auto',
        minHeight: 24,
        '& .MuiChip-label': { whiteSpace: 'normal', py: 0.25 },
      }}
      label={
        current == null
          ? `${label}：目前沒有有效的線`
          : `${label} ≈ ${fmtPrice(current.level)}（連 ${current.touches} 點，收盤距離 ${distancePct(lastClose, current.level)}）`
      }
    />
  );
}

interface LevelChipsProps {
  support: CurrentLevel | null;
  resistance: CurrentLevel | null;
  lastClose: number;
}

/** Today's support and resistance from the lines still in force, and how the lines are drawn. */
export function LevelChips({ support, resistance, lastClose }: LevelChipsProps) {
  return (
    <Stack
      direction="row"
      spacing={1}
      sx={{ flexWrap: 'wrap', rowGap: 1, mb: 1, alignItems: 'center' }}
    >
      <LevelChip label="支撐" color={SUPPORT_COLOR} current={support} lastClose={lastClose} />
      <LevelChip label="壓力" color={RESISTANCE_COLOR} current={resistance} lastClose={lastClose} />
      <InfoPopover content={TRENDLINE_METHOD} ariaLabel="趨勢線怎麼畫的" iconSize={16} />
    </Stack>
  );
}

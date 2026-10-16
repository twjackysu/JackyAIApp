import Box from '@mui/material/Box';
import Chip from '@mui/material/Chip';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import { useMemo } from 'react';

import '../charts/registerFinanceCharts';
import {
  buildTrendlineOption,
  distancePct,
  RESISTANCE_COLOR,
  SUPPORT_COLOR,
} from '../utils/trendlineChartOption';
import { analyzeTrendlines, type TrendlineAnalysis } from '../utils/trendlines';

import type { PriceBarsData } from '@/apis/financeApis/types';

import { ECHARTS_THEME } from '@/charts/echarts';
import ReactECharts from '@/charts/ReactECharts';
import { InfoPopover } from '@/components/hints';
import { fmtNum } from '@/utils/format';

export const TRENDLINE_METHOD =
  '線是程式依規則自動畫的，沒有人工挑日期：先用 ZigZag 濾掉小於 2 倍 ATR（平均真實波幅）的小波動，' +
  '只留下真正的波段高低點；再把每兩個高點（或低點）都試著連成一條線，挑「被波段點碰觸最多、收盤最少穿越、' +
  '離價格最近」的那條。每條線至少連 2 個點，畫到被連續兩日收盤穿越（或單日穿越 1 倍 ATR），' +
  '或價格離開超過 3 倍 ATR 為止。藍色是支撐、紫色是壓力，粗線是今天仍有效的線。';

export const TRENDLINE_DISCLAIMER = '趨勢線僅供參考，不構成投資建議。';

const CHART_HEIGHT = 360;

function LevelChip({
  label,
  color,
  level,
  lastClose,
  touches,
}: {
  label: string;
  color: string;
  level: number | null;
  lastClose: number;
  touches: number | null;
}) {
  return (
    <Chip
      size="small"
      variant="outlined"
      sx={{ borderColor: color, color, fontWeight: 600 }}
      label={
        level == null
          ? `${label}：目前沒有有效的線`
          : `${label} ≈ ${fmtNum(level)}（連 ${touches} 點，收盤距離 ${distancePct(lastClose, level)}）`
      }
    />
  );
}

/** Candlesticks of `data` with automatically found support/resistance trendlines. */
export function TrendlineChart({ data }: { data: PriceBarsData }) {
  const bars = data.bars;
  const analysis: TrendlineAnalysis = useMemo(() => analyzeTrendlines(bars), [bars]);
  const option = useMemo(() => buildTrendlineOption(bars, analysis), [bars, analysis]);
  const lastClose = bars[bars.length - 1].close;
  const activeTouches = (side: 'support' | 'resistance') =>
    analysis.lines.find((l) => l.side === side && l.isActive)?.touches ?? null;

  return (
    <>
      <Stack
        direction="row"
        spacing={1}
        sx={{ flexWrap: 'wrap', rowGap: 1, mb: 1, alignItems: 'center' }}
      >
        <LevelChip
          label="支撐"
          color={SUPPORT_COLOR}
          level={analysis.support}
          lastClose={lastClose}
          touches={activeTouches('support')}
        />
        <LevelChip
          label="壓力"
          color={RESISTANCE_COLOR}
          level={analysis.resistance}
          lastClose={lastClose}
          touches={activeTouches('resistance')}
        />
        <InfoPopover content={TRENDLINE_METHOD} ariaLabel="趨勢線怎麼畫的" iconSize={16} />
      </Stack>
      <Box
        role="img"
        aria-label={`${data.name} 日K線與自動趨勢線，共 ${analysis.lines.length} 條線`}
      >
        <ReactECharts
          option={option}
          notMerge
          theme={ECHARTS_THEME}
          style={{ height: CHART_HEIGHT, width: '100%' }}
        />
      </Box>
      <Typography variant="caption" color="text.secondary" sx={{ display: 'block' }}>
        資料至 {data.asOf}，收盤 {fmtNum(lastClose, 2)}。{TRENDLINE_DISCLAIMER}
      </Typography>
    </>
  );
}

export const TRENDLINE_CHART_HEIGHT = CHART_HEIGHT;

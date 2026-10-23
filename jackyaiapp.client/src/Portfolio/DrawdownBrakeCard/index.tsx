import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Skeleton from '@mui/material/Skeleton';
import Typography from '@mui/material/Typography';
import { useState } from 'react';

import { ChartSectionCard } from '../ChartSectionCard';
import { BrakeSettings, loadBrakeSettings, saveBrakeSettings } from '../drawdownBrake';
import { DISCLAIMER, SIMULATED_NOTE } from '../drawdownBrakeCopy';
import { extractStrategyPositions, priceRefreshSymbols } from '../drawdownBrakeHoldings';
import { classifyPositions } from '../drawdownBrakeScope';

import { pickActiveSignal } from './activeSignal';
import { AdvicePanel } from './AdvicePanel';
import { DrawdownBrakeFreshness } from './DrawdownBrakeRefresh';
import { HoldingsInfo } from './HoldingsInfo';
import { RiskExplanation } from './RiskExplanation';
import { SettingsPanel } from './SettingsPanel';
import { SignalSummary } from './SignalSummary';

import { useGetUserInfoQuery } from '@/apis/accountApis';
import { useGetDrawdownBrakeQuery } from '@/apis/portfolioApis';
import { PortfolioSummaryDto } from '@/apis/portfolioApis/types';
import { getApiErrorMessage } from '@/apis/utils/errorMessage';

/** 固定警語：卡片所有狀態（載入中、資料準備中、資料未更新、錯誤、預估）都在，不可關閉。 */
function Disclaimer() {
  return (
    <Alert severity="warning" variant="outlined" role="note" sx={{ mb: 2 }}>
      <Typography variant="body2" sx={{ fontWeight: 600, overflowWrap: 'anywhere' }}>
        {DISCLAIMER}
      </Typography>
      <Typography variant="caption" color="text.secondary" sx={{ overflowWrap: 'anywhere' }}>
        {SIMULATED_NOTE}
      </Typography>
    </Alert>
  );
}

function DrawdownBrakeContent({ summary }: { summary: PortfolioSummaryDto }) {
  const [settings, setSettings] = useState<BrakeSettings>(loadBrakeSettings);
  const { data, isLoading, isError, error, refetch } = useGetDrawdownBrakeQuery();
  const signal = data?.data;
  const active = signal ? pickActiveSignal(signal) : null;
  // 重新整理要重抓報價的標的：使用者持有的核心標的（0050、00631L、00685L），加上範圍內價格取得失敗
  // 的股票（例如 NTSD）— 失敗會暫停整張卡片的建議，這是使用者唯一的補救手段。
  const refreshSymbols = priceRefreshSymbols(
    extractStrategyPositions(summary.positions),
    classifyPositions(summary.positions).priceFailed,
  );

  const handleChange = (next: BrakeSettings) => {
    setSettings(next);
    saveBrakeSettings(next);
  };

  return (
    <Box sx={{ minWidth: 0 }}>
      <Disclaimer />

      {isLoading && <Skeleton variant="rounded" height={96} sx={{ mb: 2 }} />}

      {isError && !signal && (
        <Alert
          severity="error"
          sx={{ mb: 2 }}
          action={
            <Button color="inherit" size="small" onClick={() => refetch()}>
              重試
            </Button>
          }
        >
          {getApiErrorMessage(error)}
        </Alert>
      )}

      {signal?.isInitializing && (
        <Alert severity="info" sx={{ mb: 2 }}>
          資料準備中，大盤訊號建立完成後會顯示在這裡，請稍後再回來看看。
        </Alert>
      )}

      {signal?.estimateCorrected && (
        <Alert severity="warning" sx={{ mb: 2 }}>
          訊號已更正：先前的盤中預估與收盤後的正式訊號不同，已以正式訊號為準。
        </Alert>
      )}

      {signal?.isStale && !signal.isInitializing && (
        <Alert severity="warning" sx={{ mb: 2 }}>
          資料未更新（資料截止 {signal.asOfDate ?? '—'}），目前沿用最近一個交易日的狀態。
        </Alert>
      )}

      {signal && !signal.isInitializing && !active && (
        <Alert severity="info" sx={{ mb: 2 }}>
          目前沒有可用的大盤訊號。
        </Alert>
      )}

      {/* 資料時間＋重新整理：有訊號資料的所有狀態都顯示（準備中、未更新、預估、正常）。 */}
      {signal && <DrawdownBrakeFreshness signal={signal} refreshSymbols={refreshSymbols} />}

      {signal && active && (
        <>
          <SignalSummary signal={signal} active={active} />
          <AdvicePanel summary={summary} settings={settings} active={active} />
        </>
      )}

      <HoldingsInfo summary={summary} />
      <SettingsPanel settings={settings} onChange={handleChange} />
      <RiskExplanation />
    </Box>
  );
}

interface DrawdownBrakeCardProps {
  summary: PortfolioSummaryDto;
}

/** 「回落煞車」：只在 `drawdownBrakeEnabled` 為 true 時才掛載內容 — 內容才會發請求，所以功能關閉
 * 時（Q1 未決前一般使用者）既看不到，也不會產生任何 API 呼叫。不呼叫 AI、不消耗 UsageLimit。 */
export function DrawdownBrakeCard({ summary }: DrawdownBrakeCardProps) {
  const { data: userRes } = useGetUserInfoQuery();
  if (!userRes?.data.drawdownBrakeEnabled) return null;
  return (
    <ChartSectionCard title="回落煞車">
      <DrawdownBrakeContent summary={summary} />
    </ChartSectionCard>
  );
}

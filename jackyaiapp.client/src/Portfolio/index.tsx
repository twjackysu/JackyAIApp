import Box from '@mui/material/Box';
import LinearProgress from '@mui/material/LinearProgress';
import Typography from '@mui/material/Typography';
import { useState } from 'react';

import { loadFeeDiscountRate, saveFeeDiscountRate } from './feeDiscountRate';
import { HoldingDetailDrawer } from './HoldingDetailDrawer';
import { usePortfolioTour } from './onboardingTour';
import { PortfolioDashboard } from './PortfolioDashboard';
import { PortfolioHeader } from './PortfolioHeader';
import { PrivacyProvider } from './PrivacyContext';

import { useGetPortfolioSummaryQuery } from '@/apis/portfolioApis';
import { PositionDto } from '@/apis/portfolioApis/types';
import CenteredSpinner from '@/components/CenteredSpinner';

export default function Portfolio() {
  const [feeDiscountRate, setFeeDiscountRate] = useState(loadFeeDiscountRate);
  const {
    data: summaryRes,
    isLoading,
    isFetching,
    isError,
  } = useGetPortfolioSummaryQuery(feeDiscountRate);
  const summary = summaryRes?.data;
  // Background refetch (e.g. right after saving holdings — the summary recomputes prices/
  // dividends/FX live, which can take a few seconds) with no dedicated spinner otherwise: the
  // page kept showing the pre-save numbers with zero feedback that anything was happening.
  const isRefetching = isFetching && !isLoading;
  const [selected, setSelected] = useState<PositionDto | null>(null);

  const handleSaveFeeDiscountRate = (rate: number) => {
    saveFeeDiscountRate(rate);
    setFeeDiscountRate(rate); // triggers a refetch with the new rate — RTK Query caches by arg
  };

  const hasHoldings = (summary?.positions?.length ?? 0) > 0;
  // Auto-launches once per browser the first time all tour target elements exist — see
  // onboardingTour.ts. hasHoldings picks the variant: empty-state intro (where to start) for a
  // brand-new user, full dashboard walkthrough once holdings exist.
  const { startTour } = usePortfolioTour(!isLoading && !isError && !!summary, hasHoldings);

  return (
    <PrivacyProvider>
      <Box sx={{ maxWidth: 1100, mx: 'auto', py: 3 }}>
        {/* Thin top-of-page progress bar for background refetches (e.g. right after saving
            holdings) — the summary recomputes live prices/dividends/FX and can take a few
            seconds, so without this the page just sat on stale numbers with no feedback. */}
        {isRefetching && <LinearProgress sx={{ mb: 2 }} />}
        <PortfolioHeader
          summary={summary}
          hasHoldings={hasHoldings}
          isRefetching={isRefetching}
          feeDiscountRate={feeDiscountRate}
          onSaveFeeDiscountRate={handleSaveFeeDiscountRate}
          onStartTour={startTour}
        />

        {isLoading && <CenteredSpinner sx={{ py: 8 }} />}

        {isError && (
          <Typography color="error" sx={{ py: 4, textAlign: 'center' }}>
            載入失敗，請重新整理
          </Typography>
        )}

        {!isLoading && summary && hasHoldings && (
          <PortfolioDashboard summary={summary} onStockClick={setSelected} />
        )}

        {!isLoading && !isError && summary && !hasHoldings && (
          <Box data-tour="portfolio-tour-empty" sx={{ textAlign: 'center', py: 8 }}>
            <Typography color="text.secondary" sx={{ mb: 1 }}>
              還沒有持倉紀錄
            </Typography>
            <Typography variant="body2" color="text.secondary">
              點右上「建立持倉」填入你的股票、現金和基金
            </Typography>
          </Box>
        )}

        <HoldingDetailDrawer
          position={selected}
          open={selected !== null}
          onClose={() => setSelected(null)}
        />
      </Box>
    </PrivacyProvider>
  );
}

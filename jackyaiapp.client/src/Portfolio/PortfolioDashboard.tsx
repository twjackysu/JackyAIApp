import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Grid from '@mui/material/Grid';

import { AllocationCard } from './AllocationCard';
import { BenchmarkChart } from './BenchmarkChart';
import { ChartSectionCard } from './ChartSectionCard';
import { ConcentrationCard } from './ConcentrationCard';
import { ContributionWaterfallChart } from './ContributionWaterfallChart';
import { DrawdownBrakeCard } from './DrawdownBrakeCard';
import { DrawdownChart } from './DrawdownChart';
import { FinancialChangesChart } from './FinancialChangesChart';
import { FireCard } from './FireCard';
import { LazyMount } from './LazyMount';
import { NetWorthCard } from './NetWorthCard';
import { NetWorthChart } from './NetWorthChart';
import { NetWorthTrendChart } from './NetWorthTrendChart';
import { PositionsTable } from './PositionsTable';
import { RebalancingCard } from './RebalancingCard';

import { PortfolioSummaryDto, PositionDto } from '@/apis/portfolioApis/types';

interface PortfolioDashboardProps {
  summary: PortfolioSummaryDto;
  onStockClick: (position: PositionDto) => void;
}

/** Everything shown once there are holdings: the two summary cards, the charts, the cards that
 * advise, and the positions table. */
export function PortfolioDashboard({ summary, onStockClick }: PortfolioDashboardProps) {
  return (
    <>
      <Grid container spacing={2} sx={{ mb: 3 }}>
        <Grid size={{ xs: 12, md: 8 }}>
          <NetWorthCard summary={summary} />
        </Grid>
        <Grid size={{ xs: 12, md: 4 }}>
          <AllocationCard summary={summary} />
        </Grid>
      </Grid>

      {/* Three charts answering three different questions, deliberately not merged:
          trend ("better or worse off?"), composition ("what am I holding?"), and change
          ("how much moved this period?"). The composition chart alone used to carry all
          three jobs, which is why a 20k move against 10M of assets was invisible on it. */}
      <ChartSectionCard title="淨值走勢">
        <LazyMount minHeight={320}>
          <NetWorthTrendChart />
        </LazyMount>
      </ChartSectionCard>

      <ChartSectionCard title="資產與負債組成" data-tour="portfolio-tour-chart">
        <LazyMount minHeight={400}>
          <NetWorthChart />
        </LazyMount>
      </ChartSectionCard>

      <ChartSectionCard title="淨值變化量">
        <LazyMount minHeight={320}>
          <FinancialChangesChart />
        </LazyMount>
      </ChartSectionCard>

      <ChartSectionCard title="大盤對照">
        <LazyMount minHeight={360}>
          <BenchmarkChart />
        </LazyMount>
      </ChartSectionCard>

      <ChartSectionCard title="報酬貢獻度">
        <ContributionWaterfallChart positions={summary.positions} />
      </ChartSectionCard>

      <ChartSectionCard title="回撤分析">
        <LazyMount minHeight={300}>
          <DrawdownChart />
        </LazyMount>
      </ChartSectionCard>

      <ChartSectionCard title="持股集中度">
        <ConcentrationCard positions={summary.positions} />
      </ChartSectionCard>

      <ChartSectionCard title="再平衡建議">
        <RebalancingCard summary={summary} />
      </ChartSectionCard>

      {/* Renders nothing (and makes no request) unless the server says this user has the
          feature — see DrawdownBrakeCard. */}
      <DrawdownBrakeCard summary={summary} />

      <ChartSectionCard title="財務目標 (FIRE)">
        <FireCard summary={summary} />
      </ChartSectionCard>

      <Card
        data-tour="portfolio-tour-positions"
        elevation={0}
        sx={{ borderRadius: 3, border: '1px solid', borderColor: 'divider' }}
      >
        <CardContent sx={{ p: 2 }}>
          <PositionsTable
            positions={summary.positions}
            liquidGrossAssetsTWD={summary.liquidGrossAssetsTWD}
            onStockClick={onStockClick}
          />
        </CardContent>
      </Card>
    </>
  );
}

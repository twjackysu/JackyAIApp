import Alert from '@mui/material/Alert';
import Skeleton from '@mui/material/Skeleton';

import { TRENDLINE_CHART_HEIGHT, TrendlineChart } from './TrendlineChart';

import type { RangePresetId } from '../utils/trendlineRange';
import type { MarketRegion } from '@/apis/financeApis/types';

import { useGetPriceBarsQuery } from '@/apis/financeApis';

/**
 * The trendline chart for one code, with its loading and unavailable states: the one component
 * behind both the TAIEX card and the stock analysis's K-line section.
 */
export function PriceBarsTrendline({
  code,
  market,
  defaultRange,
  unavailableMessage,
}: {
  /** A stock code, or `TAIEX`. */
  code: string;
  market?: MarketRegion;
  defaultRange: RangePresetId;
  unavailableMessage: string;
}) {
  const { data: res, isError } = useGetPriceBarsQuery(market ? { code, market } : { code });
  const data = res?.data && res.data.bars.length > 0 ? res.data : null;

  if (data) return <TrendlineChart data={data} defaultRange={defaultRange} />;
  if (isError) return <Alert severity="info">{unavailableMessage}</Alert>;
  return <Skeleton variant="rounded" height={TRENDLINE_CHART_HEIGHT} aria-label="載入中" />;
}

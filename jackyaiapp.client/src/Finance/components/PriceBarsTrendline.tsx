import Alert from '@mui/material/Alert';
import Skeleton from '@mui/material/Skeleton';

import { TW_VOLUME, US_VOLUME } from '../utils/trendlineChartOption';

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

  // Volume is a stock's: TW in 張, US in 股. The TAIEX (no market) has none.
  const volume = market === 'TW' ? TW_VOLUME : market === 'US' ? US_VOLUME : undefined;

  if (data) return <TrendlineChart data={data} defaultRange={defaultRange} volume={volume} />;
  if (isError) return <Alert severity="info">{unavailableMessage}</Alert>;
  return <Skeleton variant="rounded" height={TRENDLINE_CHART_HEIGHT} aria-label="載入中" />;
}

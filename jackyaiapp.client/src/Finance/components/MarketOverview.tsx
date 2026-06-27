import Box from '@mui/material/Box';
import { green, grey, red } from '@mui/material/colors';
import Grid from '@mui/material/Grid';
import Paper from '@mui/material/Paper';
import Skeleton from '@mui/material/Skeleton';
import Typography from '@mui/material/Typography';

import type { MarketQuote } from '@/apis/financeApis/types';

import { useGetMarketOverviewQuery } from '@/apis/financeApis';

/** Display order + labels for the dashboard groups. */
const GROUPS: { key: string; label: string }[] = [
  { key: 'US', label: '📈 美國' },
  { key: 'TW', label: '🇹🇼 台灣' },
  { key: 'FX', label: '💵 匯率' },
  { key: 'Other', label: '🪙 其他' },
];

const formatPrice = (price: number | null): string => {
  if (price == null) return '—';
  const decimals = Math.abs(price) < 1 ? 4 : 2;
  return price.toLocaleString('en-US', {
    minimumFractionDigits: decimals,
    maximumFractionDigits: decimals,
  });
};

const QuoteTile = ({ quote }: { quote: MarketQuote }) => {
  const pct = quote.changePercent;
  const color = pct == null ? grey[600] : pct > 0 ? green[600] : pct < 0 ? red[600] : grey[600];
  const sign = pct != null && pct > 0 ? '+' : '';

  return (
    <Paper
      sx={{
        p: 1.5,
        height: '100%',
        border: '1px solid',
        borderColor: 'divider',
        borderRadius: 2,
      }}
    >
      <Typography variant="caption" color="text.secondary" noWrap sx={{ display: 'block' }}>
        {quote.name}
      </Typography>
      <Typography variant="h6" sx={{ fontWeight: 700, lineHeight: 1.3 }}>
        {formatPrice(quote.price)}
      </Typography>
      <Typography variant="body2" sx={{ fontWeight: 600, color }}>
        {quote.change == null || pct == null
          ? '—'
          : `${sign}${quote.change.toFixed(2)} (${sign}${pct.toFixed(2)}%)`}
      </Typography>
    </Paper>
  );
};

export const MarketOverview = () => {
  const { data: apiResponse, isLoading, isError } = useGetMarketOverviewQuery();
  const quotes = apiResponse?.data?.quotes ?? [];

  if (isLoading) {
    return (
      <Box sx={{ mb: 3 }}>
        <Typography variant="h6" gutterBottom sx={{ fontWeight: 'bold' }}>
          📊 市場儀表板
        </Typography>
        <Grid container spacing={1.5}>
          {Array.from({ length: 8 }).map((_, i) => (
            <Grid key={i} size={{ xs: 6, sm: 4, md: 3 }}>
              <Skeleton variant="rounded" height={86} />
            </Grid>
          ))}
        </Grid>
      </Box>
    );
  }

  // Don't block the page if the (free, unofficial) source is down — just hide.
  if (isError || quotes.length === 0) return null;

  return (
    <Box sx={{ mb: 3 }}>
      <Typography variant="h6" gutterBottom sx={{ fontWeight: 'bold' }}>
        📊 市場儀表板
      </Typography>
      {GROUPS.map(({ key, label }) => {
        const groupQuotes = quotes.filter((q) => q.group === key);
        if (groupQuotes.length === 0) return null;
        return (
          <Box key={key} sx={{ mb: 1.5 }}>
            <Typography variant="subtitle2" color="text.secondary" sx={{ mb: 0.5 }}>
              {label}
            </Typography>
            <Grid container spacing={1.5}>
              {groupQuotes.map((q) => (
                <Grid key={q.symbol} size={{ xs: 6, sm: 4, md: 3 }}>
                  <QuoteTile quote={q} />
                </Grid>
              ))}
            </Grid>
          </Box>
        );
      })}
    </Box>
  );
};

export default MarketOverview;

import ShowChartIcon from '@mui/icons-material/ShowChart';
import Box from '@mui/material/Box';
import { green, grey, red } from '@mui/material/colors';
import Grid from '@mui/material/Grid';
import Paper from '@mui/material/Paper';
import Skeleton from '@mui/material/Skeleton';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';

import { BankRateSection } from './macro/BankRateSection';
import { MarginSection } from './macro/MarginSection';
import { SectorSection } from './macro/SectorSection';

import type { MarketQuote } from '@/apis/financeApis/types';

import { useGetMacroEconomyQuery, useGetMarketOverviewQuery } from '@/apis/financeApis';
import { fmtNum } from '@/utils/format';

/** Display order + labels for the dashboard groups. */
const GROUPS: { key: string; label: string }[] = [
  { key: 'US', label: '美國' },
  { key: 'TW', label: '台灣' },
  { key: 'FX', label: '匯率' },
  { key: 'Other', label: '其他' },
];

const formatPrice = (price: number | null): string => {
  if (price == null) return '—';
  // Sub-$1 quotes (e.g. some FX pairs) need finer precision than typical stock prices.
  const decimals = Math.abs(price) < 1 ? 4 : 2;
  return fmtNum(price, decimals);
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

const Heading = () => (
  <Stack direction="row" spacing={0.5} sx={{ alignItems: 'center', mb: 1 }}>
    <ShowChartIcon fontSize="small" />
    <Typography variant="h6" sx={{ fontWeight: 'bold' }}>
      市場概覽
    </Typography>
  </Stack>
);

/**
 * Unified market overview: cross-market snapshot tiles (US / TW / FX / crypto / rates)
 * plus Taiwan-specific depth (sector indices, margin trading, bank rates). Replaces the
 * former separate "市場儀表板" + "總體經濟概覽" sections; overlapping items (TAIEX, spot FX)
 * live only in the tiles.
 */
export const MarketOverview = () => {
  const { data: mkt, isLoading } = useGetMarketOverviewQuery();
  const { data: macroRes } = useGetMacroEconomyQuery();
  const quotes = mkt?.data?.quotes ?? [];
  const macro = macroRes?.data;

  if (isLoading) {
    return (
      <Box sx={{ mb: 3 }}>
        <Heading />
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

  const hasTiles = quotes.length > 0;
  const hasMacroDetail =
    !!macro && (macro.sectorIndices.length > 0 || !!macro.margin || !!macro.bankRate);

  // Both upstreams down (e.g. the free sources are unavailable) — hide rather than show an error.
  if (!hasTiles && !hasMacroDetail) return null;

  return (
    <Box sx={{ mb: 3 }}>
      <Heading />

      {hasTiles &&
        GROUPS.map(({ key, label }) => {
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

      {hasMacroDetail && macro && (
        <Grid container spacing={2} sx={{ mt: hasTiles ? 0.5 : 0 }}>
          {macro.sectorIndices.length > 0 && (
            <Grid size={{ xs: 12, md: 6 }}>
              <SectorSection sectors={macro.sectorIndices} />
            </Grid>
          )}
          {macro.margin && (
            <Grid size={{ xs: 12, md: 6 }}>
              <MarginSection data={macro} />
            </Grid>
          )}
          {macro.bankRate && (
            <Grid size={{ xs: 12, md: 6 }}>
              <BankRateSection data={macro} />
            </Grid>
          )}
        </Grid>
      )}
    </Box>
  );
};

export default MarketOverview;

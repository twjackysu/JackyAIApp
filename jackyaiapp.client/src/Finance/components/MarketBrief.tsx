import AutoAwesomeIcon from '@mui/icons-material/AutoAwesome';
import TrendingDownIcon from '@mui/icons-material/TrendingDown';
import TrendingFlatIcon from '@mui/icons-material/TrendingFlat';
import TrendingUpIcon from '@mui/icons-material/TrendingUp';
import Box from '@mui/material/Box';
import { green, grey, red } from '@mui/material/colors';
import Paper from '@mui/material/Paper';
import Skeleton from '@mui/material/Skeleton';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';

import { useGetMarketBriefQuery } from '@/apis/financeApis';

const SentimentIcon = ({ sentiment }: { sentiment: string }) => {
  if (sentiment === 'bullish')
    return <TrendingUpIcon fontSize="small" sx={{ color: green[600] }} />;
  if (sentiment === 'bearish')
    return <TrendingDownIcon fontSize="small" sx={{ color: red[600] }} />;
  return <TrendingFlatIcon fontSize="small" sx={{ color: grey[600] }} />;
};

const Heading = () => (
  <Stack direction="row" spacing={0.5} sx={{ alignItems: 'center', mb: 1 }}>
    <AutoAwesomeIcon fontSize="small" color="primary" />
    <Typography variant="h6" sx={{ fontWeight: 'bold' }}>
      今日市場重點
    </Typography>
    <Typography variant="caption" color="text.secondary" sx={{ ml: 0.5 }}>
      AI 依今日數據整理，僅供參考
    </Typography>
  </Stack>
);

/**
 * "今日市場重點" — a few AI-synthesised takeaways from the day's market data, so the user
 * can grasp what matters in a couple of minutes. Grounded in the market-overview numbers.
 */
export const MarketBrief = () => {
  const { data, isLoading } = useGetMarketBriefQuery();
  const points = data?.data?.points ?? [];

  if (isLoading) {
    return (
      <Paper sx={{ p: 2, mb: 3, border: '1px solid', borderColor: 'divider', borderRadius: 2 }}>
        <Heading />
        <Stack spacing={1}>
          {Array.from({ length: 3 }).map((_, i) => (
            <Skeleton key={i} variant="text" height={28} />
          ))}
        </Stack>
      </Paper>
    );
  }

  // Nice-to-have — hide silently if the brief can't be produced.
  if (points.length === 0) return null;

  return (
    <Paper sx={{ p: 2, mb: 3, border: '1px solid', borderColor: 'divider', borderRadius: 2 }}>
      <Heading />
      <Stack spacing={1.25}>
        {points.map((p, i) => (
          <Stack key={i} direction="row" spacing={1} sx={{ alignItems: 'flex-start' }}>
            <Box sx={{ mt: '2px' }}>
              <SentimentIcon sentiment={p.sentiment} />
            </Box>
            <Box>
              <Typography variant="body2" sx={{ fontWeight: 600 }}>
                {p.headline}
              </Typography>
              {p.detail && (
                <Typography variant="caption" color="text.secondary">
                  {p.detail}
                </Typography>
              )}
            </Box>
          </Stack>
        ))}
      </Stack>
    </Paper>
  );
};

export default MarketBrief;

import AutoAwesomeIcon from '@mui/icons-material/AutoAwesome';
import TrendingDownIcon from '@mui/icons-material/TrendingDown';
import TrendingFlatIcon from '@mui/icons-material/TrendingFlat';
import TrendingUpIcon from '@mui/icons-material/TrendingUp';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import CircularProgress from '@mui/material/CircularProgress';
import { green, grey, red } from '@mui/material/colors';
import Divider from '@mui/material/Divider';
import Skeleton from '@mui/material/Skeleton';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';

import { useLazyGetMarketBriefQuery } from '@/apis/financeApis';

const SentimentIcon = ({ sentiment }: { sentiment: string }) => {
  if (sentiment === 'bullish')
    return <TrendingUpIcon fontSize="small" sx={{ color: green[600] }} />;
  if (sentiment === 'bearish')
    return <TrendingDownIcon fontSize="small" sx={{ color: red[600] }} />;
  return <TrendingFlatIcon fontSize="small" sx={{ color: grey[600] }} />;
};

export const MarketBrief = () => {
  const [fetchBrief, { data, isFetching, isError }] = useLazyGetMarketBriefQuery();
  const points = data?.data?.points ?? [];

  if (!data && !isFetching) {
    return (
      <Box sx={{ mt: 2 }}>
        <Divider sx={{ mb: 2 }} />
        <Button
          variant="outlined"
          size="small"
          startIcon={<AutoAwesomeIcon />}
          onClick={() => fetchBrief()}
        >
          產生 AI 市場重點
        </Button>
      </Box>
    );
  }

  if (isFetching) {
    return (
      <Box sx={{ mt: 2 }}>
        <Divider sx={{ mb: 2 }} />
        <Stack direction="row" spacing={1} sx={{ alignItems: 'center', mb: 1.5 }}>
          <CircularProgress size={14} />
          <Typography variant="body2" color="text.secondary">
            AI 分析中…
          </Typography>
        </Stack>
        <Stack spacing={1}>
          {Array.from({ length: 3 }).map((_, i) => (
            <Skeleton key={i} variant="text" height={28} />
          ))}
        </Stack>
      </Box>
    );
  }

  if (isError || points.length === 0) return null;

  return (
    <Box sx={{ mt: 2 }}>
      <Divider sx={{ mb: 2 }} />
      <Stack direction="row" spacing={0.5} sx={{ alignItems: 'center', mb: 1 }}>
        <AutoAwesomeIcon fontSize="small" color="primary" />
        <Typography variant="subtitle2" sx={{ fontWeight: 700 }}>
          AI 市場重點
        </Typography>
        <Typography variant="caption" color="text.secondary" sx={{ ml: 0.5 }}>
          依今日數據整理，僅供參考
        </Typography>
      </Stack>
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
    </Box>
  );
};

export default MarketBrief;

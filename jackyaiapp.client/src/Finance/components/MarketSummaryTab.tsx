import BusinessIcon from '@mui/icons-material/Business';
import PublicIcon from '@mui/icons-material/Public';
import RefreshIcon from '@mui/icons-material/Refresh';
import TrendingDownIcon from '@mui/icons-material/TrendingDown';
import TrendingFlatIcon from '@mui/icons-material/TrendingFlat';
import TrendingUpIcon from '@mui/icons-material/TrendingUp';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import CircularProgress from '@mui/material/CircularProgress';
import { green, grey, red } from '@mui/material/colors';
import IconButton from '@mui/material/IconButton';
import Paper from '@mui/material/Paper';
import Skeleton from '@mui/material/Skeleton';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';

import type { DigestPoint } from '@/apis/financeApis/types';

interface MarketSummaryTabProps {
  isAuthenticated: boolean;
  isMarketLoading: boolean;
  isMarketFetching: boolean;
  isMarketError: boolean;
  marketError: unknown;
  digestPoints: DigestPoint[];
  refetch: () => void;
}

const SentimentIcon = ({ sentiment }: { sentiment: string }) => {
  if (sentiment === 'bullish')
    return <TrendingUpIcon fontSize="small" sx={{ color: green[600] }} />;
  if (sentiment === 'bearish')
    return <TrendingDownIcon fontSize="small" sx={{ color: red[600] }} />;
  return <TrendingFlatIcon fontSize="small" sx={{ color: grey[600] }} />;
};

const PointRow = ({ point }: { point: DigestPoint }) => (
  <Stack direction="row" spacing={1} sx={{ alignItems: 'flex-start' }}>
    <Box sx={{ mt: '2px' }}>
      <SentimentIcon sentiment={point.sentiment} />
    </Box>
    <Box>
      <Stack direction="row" spacing={0.75} sx={{ alignItems: 'center' }}>
        <Typography variant="body2" sx={{ fontWeight: 600 }}>
          {point.headline}
        </Typography>
        {point.stockCode && (
          <Chip label={point.stockCode} size="small" variant="outlined" sx={{ height: 18 }} />
        )}
      </Stack>
      {point.detail && (
        <Typography variant="caption" color="text.secondary">
          {point.detail}
        </Typography>
      )}
    </Box>
  </Stack>
);

const Section = ({
  icon,
  title,
  points,
}: {
  icon: React.ReactNode;
  title: string;
  points: DigestPoint[];
}) => {
  if (points.length === 0) return null;
  return (
    <Paper sx={{ p: 2.5, border: '1px solid', borderColor: 'divider', borderRadius: 2 }}>
      <Stack direction="row" spacing={0.75} sx={{ alignItems: 'center', mb: 1.5 }}>
        {icon}
        <Typography variant="subtitle1" sx={{ fontWeight: 700 }}>
          {title}
        </Typography>
      </Stack>
      <Stack spacing={1.5}>
        {points.map((p, i) => (
          <PointRow key={i} point={p} />
        ))}
      </Stack>
    </Paper>
  );
};

export function MarketSummaryTab({
  isAuthenticated,
  isMarketLoading,
  isMarketFetching,
  isMarketError,
  marketError,
  digestPoints,
  refetch,
}: MarketSummaryTabProps) {
  if (!isAuthenticated) {
    return (
      <Alert
        severity="info"
        action={
          <Button
            color="inherit"
            size="small"
            onClick={() => {
              window.location.href = '/api/account/login/Google';
            }}
          >
            登入
          </Button>
        }
      >
        今日市場AI摘要需要登入，每日首次查看消耗 3 點使用額度（當日重看免費）
      </Alert>
    );
  }

  if (isMarketLoading || isMarketFetching) {
    return (
      <Box>
        <Stack direction="row" spacing={1} sx={{ alignItems: 'center', mb: 2 }}>
          <CircularProgress size={16} />
          <Typography variant="body2" color="text.secondary">
            AI 正在彙整今日全球與台股重點…
          </Typography>
        </Stack>
        <Stack spacing={2}>
          <Skeleton variant="rounded" height={160} />
          <Skeleton variant="rounded" height={160} />
        </Stack>
      </Box>
    );
  }

  if (isMarketError) {
    const message =
      typeof marketError === 'object' &&
      marketError !== null &&
      'data' in marketError &&
      typeof (marketError as { data?: { error?: { message?: string } } }).data?.error?.message ===
        'string'
        ? (marketError as { data: { error: { message: string } } }).data.error.message
        : '無法產生今日市場摘要，請稍後再試。';
    return (
      <Alert
        severity="error"
        action={
          <IconButton color="inherit" size="small" onClick={() => refetch()}>
            <RefreshIcon />
          </IconButton>
        }
      >
        {message}
      </Alert>
    );
  }

  if (digestPoints.length === 0) {
    return <Alert severity="info">今日暫無市場摘要。</Alert>;
  }

  const global = digestPoints.filter((p) => p.category !== 'taiwan');
  const taiwan = digestPoints.filter((p) => p.category === 'taiwan');

  return (
    <Stack spacing={2}>
      <Typography variant="caption" color="text.secondary">
        AI 依今日全球市場數據、財經新聞與台股公司公告整理，僅供參考
      </Typography>
      <Section
        icon={<PublicIcon fontSize="small" color="primary" />}
        title="全球總經"
        points={global}
      />
      <Section
        icon={<BusinessIcon fontSize="small" color="primary" />}
        title="台股公司動態"
        points={taiwan}
      />
    </Stack>
  );
}

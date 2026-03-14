import TrendingUpIcon from '@mui/icons-material/TrendingUp';
import TrendingDownIcon from '@mui/icons-material/TrendingDown';
import TrendingFlatIcon from '@mui/icons-material/TrendingFlat';
import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Chip from '@mui/material/Chip';
import Divider from '@mui/material/Divider';
import Grid from '@mui/material/Grid';
import Paper from '@mui/material/Paper';
import Skeleton from '@mui/material/Skeleton';
import Stack from '@mui/material/Stack';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableContainer from '@mui/material/TableContainer';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import Typography from '@mui/material/Typography';
import Alert from '@mui/material/Alert';
import { green, red, grey } from '@mui/material/colors';

import { useGetMacroEconomyQuery } from '@/apis/financeApis';
import type { MacroEconomyData, MarketIndexDay, SectorIndex } from '@/apis/financeApis/types';

const formatNumber = (n: number): string => n.toLocaleString('zh-TW');
const formatBillion = (n: number): string => `${(n / 1_000_000_000).toFixed(1)}B`;
const formatVolumeBillion = (n: number): string => `${(n / 100_000_000).toFixed(1)}億`;

/** Convert 民國 date "1150211" to "02/11" display */
const formatROCDate = (d: string): string => {
  if (d.length < 7) return d;
  return `${d.slice(3, 5)}/${d.slice(5, 7)}`;
};

const ChangeText = ({ value, suffix = '' }: { value: number; suffix?: string }) => {
  const color = value > 0 ? green[600] : value < 0 ? red[600] : grey[600];
  const prefix = value > 0 ? '+' : '';
  return (
    <Typography variant="body2" fontWeight="bold" sx={{ color }}>
      {prefix}{value.toFixed(2)}{suffix}
    </Typography>
  );
};

const DirectionIcon = ({ direction }: { direction: string }) => {
  if (direction === '+') return <TrendingUpIcon sx={{ color: green[600], fontSize: 18 }} />;
  if (direction === '-') return <TrendingDownIcon sx={{ color: red[600], fontSize: 18 }} />;
  return <TrendingFlatIcon sx={{ color: grey[500], fontSize: 18 }} />;
};

// === Sub-sections ===

const MarketIndexSection = ({ data }: { data: MarketIndexDay[] }) => {
  if (data.length === 0) return null;
  const latest = data[data.length - 1];
  const changePercent = latest.taiex > 0 ? (latest.change / (latest.taiex - latest.change)) * 100 : 0;

  return (
    <Card sx={{ height: '100%' }}>
      <CardContent>
        <Typography variant="subtitle2" color="text.secondary" gutterBottom>📈 加權指數 (TAIEX)</Typography>
        <Typography variant="h4" fontWeight="bold">{formatNumber(Math.round(latest.taiex))}</Typography>
        <Stack direction="row" spacing={1} alignItems="center" sx={{ mt: 0.5 }}>
          <ChangeText value={latest.change} />
          <ChangeText value={changePercent} suffix="%" />
        </Stack>
        <Divider sx={{ my: 1.5 }} />
        <Stack spacing={0.5}>
          <Typography variant="caption" color="text.secondary">
            成交量: {formatVolumeBillion(latest.tradeVolume)}股
          </Typography>
          <Typography variant="caption" color="text.secondary">
            成交值: NT${formatBillion(latest.tradeValue)}
          </Typography>
          <Typography variant="caption" color="text.secondary">
            成交筆數: {formatNumber(latest.transaction)}
          </Typography>
        </Stack>
        {data.length > 1 && (
          <>
            <Divider sx={{ my: 1.5 }} />
            <Typography variant="caption" color="text.secondary" fontWeight="bold">近期走勢</Typography>
            <Stack spacing={0.3} sx={{ mt: 0.5 }}>
              {data.slice(-5).map((d) => (
                <Stack key={d.date} direction="row" justifyContent="space-between" alignItems="center">
                  <Typography variant="caption" color="text.secondary">{formatROCDate(d.date)}</Typography>
                  <Typography variant="caption">{formatNumber(Math.round(d.taiex))}</Typography>
                  <ChangeText value={d.change} />
                </Stack>
              ))}
            </Stack>
          </>
        )}
      </CardContent>
    </Card>
  );
};

const SectorSection = ({ sectors }: { sectors: SectorIndex[] }) => {
  if (sectors.length === 0) return null;

  const sectorDisplayNames: Record<string, string> = {
    '發行量加權股價指數': '加權指數',
    '半導體類指數': '半導體',
    '電子工業類指數': '電子工業',
    '金融保險類指數': '金融保險',
    '航運類指數': '航運',
    '臺灣50指數': '台灣50',
    '生技醫療類指數': '生技醫療',
    '通信網路類指數': '通信網路',
  };

  // Exclude 加權指數 since it's already shown in the main card
  const filtered = sectors.filter(s => s.name !== '發行量加權股價指數');

  return (
    <Card sx={{ height: '100%' }}>
      <CardContent>
        <Typography variant="subtitle2" color="text.secondary" gutterBottom>🏭 產業類指數</Typography>
        <TableContainer>
          <Table size="small">
            <TableHead>
              <TableRow>
                <TableCell>產業</TableCell>
                <TableCell align="right">指數</TableCell>
                <TableCell align="right">漲跌</TableCell>
                <TableCell align="right">幅度</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {filtered.map(s => (
                <TableRow key={s.name} hover>
                  <TableCell>
                    <Stack direction="row" spacing={0.5} alignItems="center">
                      <DirectionIcon direction={s.direction} />
                      <Typography variant="body2">{sectorDisplayNames[s.name] ?? s.name}</Typography>
                    </Stack>
                  </TableCell>
                  <TableCell align="right"><Typography variant="body2">{formatNumber(Math.round(s.closeIndex))}</Typography></TableCell>
                  <TableCell align="right"><ChangeText value={s.direction === '-' ? -s.changePoints : s.changePoints} /></TableCell>
                  <TableCell align="right"><ChangeText value={s.direction === '-' ? -s.changePercent : s.changePercent} suffix="%" /></TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </TableContainer>
      </CardContent>
    </Card>
  );
};

const MarginSection = ({ data }: { data: MacroEconomyData }) => {
  if (!data.margin) return null;
  const m = data.margin;
  const marginNet = m.marginBuyTotal - m.marginSellTotal;
  const shortNet = m.shortSellTotal - m.shortBuyTotal;

  return (
    <Card sx={{ height: '100%' }}>
      <CardContent>
        <Typography variant="subtitle2" color="text.secondary" gutterBottom>💰 融資融券</Typography>
        <Grid container spacing={2}>
          <Grid size={6}>
            <Typography variant="caption" color="text.secondary">融資</Typography>
            <Stack spacing={0.3}>
              <Typography variant="body2">買進: {formatNumber(m.marginBuyTotal)}</Typography>
              <Typography variant="body2">賣出: {formatNumber(m.marginSellTotal)}</Typography>
              <Stack direction="row" spacing={0.5} alignItems="center">
                <Typography variant="body2">增減:</Typography>
                <ChangeText value={marginNet} />
              </Stack>
              <Typography variant="body2" fontWeight="bold">餘額: {formatNumber(m.marginBalanceTotal)}</Typography>
            </Stack>
          </Grid>
          <Grid size={6}>
            <Typography variant="caption" color="text.secondary">融券</Typography>
            <Stack spacing={0.3}>
              <Typography variant="body2">賣出: {formatNumber(m.shortSellTotal)}</Typography>
              <Typography variant="body2">買進: {formatNumber(m.shortBuyTotal)}</Typography>
              <Stack direction="row" spacing={0.5} alignItems="center">
                <Typography variant="body2">增減:</Typography>
                <ChangeText value={shortNet} />
              </Stack>
              <Typography variant="body2" fontWeight="bold">餘額: {formatNumber(m.shortBalanceTotal)}</Typography>
            </Stack>
          </Grid>
        </Grid>
      </CardContent>
    </Card>
  );
};

const FxAndRateSection = ({ data }: { data: MacroEconomyData }) => {
  return (
    <Card sx={{ height: '100%' }}>
      <CardContent>
        <Typography variant="subtitle2" color="text.secondary" gutterBottom>💱 匯率與利率</Typography>
        {data.exchangeRates.length > 0 && (
          <TableContainer>
            <Table size="small">
              <TableHead>
                <TableRow>
                  <TableCell>幣別</TableCell>
                  <TableCell align="right">買入</TableCell>
                  <TableCell align="right">賣出</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {data.exchangeRates.map(fx => (
                  <TableRow key={fx.currency} hover>
                    <TableCell>
                      <Typography variant="body2">{fx.displayName} ({fx.currency})</Typography>
                    </TableCell>
                    <TableCell align="right"><Typography variant="body2">{fx.buyRate?.toFixed(4) ?? '-'}</Typography></TableCell>
                    <TableCell align="right"><Typography variant="body2">{fx.sellRate?.toFixed(4) ?? '-'}</Typography></TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </TableContainer>
        )}
        {data.bankRate && (
          <>
            <Divider sx={{ my: 1.5 }} />
            <Typography variant="caption" color="text.secondary" fontWeight="bold">
              銀行利率 ({data.bankRate.bankName})
            </Typography>
            <Stack spacing={0.3} sx={{ mt: 0.5 }}>
              <Stack direction="row" justifyContent="space-between">
                <Typography variant="body2">一年期定存(固定)</Typography>
                <Chip label={`${data.bankRate.oneYearFixed?.toFixed(3) ?? '-'}%`} size="small" variant="outlined" />
              </Stack>
              <Stack direction="row" justifyContent="space-between">
                <Typography variant="body2">一年期定存(機動)</Typography>
                <Chip label={`${data.bankRate.oneYearFloating?.toFixed(3) ?? '-'}%`} size="small" variant="outlined" />
              </Stack>
              {data.bankRate.baseLendingRate && (
                <Stack direction="row" justifyContent="space-between">
                  <Typography variant="body2">基準利率</Typography>
                  <Chip label={`${data.bankRate.baseLendingRate.toFixed(3)}%`} size="small" color="primary" variant="outlined" />
                </Stack>
              )}
            </Stack>
          </>
        )}
      </CardContent>
    </Card>
  );
};

// === Loading skeleton ===
const MacroSkeleton = () => (
  <Grid container spacing={2}>
    {[1, 2, 3, 4].map(i => (
      <Grid
        key={i}
        size={{
          xs: 12,
          sm: 6,
          md: 3
        }}>
        <Paper sx={{ p: 2 }}>
          <Skeleton variant="text" width="60%" />
          <Skeleton variant="rectangular" height={120} sx={{ mt: 1 }} />
        </Paper>
      </Grid>
    ))}
  </Grid>
);

// === Main Component ===
export const MacroEconomyOverview = () => {
  const { data: apiResponse, isLoading, isError } = useGetMacroEconomyQuery();
  const data = apiResponse?.data;

  if (isLoading) return <MacroSkeleton />;
  if (isError) return <Alert severity="warning" sx={{ mb: 2 }}>總體經濟資料載入失敗</Alert>;
  if (!data) return null;

  return (
    <Box sx={{ mb: 3 }}>
      <Typography variant="h6" fontWeight="bold" gutterBottom>
        🏛️ 總體經濟概覽
      </Typography>
      <Grid container spacing={2}>
        <Grid
          size={{
            xs: 12,
            md: 6
          }}>
          <MarketIndexSection data={data.marketIndex} />
        </Grid>
        <Grid
          size={{
            xs: 12,
            md: 6
          }}>
          <SectorSection sectors={data.sectorIndices} />
        </Grid>
        <Grid
          size={{
            xs: 12,
            md: 6
          }}>
          <MarginSection data={data} />
        </Grid>
        <Grid
          size={{
            xs: 12,
            md: 6
          }}>
          <FxAndRateSection data={data} />
        </Grid>
      </Grid>
    </Box>
  );
};

export default MacroEconomyOverview;

import Chip from '@mui/material/Chip';
import CircularProgress from '@mui/material/CircularProgress';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableContainer from '@mui/material/TableContainer';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import Typography from '@mui/material/Typography';
import Box from '@mui/material/Box';

import { HoldingDto } from '@/apis/portfolioApis/types';

interface Props {
  holdings: HoldingDto[];
  usdTwdRate: number;
  loading?: boolean;
}

const fmt = (n: number, decimals = 0) =>
  n.toLocaleString('zh-TW', { minimumFractionDigits: decimals, maximumFractionDigits: decimals });

const pnlColor = (n: number) => (n > 0 ? '#22c55e' : n < 0 ? '#ef4444' : 'text.secondary');

export function HoldingsTable({ holdings, usdTwdRate, loading }: Props) {
  if (loading) {
    return (
      <Box sx={{ display: 'flex', justifyContent: 'center', py: 6 }}>
        <CircularProgress />
      </Box>
    );
  }

  if (holdings.length === 0) {
    return (
      <Typography color="text.secondary" sx={{ py: 4, textAlign: 'center' }}>
        尚無持倉，點「新增交易」開始記錄
      </Typography>
    );
  }

  return (
    <TableContainer>
      <Table size="small">
        <TableHead>
          <TableRow>
            <TableCell>股票</TableCell>
            <TableCell align="right">股數</TableCell>
            <TableCell align="right">均價</TableCell>
            <TableCell align="right">現價</TableCell>
            <TableCell align="right">市值 (TWD)</TableCell>
            <TableCell align="right">未實現損益</TableCell>
            <TableCell align="right">損益率</TableCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {holdings.map((h) => (
            <TableRow key={`${h.market}-${h.symbol}`} hover>
              <TableCell>
                <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                  <Box>
                    <Typography variant="body2" sx={{ fontWeight: 700 }}>
                      {h.symbol}
                    </Typography>
                    <Typography variant="caption" color="text.secondary">
                      {h.companyName}
                    </Typography>
                  </Box>
                  <Chip
                    label={h.market}
                    size="small"
                    sx={{ fontSize: 10, height: 18, ml: 0.5 }}
                    color={h.market === 'TW' ? 'primary' : 'warning'}
                    variant="outlined"
                  />
                  {h.priceFetchFailed && (
                    <Chip label="報價失敗" size="small" color="error" variant="outlined" sx={{ fontSize: 10, height: 18 }} />
                  )}
                </Box>
              </TableCell>
              <TableCell align="right">{fmt(h.quantity)}</TableCell>
              <TableCell align="right">
                {h.currency} {fmt(h.avgCost, 2)}
              </TableCell>
              <TableCell align="right">
                {h.priceFetchFailed ? '—' : `${h.currency} ${fmt(h.currentPrice, 2)}`}
              </TableCell>
              <TableCell align="right">
                <Typography variant="body2">
                  {fmt(h.marketValueTWD)}
                </Typography>
                {h.market === 'US' && (
                  <Typography variant="caption" color="text.secondary">
                    USD {fmt(h.marketValueLocal, 0)} × {usdTwdRate.toFixed(2)}
                  </Typography>
                )}
              </TableCell>
              <TableCell align="right">
                <Typography variant="body2" sx={{ color: pnlColor(h.unrealizedPnlTWD) }}>
                  {h.unrealizedPnlTWD >= 0 ? '+' : ''}{fmt(h.unrealizedPnlTWD)}
                </Typography>
              </TableCell>
              <TableCell align="right">
                <Typography variant="body2" sx={{ color: pnlColor(h.unrealizedPnlPct) }}>
                  {h.unrealizedPnlPct >= 0 ? '+' : ''}{h.unrealizedPnlPct.toFixed(2)}%
                </Typography>
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </TableContainer>
  );
}

import Box from '@mui/material/Box';
import Chip from '@mui/material/Chip';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableContainer from '@mui/material/TableContainer';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import Typography from '@mui/material/Typography';

import { usePrivacy } from './privacy';

import { PositionDto } from '@/apis/portfolioApis/types';

interface Props {
  positions: PositionDto[];
  onStockClick?: (p: PositionDto) => void;
}

const fmt = (n: number, d = 0) =>
  n.toLocaleString('zh-TW', { minimumFractionDigits: d, maximumFractionDigits: d });

const pnlColor = (n: number) => (n > 0 ? '#22c55e' : n < 0 ? '#ef4444' : 'text.secondary');

const TYPE_CHIP: Record<
  string,
  { label: string; color: 'primary' | 'warning' | 'success' | 'info' }
> = {
  STOCK: { label: '股票', color: 'primary' },
  CASH: { label: '現金', color: 'success' },
  FUND: { label: '基金', color: 'info' },
};

export function PositionsTable({ positions, onStockClick }: Props) {
  const { money } = usePrivacy();
  if (positions.length === 0) {
    return (
      <Typography color="text.secondary" sx={{ py: 4, textAlign: 'center' }}>
        尚無持倉，點「建立持倉」開始
      </Typography>
    );
  }

  return (
    <TableContainer>
      <Table size="small">
        <TableHead>
          <TableRow>
            <TableCell>項目</TableCell>
            <TableCell align="right">數量</TableCell>
            <TableCell align="right">現價/NAV</TableCell>
            <TableCell align="right">市值 (TWD)</TableCell>
            <TableCell align="right">未實現損益</TableCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {positions.map((p, i) => {
            const isStock = p.assetType === 'STOCK';
            // Stocks and funds (with a fundId) have a price chart; cash does not.
            const clickable = (isStock || (p.assetType === 'FUND' && !!p.symbol)) && !!onStockClick;
            const chip = TYPE_CHIP[p.assetType] ?? TYPE_CHIP.STOCK;
            return (
              <TableRow
                key={`${p.assetType}-${p.symbol}-${p.name}-${i}`}
                hover
                onClick={() => clickable && onStockClick?.(p)}
                sx={{ cursor: clickable ? 'pointer' : 'default' }}
              >
                <TableCell>
                  <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                    <Box>
                      <Typography variant="body2" sx={{ fontWeight: 700 }}>
                        {p.symbol || p.name}
                      </Typography>
                      {p.symbol && (
                        <Typography variant="caption" color="text.secondary">
                          {p.name}
                        </Typography>
                      )}
                    </Box>
                    <Chip
                      label={isStock && p.market ? p.market : chip.label}
                      size="small"
                      color={chip.color}
                      variant="outlined"
                      sx={{ fontSize: 10, height: 18 }}
                    />
                    {p.priceFetchFailed && (
                      <Chip
                        label="報價失敗"
                        size="small"
                        color="error"
                        variant="outlined"
                        sx={{ fontSize: 10, height: 18 }}
                      />
                    )}
                  </Box>
                </TableCell>
                <TableCell align="right">
                  {p.assetType === 'CASH' ? '—' : fmt(p.quantity, p.assetType === 'FUND' ? 2 : 0)}
                </TableCell>
                <TableCell align="right">
                  {p.assetType === 'CASH' ? '—' : `${p.currency} ${money(fmt(p.currentPrice, 2))}`}
                </TableCell>
                <TableCell align="right">
                  <Typography variant="body2">{money(fmt(p.marketValueTWD))}</Typography>
                  {p.currency === 'USD' && (
                    <Typography variant="caption" color="text.secondary">
                      USD {money(fmt(p.marketValueLocal, 0))}
                    </Typography>
                  )}
                </TableCell>
                <TableCell align="right">
                  {p.unrealizedPnlTWD != null && p.unrealizedPnlPct != null ? (
                    <>
                      <Typography variant="body2" sx={{ color: pnlColor(p.unrealizedPnlTWD) }}>
                        {money(`${p.unrealizedPnlTWD >= 0 ? '+' : ''}${fmt(p.unrealizedPnlTWD)}`)}
                      </Typography>
                      <Typography variant="caption" sx={{ color: pnlColor(p.unrealizedPnlPct) }}>
                        {p.unrealizedPnlPct >= 0 ? '+' : ''}
                        {p.unrealizedPnlPct.toFixed(2)}%
                      </Typography>
                    </>
                  ) : (
                    <Typography variant="caption" color="text.secondary">
                      —
                    </Typography>
                  )}
                </TableCell>
              </TableRow>
            );
          })}
        </TableBody>
      </Table>
    </TableContainer>
  );
}

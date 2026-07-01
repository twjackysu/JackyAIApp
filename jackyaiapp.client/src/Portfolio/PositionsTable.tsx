import Box from '@mui/material/Box';
import Chip from '@mui/material/Chip';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableContainer from '@mui/material/TableContainer';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import Typography from '@mui/material/Typography';

import { fmtNum as fmt, fmtQty, pnlColor } from './portfolioUtils';
import { usePrivacy } from './privacy';

import { PositionDto } from '@/apis/portfolioApis/types';

interface Props {
  positions: PositionDto[];
  onStockClick?: (p: PositionDto) => void;
}

const TYPE_CHIP: Record<
  string,
  { label: string; color: 'primary' | 'warning' | 'success' | 'info' }
> = {
  STOCK: { label: '股票', color: 'primary' },
  CASH: { label: '現金', color: 'success' },
  FUND: { label: '基金', color: 'info' },
};

export function PositionsTable({ positions, onStockClick }: Props) {
  const { value, change, showRatio } = usePrivacy();
  const total = positions.reduce((s, p) => s + p.marketValueTWD, 0);
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
            <TableCell align="right">占比</TableCell>
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
                    {p.assetType !== 'CASH' && p.exposureFactor !== 1 && (
                      <Chip
                        label={p.exposureFactor === 0 ? '0曝險' : `曝險×${p.exposureFactor}`}
                        size="small"
                        color={p.exposureFactor === 0 ? 'default' : 'warning'}
                        variant="outlined"
                        sx={{ fontSize: 10, height: 18 }}
                      />
                    )}
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
                  {p.assetType === 'CASH' ? '—' : fmtQty(p.quantity)}
                </TableCell>
                <TableCell align="right">
                  {p.assetType === 'CASH' ? '—' : `${p.currency} ${value(fmt(p.currentPrice, 2))}`}
                </TableCell>
                <TableCell align="right">
                  <Typography variant="body2">{value(fmt(p.marketValueTWD))}</Typography>
                  {p.currency === 'USD' && (
                    <Typography variant="caption" color="text.secondary">
                      USD {value(fmt(p.marketValueLocal, 0))}
                    </Typography>
                  )}
                </TableCell>
                <TableCell align="right">
                  {showRatio && total > 0
                    ? `${((p.marketValueTWD / total) * 100).toFixed(1)}%`
                    : '—'}
                </TableCell>
                <TableCell align="right">
                  {p.unrealizedPnlTWD != null && p.unrealizedPnlPct != null ? (
                    <>
                      <Typography variant="body2" sx={{ color: pnlColor(p.unrealizedPnlTWD) }}>
                        {change(`${p.unrealizedPnlTWD >= 0 ? '+' : ''}${fmt(p.unrealizedPnlTWD)}`)}
                      </Typography>
                      {showRatio && (
                        <Typography variant="caption" sx={{ color: pnlColor(p.unrealizedPnlPct) }}>
                          {p.unrealizedPnlPct >= 0 ? '+' : ''}
                          {p.unrealizedPnlPct.toFixed(2)}%
                        </Typography>
                      )}
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

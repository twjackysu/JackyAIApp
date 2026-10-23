import Box from '@mui/material/Box';
import Chip from '@mui/material/Chip';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableContainer from '@mui/material/TableContainer';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import Typography from '@mui/material/Typography';
import useMediaQuery from '@mui/material/useMediaQuery';

import { loadFeeDiscountRate } from '../feeDiscountRate';
import { liquidSharePct, POSITION_SHARE_TOOLTIP } from '../positionShare';
import { usePrivacy } from '../privacy';

import { PositionChips } from './PositionChips';
import { isFlatValued, opensDetail } from './positionDisplay';
import { PositionsCardList } from './PositionsCardList';
import { PriceFailedBadge } from './PriceFailedBadge';

import { useRefreshPositionPriceMutation } from '@/apis/portfolioApis';
import { PositionDto } from '@/apis/portfolioApis/types';
import { InfoPopover } from '@/components/hints';
import { fmtNum as fmt, fmtQty, fmtSigned, fmtSignedPct, pnlColor } from '@/utils/format';

interface GroupTableProps {
  positions: PositionDto[];
  liquidGrossAssetsTWD: number;
  onStockClick?: (p: PositionDto) => void;
}

/** One group's holdings: a table from `sm` up, cards on a phone. */
export function PositionsGroupTable({
  positions,
  liquidGrossAssetsTWD,
  onStockClick,
}: GroupTableProps) {
  const { value, change, showRatio } = usePrivacy();
  const [refreshPrice, { isLoading: isRefreshing, originalArgs }] =
    useRefreshPositionPriceMutation();
  const isMobile = useMediaQuery((theme) => theme.breakpoints.down('sm'));

  const handleRefresh = (symbol: string) => {
    refreshPrice({ symbol, feeDiscountRate: loadFeeDiscountRate() });
  };

  if (isMobile) {
    return (
      <PositionsCardList
        positions={positions}
        onStockClick={onStockClick}
        onRefresh={handleRefresh}
        isRefreshing={isRefreshing}
        refreshingSymbol={originalArgs?.symbol}
      />
    );
  }

  return (
    <TableContainer>
      <Table size="small">
        <TableHead>
          <TableRow>
            <TableCell>項目</TableCell>
            <TableCell align="right">數量</TableCell>
            <TableCell align="right" sx={{ display: { xs: 'none', sm: 'table-cell' } }}>
              現價/NAV
            </TableCell>
            <TableCell align="right">市值 (TWD)</TableCell>
            <TableCell align="right" sx={{ display: { xs: 'none', sm: 'table-cell' } }}>
              <Box
                component="span"
                sx={{ display: 'inline-flex', alignItems: 'center', gap: 0.25 }}
              >
                占比
                <InfoPopover
                  content={POSITION_SHARE_TOOLTIP}
                  ariaLabel="占比計算方式"
                  iconSize={14}
                />
              </Box>
            </TableCell>
            <TableCell align="right">未實現損益</TableCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {positions.map((p, i) => {
            const clickable = opensDetail(p, !!onStockClick);
            const share = liquidSharePct(p.marketValueTWD, p.assetType, liquidGrossAssetsTWD);
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
                      {(p.symbol || p.valuationMethod) && (
                        <Typography variant="caption" color="text.secondary">
                          {p.symbol ? p.name : p.valuationMethod}
                        </Typography>
                      )}
                    </Box>
                    <PositionChips position={p} />
                    {p.priceFetchFailed && (
                      <>
                        <Chip
                          label="報價失敗"
                          size="small"
                          color="error"
                          variant="outlined"
                          sx={{ fontSize: 10, height: 18 }}
                        />
                        <PriceFailedBadge
                          symbol={p.symbol}
                          onRefresh={handleRefresh}
                          refreshing={isRefreshing && originalArgs?.symbol === p.symbol}
                        />
                      </>
                    )}
                    {/* AI theme tags — subtler than the status chips above (no border/color) so a
                        fully-tagged table doesn't read as a wall of outlined badges. */}
                    {p.tags?.map((t) => (
                      <Chip
                        key={t}
                        label={t}
                        size="small"
                        sx={{ fontSize: 10, height: 18, bgcolor: 'action.selected' }}
                      />
                    ))}
                  </Box>
                </TableCell>
                <TableCell align="right">
                  {isFlatValued(p)
                    ? '—'
                    : // Quantity is masked too, not just price: current price is public
                      // knowledge for any listed security, so leaking quantity alone still lets
                      // a screenshot viewer compute market value themselves.
                      value(fmtQty(p.quantity))}
                </TableCell>
                <TableCell align="right" sx={{ display: { xs: 'none', sm: 'table-cell' } }}>
                  {isFlatValued(p) ? '—' : `${p.currency} ${value(fmt(p.currentPrice, 2))}`}
                </TableCell>
                <TableCell align="right">
                  <Typography
                    variant="body2"
                    sx={{
                      color: p.assetType === 'LIABILITY' ? pnlColor(p.marketValueTWD) : undefined,
                    }}
                  >
                    {value(fmt(p.marketValueTWD))}
                  </Typography>
                  {p.currency === 'USD' && (
                    <Typography variant="caption" color="text.secondary">
                      USD {value(fmt(p.marketValueLocal, 0))}
                    </Typography>
                  )}
                </TableCell>
                <TableCell align="right" sx={{ display: { xs: 'none', sm: 'table-cell' } }}>
                  {showRatio && share != null ? `${share.toFixed(1)}%` : '—'}
                </TableCell>
                <TableCell align="right">
                  {p.unrealizedPnlTWD != null && p.unrealizedPnlPct != null ? (
                    <>
                      <Typography variant="body2" sx={{ color: pnlColor(p.unrealizedPnlTWD) }}>
                        {change(fmtSigned(p.unrealizedPnlTWD))}
                      </Typography>
                      {showRatio && (
                        <Typography variant="caption" sx={{ color: pnlColor(p.unrealizedPnlPct) }}>
                          {fmtSignedPct(p.unrealizedPnlPct)}
                        </Typography>
                      )}
                    </>
                  ) : (
                    <Typography variant="caption" color="text.secondary">
                      —
                    </Typography>
                  )}
                  {p.dividendsTWD > 0 && (
                    <Typography variant="caption" color="text.secondary" sx={{ display: 'block' }}>
                      +息 {value(fmt(p.dividendsTWD))}
                    </Typography>
                  )}
                  {p.estimatedSellCostTWD > 0 && (
                    <Typography variant="caption" color="text.secondary" sx={{ display: 'block' }}>
                      -估費 {value(fmt(p.estimatedSellCostTWD))}
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

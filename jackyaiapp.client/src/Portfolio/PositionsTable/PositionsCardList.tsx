import Box from '@mui/material/Box';
import Chip from '@mui/material/Chip';
import Typography from '@mui/material/Typography';

import { usePrivacy } from '../privacy';

import { PositionChips } from './PositionChips';
import { isFlatValued, opensDetail } from './positionDisplay';
import { PriceFailedBadge } from './PriceFailedBadge';

import { PositionDto } from '@/apis/portfolioApis/types';
import { fmtNum as fmt, fmtQty, fmtSigned, fmtSignedPct, pnlColor } from '@/utils/format';

interface CardListProps {
  positions: PositionDto[];
  onStockClick?: (p: PositionDto) => void;
  onRefresh: (symbol: string) => void;
  isRefreshing: boolean;
  refreshingSymbol?: string;
}

// Mobile (xs) replacement for PositionsGroupTable's <Table> — a table is fundamentally a
// compression, not a reorganization: squeezing 4+ columns into 375px just makes the "項目" cell
// wrap/overflow and the "未實現損益" cell stack 4 lines tall. Two-line cards (identity left,
// numbers right) is the standard brokerage-app pattern for this instead. sm+ keeps the table
// untouched.
export function PositionsCardList({
  positions,
  onStockClick,
  onRefresh,
  isRefreshing,
  refreshingSymbol,
}: CardListProps) {
  const { value, change, showRatio } = usePrivacy();

  return (
    <Box>
      {positions.map((p, i) => {
        const clickable = opensDetail(p, !!onStockClick);

        return (
          <Box
            key={`${p.assetType}-${p.symbol}-${p.name}-${i}`}
            onClick={() => clickable && onStockClick?.(p)}
            sx={{
              display: 'flex',
              justifyContent: 'space-between',
              gap: 1,
              py: 1,
              px: 1.5,
              borderBottom: '1px solid',
              borderColor: 'divider',
              cursor: clickable ? 'pointer' : 'default',
            }}
          >
            <Box sx={{ minWidth: 0, flex: 1 }}>
              <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.5, flexWrap: 'wrap' }}>
                <Typography variant="body2" sx={{ fontWeight: 700 }} noWrap>
                  {p.symbol || p.name}
                </Typography>
                <PositionChips position={p} />
              </Box>
              {!isFlatValued(p) && (
                <Typography
                  variant="caption"
                  color="text.secondary"
                  noWrap
                  sx={{ display: 'block' }}
                >
                  {p.symbol ? `${p.name} · ` : ''}
                  {value(fmtQty(p.quantity))}
                </Typography>
              )}
              {p.assetType === 'REAL_ESTATE' && p.valuationMethod && (
                <Typography
                  variant="caption"
                  color="text.secondary"
                  noWrap
                  sx={{ display: 'block' }}
                >
                  {p.valuationMethod}
                </Typography>
              )}
              {(p.tags?.length ?? 0) > 0 && (
                <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 0.5, mt: 0.25 }}>
                  {p.tags.map((t) => (
                    <Chip
                      key={t}
                      label={t}
                      size="small"
                      sx={{ fontSize: 10, height: 16, bgcolor: 'action.selected' }}
                    />
                  ))}
                </Box>
              )}
            </Box>
            <Box sx={{ textAlign: 'right', flexShrink: 0 }}>
              {p.priceFetchFailed ? (
                <Box
                  sx={{
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'flex-end',
                    gap: 0.25,
                  }}
                >
                  <Typography variant="body2" color="error">
                    報價失敗
                  </Typography>
                  <PriceFailedBadge
                    symbol={p.symbol}
                    onRefresh={onRefresh}
                    refreshing={isRefreshing && refreshingSymbol === p.symbol}
                  />
                </Box>
              ) : (
                <>
                  <Typography
                    variant="body2"
                    sx={{
                      color: p.assetType === 'LIABILITY' ? pnlColor(p.marketValueTWD) : undefined,
                    }}
                  >
                    {value(fmt(p.marketValueTWD))}
                  </Typography>
                  {p.currency === 'USD' && (
                    <Typography variant="caption" color="text.secondary" sx={{ display: 'block' }}>
                      USD {value(fmt(p.marketValueLocal, 0))}
                    </Typography>
                  )}
                </>
              )}
              {p.unrealizedPnlTWD != null && p.unrealizedPnlPct != null && (
                <Typography
                  variant="caption"
                  sx={{ color: pnlColor(p.unrealizedPnlTWD), display: 'block' }}
                >
                  {change(fmtSigned(p.unrealizedPnlTWD))}
                  {showRatio && ` (${fmtSignedPct(p.unrealizedPnlPct)})`}
                </Typography>
              )}
            </Box>
          </Box>
        );
      })}
    </Box>
  );
}

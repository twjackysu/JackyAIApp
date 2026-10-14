import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import RefreshIcon from '@mui/icons-material/Refresh';
import Accordion from '@mui/material/Accordion';
import AccordionDetails from '@mui/material/AccordionDetails';
import AccordionSummary from '@mui/material/AccordionSummary';
import Box from '@mui/material/Box';
import Chip from '@mui/material/Chip';
import CircularProgress from '@mui/material/CircularProgress';
import IconButton from '@mui/material/IconButton';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableContainer from '@mui/material/TableContainer';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import Typography from '@mui/material/Typography';
import useMediaQuery from '@mui/material/useMediaQuery';
import { useState } from 'react';

import { loadFeeDiscountRate } from './feeDiscountRate';
import { liquidSharePct, POSITION_SHARE_TOOLTIP } from './positionShare';
import { usePrivacy } from './privacy';

import { useRefreshPositionPriceMutation } from '@/apis/portfolioApis';
import { PositionDto } from '@/apis/portfolioApis/types';
import { ActionTooltip, InfoPopover } from '@/components/hints';
import { fmtNum as fmt, fmtQty, fmtSigned, fmtSignedPct, pnlColor } from '@/utils/format';

interface Props {
  positions: PositionDto[];
  /** Denominator of the 占比 column: `PortfolioSummaryDto.liquidGrossAssetsTWD`. */
  liquidGrossAssetsTWD: number;
  onStockClick?: (p: PositionDto) => void;
}

const TYPE_CHIP: Record<
  string,
  { label: string; color: 'primary' | 'warning' | 'success' | 'info' | 'error' }
> = {
  STOCK: { label: '股票', color: 'primary' },
  CASH: { label: '現金', color: 'success' },
  FUND: { label: '基金', color: 'info' },
  LIABILITY: { label: '負債', color: 'error' },
  REAL_ESTATE: { label: '不動產', color: 'warning' },
};

// Positions with no live price/quantity concept — manually valued, "quantity" IS the value.
function isFlatValued(p: PositionDto): boolean {
  return p.assetType === 'CASH' || p.assetType === 'LIABILITY' || p.assetType === 'REAL_ESTATE';
}

type GroupKey = 'TW_STOCK' | 'US_STOCK' | 'FUND' | 'CASH' | 'REAL_ESTATE' | 'LIABILITY' | 'OTHER';

const GROUP_LABEL: Record<GroupKey, string> = {
  TW_STOCK: '台股',
  US_STOCK: '美股',
  FUND: '基金',
  CASH: '現金',
  REAL_ESTATE: '不動產',
  LIABILITY: '負債',
  OTHER: '其他',
};

// Liability is listed last — it's debt, not a holding.
const GROUP_ORDER: GroupKey[] = [
  'TW_STOCK',
  'US_STOCK',
  'FUND',
  'CASH',
  'REAL_ESTATE',
  'LIABILITY',
  'OTHER',
];

function groupKeyOf(p: PositionDto): GroupKey {
  if (p.assetType === 'STOCK') return p.market === 'US' ? 'US_STOCK' : 'TW_STOCK';
  if (p.assetType === 'FUND') return 'FUND';
  if (p.assetType === 'CASH') return 'CASH';
  if (p.assetType === 'REAL_ESTATE') return 'REAL_ESTATE';
  if (p.assetType === 'LIABILITY') return 'LIABILITY';
  return 'OTHER';
}

// Shared between the desktop table and the mobile card list so the reload mutation trigger /
// spinner / stopPropagation logic isn't duplicated in two places.
interface PriceFailedBadgeProps {
  symbol: string;
  onRefresh: (symbol: string) => void;
  refreshing: boolean;
}

function PriceFailedBadge({ symbol, onRefresh, refreshing }: PriceFailedBadgeProps) {
  return (
    <ActionTooltip title="重新抓取報價">
      <span>
        <IconButton
          size="small"
          disabled={refreshing}
          onClick={(e) => {
            e.stopPropagation();
            onRefresh(symbol);
          }}
          sx={{ p: 0.25 }}
        >
          {refreshing ? <CircularProgress size={14} /> : <RefreshIcon sx={{ fontSize: 14 }} />}
        </IconButton>
      </span>
    </ActionTooltip>
  );
}

export function PositionsTable({ positions, liquidGrossAssetsTWD, onStockClick }: Props) {
  const { value, showRatio } = usePrivacy();
  const [expanded, setExpanded] = useState<Record<string, boolean>>({});

  if (positions.length === 0) {
    return (
      <Typography color="text.secondary" sx={{ py: 4, textAlign: 'center' }}>
        尚無持倉，點「建立持倉」開始
      </Typography>
    );
  }

  const groups = GROUP_ORDER.map((key) => ({
    key,
    positions: positions.filter((p) => groupKeyOf(p) === key),
  })).filter((g) => g.positions.length > 0);

  return (
    <Box>
      {groups.map((g) => {
        const groupTotal = g.positions.reduce((s, p) => s + p.marketValueTWD, 0);
        // Group keys match asset types where it matters: only REAL_ESTATE has no share.
        const groupShare = liquidSharePct(groupTotal, g.key, liquidGrossAssetsTWD);
        const isExpanded = expanded[g.key] ?? true;
        return (
          <Accordion
            key={g.key}
            expanded={isExpanded}
            onChange={(_, next) => setExpanded((prev) => ({ ...prev, [g.key]: next }))}
            disableGutters
          >
            <AccordionSummary expandIcon={<ExpandMoreIcon />}>
              <Box
                sx={{
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'space-between',
                  width: '100%',
                  pr: 1,
                }}
              >
                <Typography variant="subtitle2" sx={{ fontWeight: 700 }}>
                  {GROUP_LABEL[g.key]}（{g.positions.length}）
                </Typography>
                <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5 }}>
                  {showRatio && groupShare != null && (
                    <Typography variant="caption" color="text.secondary">
                      {groupShare.toFixed(1)}%
                    </Typography>
                  )}
                  <Typography
                    variant="body2"
                    sx={{ color: g.key === 'LIABILITY' ? pnlColor(groupTotal) : undefined }}
                  >
                    {value(fmt(groupTotal))}
                  </Typography>
                </Box>
              </Box>
            </AccordionSummary>
            <AccordionDetails sx={{ p: 0 }}>
              <PositionsGroupTable
                positions={g.positions}
                liquidGrossAssetsTWD={liquidGrossAssetsTWD}
                onStockClick={onStockClick}
              />
            </AccordionDetails>
          </Accordion>
        );
      })}
    </Box>
  );
}

interface GroupTableProps {
  positions: PositionDto[];
  liquidGrossAssetsTWD: number;
  onStockClick?: (p: PositionDto) => void;
}

function PositionsGroupTable({ positions, liquidGrossAssetsTWD, onStockClick }: GroupTableProps) {
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
                      {(p.symbol || p.valuationMethod) && (
                        <Typography variant="caption" color="text.secondary">
                          {p.symbol ? p.name : p.valuationMethod}
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
                    {!isFlatValued(p) && p.exposureFactor !== 1 && (
                      <Chip
                        label={p.exposureFactor === 0 ? '0曝險' : `曝險×${p.exposureFactor}`}
                        size="small"
                        color={p.exposureFactor === 0 ? 'default' : 'warning'}
                        variant="outlined"
                        sx={{ fontSize: 10, height: 18 }}
                      />
                    )}
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
                  {(() => {
                    const share = liquidSharePct(
                      p.marketValueTWD,
                      p.assetType,
                      liquidGrossAssetsTWD,
                    );
                    return showRatio && share != null ? `${share.toFixed(1)}%` : '—';
                  })()}
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
// above untouched.
function PositionsCardList({
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
        const isStock = p.assetType === 'STOCK';
        const clickable = (isStock || (p.assetType === 'FUND' && !!p.symbol)) && !!onStockClick;
        const chip = TYPE_CHIP[p.assetType] ?? TYPE_CHIP.STOCK;

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
                <Chip
                  label={isStock && p.market ? p.market : chip.label}
                  size="small"
                  color={chip.color}
                  variant="outlined"
                  sx={{ fontSize: 10, height: 18 }}
                />
                {!isFlatValued(p) && p.exposureFactor !== 1 && (
                  <Chip
                    label={p.exposureFactor === 0 ? '0曝險' : `曝險×${p.exposureFactor}`}
                    size="small"
                    color={p.exposureFactor === 0 ? 'default' : 'warning'}
                    variant="outlined"
                    sx={{ fontSize: 10, height: 18 }}
                  />
                )}
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

import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import Accordion from '@mui/material/Accordion';
import AccordionDetails from '@mui/material/AccordionDetails';
import AccordionSummary from '@mui/material/AccordionSummary';
import Box from '@mui/material/Box';
import Chip from '@mui/material/Chip';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableContainer from '@mui/material/TableContainer';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import Typography from '@mui/material/Typography';
import { useState } from 'react';

import { fmtNum as fmt, fmtQty, pnlColor } from './portfolioUtils';
import { usePrivacy } from './privacy';

import { PositionDto } from '@/apis/portfolioApis/types';

interface Props {
  positions: PositionDto[];
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
};

type GroupKey = 'TW_STOCK' | 'US_STOCK' | 'FUND' | 'CASH' | 'LIABILITY' | 'OTHER';

const GROUP_LABEL: Record<GroupKey, string> = {
  TW_STOCK: '台股',
  US_STOCK: '美股',
  FUND: '基金',
  CASH: '現金',
  LIABILITY: '負債',
  OTHER: '其他',
};

// Liability is listed last — it's debt, not a holding.
const GROUP_ORDER: GroupKey[] = ['TW_STOCK', 'US_STOCK', 'FUND', 'CASH', 'LIABILITY', 'OTHER'];

function groupKeyOf(p: PositionDto): GroupKey {
  if (p.assetType === 'STOCK') return p.market === 'US' ? 'US_STOCK' : 'TW_STOCK';
  if (p.assetType === 'FUND') return 'FUND';
  if (p.assetType === 'CASH') return 'CASH';
  if (p.assetType === 'LIABILITY') return 'LIABILITY';
  return 'OTHER';
}

export function PositionsTable({ positions, onStockClick }: Props) {
  const { value, showRatio } = usePrivacy();
  // Weight % denominator is gross assets, not net worth — liability (the only negative
  // value) must not shrink the denominator and inflate everyone else's percentage.
  const total = positions.reduce((s, p) => s + Math.max(p.marketValueTWD, 0), 0);
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
                  {showRatio && total > 0 && (
                    <Typography variant="caption" color="text.secondary">
                      {/* groupTotal is already negative for LIABILITY (sign-flip invariant,
                          see PortfolioService), so this naturally renders e.g. "-3.2%". */}
                      {((groupTotal / total) * 100).toFixed(1)}%
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
                total={total}
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
  total: number;
  onStockClick?: (p: PositionDto) => void;
}

function PositionsGroupTable({ positions, total, onStockClick }: GroupTableProps) {
  const { value, change, showRatio } = usePrivacy();

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
              占比
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
                    {p.assetType !== 'CASH' &&
                      p.assetType !== 'LIABILITY' &&
                      p.exposureFactor !== 1 && (
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
                  {p.assetType === 'CASH' || p.assetType === 'LIABILITY' ? '—' : fmtQty(p.quantity)}
                </TableCell>
                <TableCell align="right" sx={{ display: { xs: 'none', sm: 'table-cell' } }}>
                  {p.assetType === 'CASH' || p.assetType === 'LIABILITY'
                    ? '—'
                    : `${p.currency} ${value(fmt(p.currentPrice, 2))}`}
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
                  {p.dividendsTWD > 0 && (
                    <Typography variant="caption" color="text.secondary" sx={{ display: 'block' }}>
                      +息 {value(fmt(p.dividendsTWD))}
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

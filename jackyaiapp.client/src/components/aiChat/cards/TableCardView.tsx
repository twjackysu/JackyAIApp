import {
  Box,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  TableSortLabel,
  Typography,
} from '@mui/material';
import { useMemo, useState } from 'react';

import TableCardActions from './TableCardActions';
import { nextSort, sortTableRows, type TableSort } from './tableSort';

import type { CardCell, TableCard } from './toolCard';

// A number never reads right broken over lines, and its column takes only what it needs, leaving the
// rest to the text (a stock's name) rather than splitting it.
const numericCell = { whiteSpace: 'nowrap', width: '1%' } as const;

const show = (v: CardCell) =>
  v === null ? '—' : typeof v === 'number' ? v.toLocaleString('zh-TW') : v;

/**
 * A tool's rows as a table in the answer; long ones scroll inside it rather than the chat. A header sorts by its column
 * (D-152), and 複製表格 / 下載 CSV under it take the rows in the order on screen (D-150).
 */
export default function TableCardView({ card }: { card: TableCard }) {
  const [sort, setSort] = useState<TableSort | null>(null);
  const rows = useMemo(() => sortTableRows(card.rows, sort), [card.rows, sort]);
  const shown = useMemo(() => ({ ...card, rows: [...rows] }), [card, rows]);
  return (
    <Box>
      {card.title && (
        <Typography variant="subtitle2" sx={{ mb: 0.75 }}>
          {card.title}
        </Typography>
      )}
      <TableContainer sx={{ maxHeight: 320 }}>
        {/* Narrow padding: the card often sits in a side sheet or on a phone. */}
        <Table size="small" stickyHeader sx={{ '& .MuiTableCell-root': { px: 1 } }}>
          <TableHead>
            <TableRow>
              {card.columns.map((c) => (
                <TableCell
                  key={c.key}
                  align={c.numeric ? 'right' : 'left'}
                  sx={c.numeric ? numericCell : undefined}
                  sortDirection={sort?.key === c.key ? sort.direction : false}
                >
                  <TableSortLabel
                    active={sort?.key === c.key}
                    direction={sort?.key === c.key ? sort.direction : c.numeric ? 'desc' : 'asc'}
                    onClick={() => setSort((current) => nextSort(current, c))}
                    // Only the sorted column's arrow shows, in the accent colour with its label. A number column's sits
                    // in the gap left of its label and takes no width: the column stays as narrow as its numbers, and on a
                    // phone a wider one pushes the table past the answer.
                    sx={{
                      '&.Mui-active, &.Mui-active .MuiTableSortLabel-icon': {
                        color: 'primary.main',
                      },
                      ...(c.numeric && { position: 'relative' }),
                      '& .MuiTableSortLabel-icon': {
                        fontSize: 16,
                        ml: 0.25,
                        mr: 0,
                        ...(c.numeric && { position: 'absolute', right: '100%', mx: 0 }),
                        ...(sort?.key !== c.key && { display: 'none' }),
                      },
                    }}
                  >
                    {c.label}
                  </TableSortLabel>
                </TableCell>
              ))}
            </TableRow>
          </TableHead>
          <TableBody>
            {rows.map((row, i) => (
              <TableRow key={i}>
                {card.columns.map((c) => (
                  <TableCell
                    key={c.key}
                    align={c.numeric ? 'right' : 'left'}
                    sx={{
                      ...(c.numeric && { ...numericCell, fontVariantNumeric: 'tabular-nums' }),
                      ...(c.nowrap && { whiteSpace: 'nowrap' }),
                    }}
                  >
                    {show(row[c.key])}
                  </TableCell>
                ))}
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableContainer>
      {card.totalRows !== null && (
        <Typography variant="caption" color="text.secondary">
          顯示前 {card.rows.length} 筆，共 {card.totalRows.toLocaleString('zh-TW')} 筆
        </Typography>
      )}
      {card.rows.length > 0 && <TableCardActions table={shown} />}
    </Box>
  );
}

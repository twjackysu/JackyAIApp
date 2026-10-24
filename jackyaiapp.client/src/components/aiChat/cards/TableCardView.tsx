import {
  Box,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Typography,
} from '@mui/material';

import type { CardCell, TableCard } from './toolCard';

// A number never reads right broken over lines, and its column takes only what it needs, leaving the
// rest to the text (a stock's name) rather than splitting it.
const numericCell = { whiteSpace: 'nowrap', width: '1%' } as const;

const show = (v: CardCell) =>
  v === null ? '—' : typeof v === 'number' ? v.toLocaleString('zh-TW') : v;

/** A tool's rows as a table in the answer; long ones scroll inside it rather than the chat. */
export default function TableCardView({ card }: { card: TableCard }) {
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
                >
                  {c.label}
                </TableCell>
              ))}
            </TableRow>
          </TableHead>
          <TableBody>
            {card.rows.map((row, i) => (
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
    </Box>
  );
}

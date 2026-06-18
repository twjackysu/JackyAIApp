import Paper from '@mui/material/Paper';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableContainer from '@mui/material/TableContainer';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import TableSortLabel from '@mui/material/TableSortLabel';
import { useMemo, useState } from 'react';

import { formatCellValue } from '../../utils/formatters';

interface TableVizProps {
  rows: Record<string, unknown>[];
  headers: string[];
}

export function TableViz({ rows, headers }: TableVizProps) {
  const [sortCol, setSortCol] = useState<string | null>(null);
  const [sortDir, setSortDir] = useState<'asc' | 'desc'>('asc');

  // Reset sorting whenever a new result arrives — adjust state during render
  // (keyed on the header set) instead of a sync useEffect.
  const headersKey = headers.join('|');
  const [prevHeadersKey, setPrevHeadersKey] = useState(headersKey);
  if (prevHeadersKey !== headersKey) {
    setPrevHeadersKey(headersKey);
    setSortCol(null);
    setSortDir('asc');
  }

  const handleSortClick = (col: string) => {
    if (sortCol === col) {
      setSortDir((d) => (d === 'asc' ? 'desc' : 'asc'));
    } else {
      setSortCol(col);
      setSortDir('asc');
    }
  };

  const sortedRows = useMemo(() => {
    if (!sortCol) return rows;
    return [...rows].sort((a, b) => {
      const av = a[sortCol];
      const bv = b[sortCol];
      if (typeof av === 'number' && typeof bv === 'number') {
        return sortDir === 'asc' ? av - bv : bv - av;
      }
      const cmp = String(av ?? '').localeCompare(String(bv ?? ''));
      return sortDir === 'asc' ? cmp : -cmp;
    });
  }, [rows, sortCol, sortDir]);

  return (
    <TableContainer component={Paper} variant="outlined" sx={{ height: '100%' }}>
      <Table size="small" stickyHeader>
        <TableHead>
          <TableRow>
            {headers.map((h) => (
              <TableCell
                key={h}
                sortDirection={sortCol === h ? sortDir : false}
                sx={{ whiteSpace: 'nowrap', userSelect: 'none' }}
              >
                <TableSortLabel
                  active={sortCol === h}
                  direction={sortCol === h ? sortDir : 'asc'}
                  onClick={() => handleSortClick(h)}
                >
                  {h}
                </TableSortLabel>
              </TableCell>
            ))}
          </TableRow>
        </TableHead>
        <TableBody>
          {sortedRows.map((row, i) => (
            <TableRow key={i} hover>
              {headers.map((h) => (
                <TableCell key={h}>{formatCellValue(row[h])}</TableCell>
              ))}
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </TableContainer>
  );
}

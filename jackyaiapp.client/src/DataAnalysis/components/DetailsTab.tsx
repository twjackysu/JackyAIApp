import Alert from '@mui/material/Alert';
import CircularProgress from '@mui/material/CircularProgress';
import Paper from '@mui/material/Paper';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableContainer from '@mui/material/TableContainer';
import TableHead from '@mui/material/TableHead';
import TablePagination from '@mui/material/TablePagination';
import TableRow from '@mui/material/TableRow';
import Typography from '@mui/material/Typography';

import { ROWS_PER_PAGE } from '../constants';
import { formatCellValue } from '../utils/formatters';

import { FilterEditor } from './FilterEditor';

import type { FieldDefinition } from '../../apis/dataAnalysisApis/types';
import type { ActiveFilter } from '../types';

interface DetailsTabProps {
  fields: FieldDefinition[];
  detailsFilters: ActiveFilter[];
  onFiltersChange: (filters: ActiveFilter[]) => void;
  detailsLoading: boolean;
  detailsError: unknown;
  detailsRows: Record<string, unknown>[];
  detailsHeaders: string[];
  detailsTotalCount: number;
  page: number;
  onPageChange: (page: number) => void;
  sourceId?: string;
  tableName?: string;
}

export function DetailsTab({
  fields,
  detailsFilters,
  onFiltersChange,
  detailsLoading,
  detailsError,
  detailsRows,
  detailsHeaders,
  detailsTotalCount,
  page,
  onPageChange,
  sourceId,
  tableName,
}: DetailsTabProps) {
  return (
    <>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
        <strong>DetailsAsync</strong> — paginated row-level data. Filters apply immediately.
      </Typography>

      <Paper variant="outlined" sx={{ p: 2, mb: 2 }}>
        <FilterEditor
          filters={detailsFilters}
          fields={fields}
          onChange={onFiltersChange}
          sourceId={sourceId}
          tableName={tableName}
        />
      </Paper>

      {detailsLoading && <CircularProgress size={24} sx={{ mb: 1 }} />}
      {detailsError && (
        <Alert severity="error" sx={{ mb: 2 }}>
          Failed to load rows.
        </Alert>
      )}

      {detailsHeaders.length > 0 && (
        <Paper variant="outlined">
          <TableContainer sx={{ maxHeight: 500 }}>
            <Table size="small" stickyHeader>
              <TableHead>
                <TableRow>
                  {detailsHeaders.map((h) => (
                    <TableCell key={h} sx={{ whiteSpace: 'nowrap' }}>
                      {h}
                    </TableCell>
                  ))}
                </TableRow>
              </TableHead>
              <TableBody>
                {detailsRows.map((row, i) => (
                  <TableRow key={i} hover>
                    {detailsHeaders.map((h) => (
                      <TableCell
                        key={h}
                        sx={{
                          maxWidth: 160,
                          overflow: 'hidden',
                          textOverflow: 'ellipsis',
                          whiteSpace: 'nowrap',
                        }}
                        title={String((row as Record<string, unknown>)[h] ?? '')}
                      >
                        {formatCellValue((row as Record<string, unknown>)[h])}
                      </TableCell>
                    ))}
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </TableContainer>

          <TablePagination
            component="div"
            count={detailsTotalCount}
            page={page}
            rowsPerPage={ROWS_PER_PAGE}
            rowsPerPageOptions={[ROWS_PER_PAGE]}
            onPageChange={(_, newPage) => onPageChange(newPage)}
            labelDisplayedRows={({ from, to, count }) =>
              `${from}–${to} of ${count > 0 ? count : `more than ${to}`}`
            }
          />
        </Paper>
      )}
    </>
  );
}

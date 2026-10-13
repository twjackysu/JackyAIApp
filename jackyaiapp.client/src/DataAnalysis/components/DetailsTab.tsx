import FileDownloadOutlinedIcon from '@mui/icons-material/FileDownloadOutlined';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
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

import type { DetailsExportResult } from '../../apis/dataAnalysisApis/exportCsv';
import type { FieldDefinition } from '../../apis/dataAnalysisApis/types';
import type { ActiveFilter } from '../types';

import { fmtNum } from '@/utils/format';

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
  /** Downloads every row matching the filters (not just this page) as CSV. */
  onExport: () => void;
  exporting: boolean;
  exportResult: DetailsExportResult | null;
  exportError: string | null;
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
  onExport,
  exporting,
  exportResult,
  exportError,
}: DetailsTabProps) {
  const truncated = exportResult != null && exportResult.total > exportResult.exported;

  return (
    <>
      <Box
        sx={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          gap: 1,
          flexWrap: 'wrap',
          mb: 2,
        }}
      >
        <Typography variant="body2" color="text.secondary">
          瀏覽數字背後的原始資料列。篩選條件會立即套用。
        </Typography>
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
          {exportResult && !truncated && (
            <Typography variant="caption" color="text.secondary">
              已匯出 {fmtNum(exportResult.exported)} 筆
            </Typography>
          )}
          <Button
            size="small"
            variant="outlined"
            startIcon={exporting ? <CircularProgress size={16} /> : <FileDownloadOutlinedIcon />}
            onClick={onExport}
            disabled={exporting}
          >
            {exporting ? '匯出中…' : '匯出 CSV'}
          </Button>
        </Box>
      </Box>

      {exportError && (
        <Alert severity="error" sx={{ mb: 2 }}>
          匯出失敗：{exportError}
        </Alert>
      )}
      {truncated && exportResult && (
        <Alert severity="warning" sx={{ mb: 2 }}>
          已匯出前 {fmtNum(exportResult.exported)} 筆，符合條件的共 {fmtNum(exportResult.total)}{' '}
          筆，超過單次匯出上限。請加上篩選條件縮小範圍，分批匯出。
        </Alert>
      )}

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
          載入資料列失敗。
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
              `${fmtNum(from)}–${fmtNum(to)}，共 ${count >= 0 ? fmtNum(count) : `超過 ${fmtNum(to)}`} 筆`
            }
          />
        </Paper>
      )}
    </>
  );
}

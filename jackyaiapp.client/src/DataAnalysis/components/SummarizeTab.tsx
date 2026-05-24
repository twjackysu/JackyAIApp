import PlayArrowIcon from '@mui/icons-material/PlayArrow';
import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import Divider from '@mui/material/Divider';
import Paper from '@mui/material/Paper';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableContainer from '@mui/material/TableContainer';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import Typography from '@mui/material/Typography';

import type { FieldDefinition, MeasureFieldDto } from '../../apis/dataAnalysisApis/types';
import type { ActiveFilter, ViewByEntry } from '../types';
import { formatCellValue } from '../utils/formatters';
import { FilterEditor } from './FilterEditor';
import { MeasureEditor } from './MeasureEditor';
import { ViewByEditor } from './ViewByEditor';

interface SummarizeTabProps {
  // ViewBy
  viewByEntries: ViewByEntry[];
  onViewByAdd: () => void;
  onViewByUpdate: (entry: ViewByEntry) => void;
  onViewByRemove: (id: number) => void;
  // Measures
  measures: MeasureFieldDto[];
  onMeasureAdd: () => void;
  onMeasureRemove: (i: number) => void;
  onMeasureUpdate: (i: number, patch: Partial<MeasureFieldDto>) => void;
  // Filters
  summarizeFilters: ActiveFilter[];
  onSummarizeFiltersChange: (filters: ActiveFilter[]) => void;
  // Run
  onRunSummarize: () => void;
  isLoading: boolean;
  error: unknown;
  // Results
  summarizeRows: Record<string, unknown>[];
  summarizeHeaders: string[];
  // Context
  fields: FieldDefinition[];
  sourceId?: string;
  tableName?: string;
}

export function SummarizeTab({
  viewByEntries, onViewByAdd, onViewByUpdate, onViewByRemove,
  measures, onMeasureAdd, onMeasureRemove, onMeasureUpdate,
  summarizeFilters, onSummarizeFiltersChange,
  onRunSummarize, isLoading, error,
  summarizeRows, summarizeHeaders,
  fields, sourceId, tableName,
}: SummarizeTabProps) {
  return (
    <>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
        <strong>SummarizeAsync</strong> — GroupBy + aggregated Measures, with optional Filters.
      </Typography>

      <Paper variant="outlined" sx={{ p: 2, mb: 3 }}>
        <ViewByEditor
          entries={viewByEntries}
          fields={fields}
          onAdd={onViewByAdd}
          onUpdate={onViewByUpdate}
          onRemove={onViewByRemove}
          sourceId={sourceId}
          tableName={tableName}
        />

        <Divider sx={{ my: 2 }} />

        <MeasureEditor
          measures={measures}
          fields={fields}
          onAdd={onMeasureAdd}
          onRemove={onMeasureRemove}
          onUpdate={onMeasureUpdate}
        />

        <Divider sx={{ my: 2 }} />

        <FilterEditor
          filters={summarizeFilters}
          fields={fields}
          onChange={onSummarizeFiltersChange}
          sourceId={sourceId}
          tableName={tableName}
        />

        <Button
          variant="contained"
          startIcon={<PlayArrowIcon />}
          onClick={onRunSummarize}
          disabled={isLoading || viewByEntries.length === 0 || measures.length === 0}
          sx={{ mt: 2 }}
        >
          {isLoading ? 'Running…' : 'Run Query'}
        </Button>
      </Paper>

      {error && (
        <Alert severity="error" sx={{ mb: 2 }}>Query failed. Check console for details.</Alert>
      )}

      {summarizeHeaders.length > 0 && (
        <>
          <Typography variant="caption" color="text.secondary" sx={{ mb: 1, display: 'block' }}>
            {summarizeRows.length} rows returned
          </Typography>
          <TableContainer component={Paper} variant="outlined">
            <Table size="small">
              <TableHead>
                <TableRow>
                  {summarizeHeaders.map((h) => <TableCell key={h}>{h}</TableCell>)}
                </TableRow>
              </TableHead>
              <TableBody>
                {summarizeRows.map((row, i) => (
                  <TableRow key={i} hover>
                    {summarizeHeaders.map((h) => (
                      <TableCell key={h}>{formatCellValue(row[h])}</TableCell>
                    ))}
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </TableContainer>
        </>
      )}
    </>
  );
}

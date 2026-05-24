import BarChartIcon from '@mui/icons-material/BarChart';
import PieChartIcon from '@mui/icons-material/PieChart';
import PlayArrowIcon from '@mui/icons-material/PlayArrow';
import ShowChartIcon from '@mui/icons-material/ShowChart';
import StackedBarChartIcon from '@mui/icons-material/StackedBarChart';
import TableChartIcon from '@mui/icons-material/TableChart';
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
import TableSortLabel from '@mui/material/TableSortLabel';
import ToggleButton from '@mui/material/ToggleButton';
import ToggleButtonGroup from '@mui/material/ToggleButtonGroup';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';
import { useEffect, useMemo, useState } from 'react';

import type { FieldDefinition, MeasureFieldDto } from '../../apis/dataAnalysisApis/types';
import type { ActiveFilter, FieldTransformDef, ViewByEntry } from '../types';
import { formatCellValue } from '../utils/formatters';
import { FieldTransformEditor } from './FieldTransformEditor';
import { FilterEditor } from './FilterEditor';
import { MeasureEditor } from './MeasureEditor';
import { SummarizeChart, type ChartViewType } from './SummarizeChart';
import { ViewByEditor } from './ViewByEditor';

interface SummarizeTabProps {
  // Transforms
  fieldTransforms:  FieldTransformDef[];
  onTransformAdd:   (t: Omit<FieldTransformDef, 'id'>) => void;
  onTransformUpdate:(t: FieldTransformDef) => void;
  onTransformRemove:(id: number) => void;
  // ViewBy
  viewByEntries:    ViewByEntry[];
  onViewByAdd:      () => void;
  onViewByUpdate:   (entry: ViewByEntry) => void;
  onViewByRemove:   (id: number) => void;
  // Measures
  measures:         MeasureFieldDto[];
  onMeasureAdd:     () => void;
  onMeasureRemove:  (i: number) => void;
  onMeasureUpdate:  (i: number, patch: Partial<MeasureFieldDto>) => void;
  // Filters
  summarizeFilters:        ActiveFilter[];
  onSummarizeFiltersChange:(filters: ActiveFilter[]) => void;
  // Run
  onRunSummarize: () => void;
  isLoading:      boolean;
  error:          unknown;
  // Results
  summarizeRows:    Record<string, unknown>[];
  summarizeHeaders: string[];
  // Context
  fields:     FieldDefinition[];
  sourceId?:  string;
  tableName?: string;
}

// ── Chart-type toggle metadata ─────────────────────────────────────────────────

interface ChartOption {
  value: ChartViewType;
  label: string;
  icon:  React.ReactNode;
}

const CHART_OPTIONS: ChartOption[] = [
  { value: 'table',         label: 'Table',          icon: <TableChartIcon fontSize="small" /> },
  { value: 'column',        label: 'Column',         icon: <BarChartIcon fontSize="small" /> },
  { value: 'bar',           label: 'Bar (horiz.)',   icon: <BarChartIcon fontSize="small" sx={{ transform: 'rotate(90deg)' }} /> },
  { value: 'line',          label: 'Line',            icon: <ShowChartIcon fontSize="small" /> },
  { value: 'pie',           label: 'Pie',             icon: <PieChartIcon fontSize="small" /> },
  { value: 'stackedColumn', label: 'Stacked Column',  icon: <StackedBarChartIcon fontSize="small" /> },
  { value: 'stackedBar',    label: 'Stacked Bar',     icon: <StackedBarChartIcon fontSize="small" sx={{ transform: 'rotate(90deg)' }} /> },
];

// ── Component ─────────────────────────────────────────────────────────────────

export function SummarizeTab({
  fieldTransforms, onTransformAdd, onTransformUpdate, onTransformRemove,
  viewByEntries, onViewByAdd, onViewByUpdate, onViewByRemove,
  measures, onMeasureAdd, onMeasureRemove, onMeasureUpdate,
  summarizeFilters, onSummarizeFiltersChange,
  onRunSummarize, isLoading, error,
  summarizeRows, summarizeHeaders,
  fields, sourceId, tableName,
}: SummarizeTabProps) {
  const [chartType, setChartType] = useState<ChartViewType>('table');

  // ── Column sort ────────────────────────────────────────────────────────────
  const [sortCol, setSortCol]     = useState<string | null>(null);
  const [sortDir, setSortDir]     = useState<'asc' | 'desc'>('asc');

  // Reset sort whenever a new result set arrives
  useEffect(() => {
    setSortCol(null);
    setSortDir('asc');
  }, [summarizeHeaders]);

  const handleSortClick = (col: string) => {
    if (sortCol === col) {
      setSortDir((d) => (d === 'asc' ? 'desc' : 'asc'));
    } else {
      setSortCol(col);
      setSortDir('asc');
    }
  };

  const sortedRows = useMemo(() => {
    if (!sortCol) return summarizeRows;
    return [...summarizeRows].sort((a, b) => {
      const av = a[sortCol];
      const bv = b[sortCol];
      // nulls/undefined always last
      if (av == null && bv == null) return 0;
      if (av == null) return 1;
      if (bv == null) return -1;
      // numeric comparison when both values are numbers
      if (typeof av === 'number' && typeof bv === 'number') {
        return sortDir === 'asc' ? av - bv : bv - av;
      }
      // fall back to locale string comparison
      const cmp = String(av).localeCompare(String(bv), undefined, { numeric: true, sensitivity: 'base' });
      return sortDir === 'asc' ? cmp : -cmp;
    });
  }, [summarizeRows, sortCol, sortDir]);

  return (
    <>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
        <strong>SummarizeAsync</strong> — GroupBy + aggregated Measures, with optional Filters.
      </Typography>

      <Paper variant="outlined" sx={{ p: 2, mb: 3 }}>
        {/* ── Field Transforms ── */}
        <FieldTransformEditor
          transforms={fieldTransforms}
          onAdd={onTransformAdd}
          onUpdate={onTransformUpdate}
          onRemove={onTransformRemove}
          fields={fields}
          sourceId={sourceId}
          tableName={tableName}
        />

        <Divider sx={{ my: 2 }} />

        {/* ── Group By ── */}
        <ViewByEditor
          entries={viewByEntries}
          fields={fields}
          transforms={fieldTransforms}
          onAdd={onViewByAdd}
          onUpdate={onViewByUpdate}
          onRemove={onViewByRemove}
        />

        <Divider sx={{ my: 2 }} />

        {/* ── Measures ── */}
        <MeasureEditor
          measures={measures}
          fields={fields}
          onAdd={onMeasureAdd}
          onRemove={onMeasureRemove}
          onUpdate={onMeasureUpdate}
        />

        <Divider sx={{ my: 2 }} />

        {/* ── Filters ── */}
        <FilterEditor
          filters={summarizeFilters}
          fields={fields}
          transforms={fieldTransforms}
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
          {/* ── Results header ── */}
          <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 8 }}>
            <Typography variant="caption" color="text.secondary">
              {summarizeRows.length} rows returned
            </Typography>

            <ToggleButtonGroup
              size="small"
              exclusive
              value={chartType}
              onChange={(_e, v) => { if (v !== null) setChartType(v); }}
              aria-label="view type"
            >
              {CHART_OPTIONS.map(({ value, label, icon }) => (
                <Tooltip key={value} title={label} placement="top">
                  <ToggleButton value={value} aria-label={label}>
                    {icon}
                  </ToggleButton>
                </Tooltip>
              ))}
            </ToggleButtonGroup>
          </div>

          {/* ── Table view ── */}
          {chartType === 'table' && (
            <TableContainer component={Paper} variant="outlined">
              <Table size="small">
                <TableHead>
                  <TableRow>
                    {summarizeHeaders.map((h) => (
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
                      {summarizeHeaders.map((h) => (
                        <TableCell key={h}>{formatCellValue(row[h])}</TableCell>
                      ))}
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </TableContainer>
          )}

          {/* ── Chart view ── */}
          {chartType !== 'table' && (
            <SummarizeChart
              chartType={chartType}
              rows={summarizeRows}
              headers={summarizeHeaders}
            />
          )}
        </>
      )}
    </>
  );
}

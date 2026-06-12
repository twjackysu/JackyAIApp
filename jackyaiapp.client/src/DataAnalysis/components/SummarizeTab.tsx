import AutoAwesomeIcon from '@mui/icons-material/AutoAwesome';
import BarChartIcon from '@mui/icons-material/BarChart';
import LoginIcon from '@mui/icons-material/Login';
import PieChartIcon from '@mui/icons-material/PieChart';
import PlayArrowIcon from '@mui/icons-material/PlayArrow';
import SettingsIcon from '@mui/icons-material/Settings';
import ShowChartIcon from '@mui/icons-material/ShowChart';
import StackedBarChartIcon from '@mui/icons-material/StackedBarChart';
import TableChartIcon from '@mui/icons-material/TableChart';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import Divider from '@mui/material/Divider';
import Paper from '@mui/material/Paper';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableContainer from '@mui/material/TableContainer';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import TableSortLabel from '@mui/material/TableSortLabel';
import TextField from '@mui/material/TextField';
import ToggleButton from '@mui/material/ToggleButton';
import ToggleButtonGroup from '@mui/material/ToggleButtonGroup';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';
import { useEffect, useMemo, useState } from 'react';

import { formatCellValue } from '../utils/formatters';

import { FieldTransformEditor } from './FieldTransformEditor';
import { FilterEditor } from './FilterEditor';
import { MeasureEditor } from './MeasureEditor';
import { SummarizeChart, type ChartViewType } from './SummarizeChart';
import { ViewByEditor } from './ViewByEditor';

import type { FieldDefinition, MeasureFieldDto } from '../../apis/dataAnalysisApis/types';
import type { ActiveFilter, FieldTransformDef, ViewByEntry } from '../types';

type SummarizeMode = 'manual' | 'ai';

interface SummarizeTabProps {
  // Transforms
  fieldTransforms: FieldTransformDef[];
  onTransformAdd: (t: Omit<FieldTransformDef, 'id'>) => void;
  onTransformUpdate: (t: FieldTransformDef) => void;
  onTransformRemove: (id: number) => void;
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
  // Run (manual)
  onRunSummarize: () => void;
  isLoading: boolean;
  error: unknown;
  // Results (manual)
  summarizeRows: Record<string, unknown>[];
  summarizeHeaders: string[];
  // NL mode
  onRunNlSummarize: (query: string) => void;
  nlIsLoading: boolean;
  nlError: unknown;
  nlRows: Record<string, unknown>[];
  nlInterpretation?: string;
  nlNotes?: string[];
  isLoggedIn: boolean;
  // Context
  fields: FieldDefinition[];
  sourceId?: string;
  tableName?: string;
}

// ── Chart-type toggle metadata ─────────────────────────────────────────────────

interface ChartOption {
  value: ChartViewType;
  label: string;
  icon: React.ReactNode;
}

const CHART_OPTIONS: ChartOption[] = [
  { value: 'table', label: 'Table', icon: <TableChartIcon fontSize="small" /> },
  { value: 'column', label: 'Column', icon: <BarChartIcon fontSize="small" /> },
  {
    value: 'bar',
    label: 'Bar (horiz.)',
    icon: <BarChartIcon fontSize="small" sx={{ transform: 'rotate(90deg)' }} />,
  },
  { value: 'line', label: 'Line', icon: <ShowChartIcon fontSize="small" /> },
  { value: 'pie', label: 'Pie', icon: <PieChartIcon fontSize="small" /> },
  {
    value: 'stackedColumn',
    label: 'Stacked Column',
    icon: <StackedBarChartIcon fontSize="small" />,
  },
  {
    value: 'stackedBar',
    label: 'Stacked Bar',
    icon: <StackedBarChartIcon fontSize="small" sx={{ transform: 'rotate(90deg)' }} />,
  },
];

const NL_EXAMPLES = [
  'Total revenue by region',
  'Monthly order count trend',
  'Revenue breakdown by product category',
  'Average order value by customer segment',
];

// ── Component ─────────────────────────────────────────────────────────────────

export function SummarizeTab({
  fieldTransforms,
  onTransformAdd,
  onTransformUpdate,
  onTransformRemove,
  viewByEntries,
  onViewByAdd,
  onViewByUpdate,
  onViewByRemove,
  measures,
  onMeasureAdd,
  onMeasureRemove,
  onMeasureUpdate,
  summarizeFilters,
  onSummarizeFiltersChange,
  onRunSummarize,
  isLoading,
  error,
  summarizeRows,
  summarizeHeaders,
  onRunNlSummarize,
  nlIsLoading,
  nlError,
  nlRows,
  nlInterpretation,
  nlNotes,
  isLoggedIn,
  fields,
  sourceId,
  tableName,
}: SummarizeTabProps) {
  const [mode, setMode] = useState<SummarizeMode>('ai');
  const [nlQuery, setNlQuery] = useState('');
  const [chartType, setChartType] = useState<ChartViewType>('table');

  // Derive active result set based on current mode
  const activeRows = mode === 'ai' ? nlRows : summarizeRows;
  const activeHeaders = useMemo(
    () => (mode === 'ai' ? (nlRows.length > 0 ? Object.keys(nlRows[0]) : []) : summarizeHeaders),
    [mode, nlRows, summarizeHeaders],
  );

  // ── Column sort ────────────────────────────────────────────────────────────
  const [sortCol, setSortCol] = useState<string | null>(null);
  const [sortDir, setSortDir] = useState<'asc' | 'desc'>('asc');

  // Reset sort whenever a new result set arrives
  useEffect(() => {
    setSortCol(null);
    setSortDir('asc');
  }, [activeHeaders]);

  const handleSortClick = (col: string) => {
    if (sortCol === col) {
      setSortDir((d) => (d === 'asc' ? 'desc' : 'asc'));
    } else {
      setSortCol(col);
      setSortDir('asc');
    }
  };

  const sortedRows = useMemo(() => {
    if (!sortCol) return activeRows;
    return [...activeRows].sort((a, b) => {
      const av = a[sortCol];
      const bv = b[sortCol];
      if (av == null && bv == null) return 0;
      if (av == null) return 1;
      if (bv == null) return -1;
      if (typeof av === 'number' && typeof bv === 'number') {
        return sortDir === 'asc' ? av - bv : bv - av;
      }
      const cmp = String(av).localeCompare(String(bv), undefined, {
        numeric: true,
        sensitivity: 'base',
      });
      return sortDir === 'asc' ? cmp : -cmp;
    });
  }, [activeRows, sortCol, sortDir]);

  const activeError = mode === 'ai' ? nlError : error;
  // True once an NL query has completed (success with any row count, including 0)
  const nlQueryRan = mode === 'ai' && nlInterpretation != null;
  const showResultsArea = activeHeaders.length > 0 || nlQueryRan;

  // Ask AI requires login (every call consumes credits). When logged out, the Generate
  // button sends the user to sign in instead of firing a request that would 401.
  const handleGenerate = () => {
    if (!isLoggedIn) {
      window.location.href = `/api/account/login/Google?ReturnUrl=${encodeURIComponent(
        window.location.pathname,
      )}`;
      return;
    }
    if (nlQuery.trim()) onRunNlSummarize(nlQuery.trim());
  };

  // Clickable example questions — shown both in the input panel and in the "no data" guidance,
  // so a user whose question matched nothing can fall back to a known-good query.
  const suggestionChips = (
    <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 0.75, mt: 1 }}>
      {NL_EXAMPLES.map((ex) => (
        <Chip
          key={ex}
          label={ex}
          size="small"
          variant="outlined"
          clickable
          onClick={() => setNlQuery(ex)}
        />
      ))}
    </Box>
  );

  return (
    <>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
        <strong>SummarizeAsync</strong> — GroupBy + aggregated Measures, with optional Filters.
      </Typography>

      {/* ── Mode toggle ────────────────────────────────────────────────────── */}
      <ToggleButtonGroup
        size="small"
        exclusive
        value={mode}
        onChange={(_e, v) => {
          if (v !== null) setMode(v);
        }}
        aria-label="summarize mode"
        sx={{ mb: 2 }}
      >
        <ToggleButton value="manual" aria-label="manual mode">
          <SettingsIcon fontSize="small" sx={{ mr: 0.5 }} />
          Manual
        </ToggleButton>
        <ToggleButton value="ai" aria-label="ask AI">
          <AutoAwesomeIcon fontSize="small" sx={{ mr: 0.5 }} />
          Ask AI
        </ToggleButton>
      </ToggleButtonGroup>

      {/* ── Manual mode ────────────────────────────────────────────────────── */}
      {mode === 'manual' && (
        <Paper variant="outlined" sx={{ p: 2, mb: 3 }}>
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

          <ViewByEditor
            entries={viewByEntries}
            fields={fields}
            transforms={fieldTransforms}
            onAdd={onViewByAdd}
            onUpdate={onViewByUpdate}
            onRemove={onViewByRemove}
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
      )}

      {/* ── AI mode ────────────────────────────────────────────────────────── */}
      {mode === 'ai' && (
        <Paper
          variant="outlined"
          sx={{
            p: 2.5,
            mb: 3,
            borderColor: 'rgba(94, 106, 210, 0.35)',
            backgroundColor: 'rgba(94, 106, 210, 0.05)',
          }}
        >
          <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
            Describe what you want to see in plain language. The AI will translate your request into
            a query and return the results.
          </Typography>

          <TextField
            multiline
            minRows={2}
            maxRows={5}
            fullWidth
            placeholder='e.g. "Give me total revenue by region for the TW area"'
            value={nlQuery}
            onChange={(e) => setNlQuery(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter' && (e.metaKey || e.ctrlKey) && nlQuery.trim()) {
                handleGenerate();
              }
            }}
            sx={{ mb: 1.5 }}
          />

          <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 0.75, mb: 2 }}>
            {NL_EXAMPLES.map((ex) => (
              <Chip
                key={ex}
                label={ex}
                size="small"
                variant="outlined"
                onClick={() => setNlQuery(ex)}
                clickable
              />
            ))}
          </Box>

          <Button
            variant="contained"
            startIcon={isLoggedIn ? <AutoAwesomeIcon /> : <LoginIcon />}
            onClick={handleGenerate}
            disabled={isLoggedIn && (nlIsLoading || !nlQuery.trim())}
          >
            {!isLoggedIn ? 'Sign in to use AI' : nlIsLoading ? 'Generating…' : 'Generate'}
          </Button>

          <Typography variant="caption" color="text.secondary" sx={{ ml: 1.5 }}>
            {isLoggedIn
              ? 'Costs 2 credits per query'
              : 'Requires login · costs 2 credits per query'}
          </Typography>
        </Paper>
      )}

      {activeError && mode === 'manual' && (
        <Alert severity="error" sx={{ mb: 2 }}>
          Query failed. Check console for details.
        </Alert>
      )}

      {activeError && mode === 'ai' && (
        <Alert severity="warning" sx={{ mb: 2 }}>
          <Typography variant="body2">
            The AI couldn&apos;t answer this question — this data may not have a field or value that
            matches it. Try one of these instead:
          </Typography>
          {suggestionChips}
        </Alert>
      )}

      {showResultsArea && (
        <>
          {/* ── Results header ── */}
          <div
            style={{
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'space-between',
              marginBottom: 8,
            }}
          >
            <Box>
              <Typography variant="caption" color="text.secondary">
                {activeRows.length} rows returned
              </Typography>
              {mode === 'ai' && nlInterpretation && (
                <Typography
                  component="p"
                  variant="caption"
                  color="text.secondary"
                  sx={{ fontStyle: 'italic', display: 'block' }}
                >
                  {nlInterpretation}
                </Typography>
              )}
            </Box>

            <ToggleButtonGroup
              size="small"
              exclusive
              value={chartType}
              onChange={(_e, v) => {
                if (v !== null) setChartType(v);
              }}
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

          {/* ── Dropped-filter notes (rows came back, but a requested value didn't exist) ── */}
          {mode === 'ai' && nlNotes && nlNotes.length > 0 && (
            <Alert severity="warning" sx={{ mb: 2 }}>
              {nlNotes.map((note) => (
                <div key={note}>{note}</div>
              ))}
            </Alert>
          )}

          {/* ── Empty state ── */}
          {activeHeaders.length === 0 && (
            <Alert severity="warning" sx={{ mb: 2 }}>
              <Typography variant="body2">
                No matching data for this question — the date or value you asked for may not exist
                in this data. Try one of these instead:
              </Typography>
              {suggestionChips}
            </Alert>
          )}

          {/* ── Table view ── */}
          {activeHeaders.length > 0 && chartType === 'table' && (
            <TableContainer component={Paper} variant="outlined">
              <Table size="small">
                <TableHead>
                  <TableRow>
                    {activeHeaders.map((h) => (
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
                      {activeHeaders.map((h) => (
                        <TableCell key={h}>{formatCellValue(row[h])}</TableCell>
                      ))}
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </TableContainer>
          )}

          {/* ── Chart view ── */}
          {activeHeaders.length > 0 && chartType !== 'table' && (
            <SummarizeChart chartType={chartType} rows={activeRows} headers={activeHeaders} />
          )}
        </>
      )}
    </>
  );
}

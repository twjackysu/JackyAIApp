import AutoAwesomeIcon from '@mui/icons-material/AutoAwesome';
import BarChartIcon from '@mui/icons-material/BarChart';
import CheckIcon from '@mui/icons-material/Check';
import CodeIcon from '@mui/icons-material/Code';
import HistoryIcon from '@mui/icons-material/History';
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
import CircularProgress from '@mui/material/CircularProgress';
import Collapse from '@mui/material/Collapse';
import Divider from '@mui/material/Divider';
import IconButton from '@mui/material/IconButton';
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
import { inferColumns } from './summarizeChart/dataPrep';
import { ViewByEditor } from './ViewByEditor';

import type {
  FieldDefinition,
  MeasureFieldDto,
  SummarizeRequest,
} from '../../apis/dataAnalysisApis/types';
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
  nlPlan?: SummarizeRequest;
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

// Mix of starter and realistic "what a sales/finance team would actually ask"
// questions — the complex ones exercise multi-dimension group-bys, filters,
// NumericBin tiers, and weekly DateTrunc with relative dates.
// Values and tier boundaries verified against the live dbo.Orders data
// (Revenue spans ~1K–10K; categories are Consulting/Hardware/Software/
// Support/Training; statuses are Completed/Pending/Cancelled).
const NL_EXAMPLES = [
  'Total revenue by region',
  'Monthly order count trend',
  'Quarterly revenue by sales channel for Enterprise customers in EMEA',
  'Orders and revenue by deal size tier (<3K, 3K–6K, >6K)',
  'Weekly completed-order revenue for Software this year',
];

// Staged progress shown while the NL agent runs. Mirrors the backend's real
// phases (plan → fetch live values → execute); timing is simulated locally
// since the request is a single HTTP call.
const NL_STEPS = [
  'Understanding your question',
  'Checking real values in your data',
  'Running the query',
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
  nlPlan,
  nlNotes,
  isLoggedIn,
  fields,
  sourceId,
  tableName,
}: SummarizeTabProps) {
  const [mode, setMode] = useState<SummarizeMode>('ai');
  const [nlQuery, setNlQuery] = useState('');
  const [chartType, setChartType] = useState<ChartViewType>('table');
  const [recentQueries, setRecentQueries] = useState<string[]>([]);
  const [planOpen, setPlanOpen] = useState(false);
  const [loadStep, setLoadStep] = useState(0);

  // Advance the simulated NL progress steps while the request is in flight
  useEffect(() => {
    if (!nlIsLoading) return;
    setLoadStep(0);
    const t1 = setTimeout(() => setLoadStep(1), 2200);
    const t2 = setTimeout(() => setLoadStep(2), 5500);
    return () => {
      clearTimeout(t1);
      clearTimeout(t2);
    };
  }, [nlIsLoading]);

  // When a new AI result arrives, pick a sensible default view from its shape:
  // temporal dimension → line, two dimensions → stacked column,
  // one dimension → column, anything else → table.
  useEffect(() => {
    if (nlRows.length === 0) return;
    setPlanOpen(false);
    const headers = Object.keys(nlRows[0]);
    const { groupKeys, measureKeys } = inferColumns(headers, nlRows);
    let suggested: ChartViewType = 'table';
    if (measureKeys.length > 0 && groupKeys.length > 0 && nlRows.length > 1) {
      const firstVal = String(nlRows[0][groupKeys[0]] ?? '');
      const looksTemporal =
        /date|month|year|week|quarter|day|time/i.test(groupKeys[0]) ||
        /^\d{4}([-/]\d{1,2})?/.test(firstVal);
      suggested = groupKeys.length >= 2 ? 'stackedColumn' : looksTemporal ? 'line' : 'column';
    }
    setChartType(suggested);
  }, [nlRows]);

  // Human-readable rendering of the query plan the AI generated
  const planChips = useMemo(() => {
    if (!nlPlan) return [];
    const chips: string[] = [];
    for (const g of nlPlan.viewBy ?? []) {
      if (g.type === 'DateTrunc' && g.granularity)
        chips.push(`by ${g.granularity}(${g.fieldName})`);
      else if (g.type === 'NumericBin') chips.push(`by ${g.fieldName} (ranges)`);
      else if (g.type === 'StringGrouping') chips.push(`by ${g.fieldName} (grouped)`);
      else chips.push(`by ${g.fieldName}`);
    }
    for (const m of nlPlan.measures ?? []) chips.push(`${m.aggregation}(${m.fieldName})`);
    for (const f of nlPlan.filters ?? []) {
      const shown = f.values.slice(0, 3).join(', ');
      const extra = f.values.length > 3 ? ` +${f.values.length - 3}` : '';
      chips.push(`${f.fieldName} ${f.op ?? 'In'} [${shown}${extra}]`);
    }
    return chips;
  }, [nlPlan]);

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
    const q = nlQuery.trim();
    if (q) {
      setRecentQueries((prev) => [q, ...prev.filter((p) => p !== q)].slice(0, 5));
      onRunNlSummarize(q);
    }
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
      {mode === 'manual' && (
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
          <strong>SummarizeAsync</strong> — GroupBy + aggregated Measures, with optional Filters.
        </Typography>
      )}

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

          {recentQueries.length > 0 && (
            <Box sx={{ display: 'flex', alignItems: 'center', flexWrap: 'wrap', gap: 0.75, mb: 2 }}>
              <Tooltip title="Recent questions">
                <HistoryIcon sx={{ fontSize: 16, color: 'text.secondary' }} />
              </Tooltip>
              {recentQueries.map((q) => (
                <Chip
                  key={q}
                  label={q}
                  size="small"
                  onClick={() => setNlQuery(q)}
                  clickable
                  sx={{ maxWidth: 320 }}
                />
              ))}
            </Box>
          )}

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

      {/* ── Staged AI progress ─────────────────────────────────────────────── */}
      {mode === 'ai' && nlIsLoading && (
        <Paper variant="outlined" sx={{ p: 2, mb: 2 }}>
          {NL_STEPS.map((step, i) => (
            <Box key={step} sx={{ display: 'flex', alignItems: 'center', gap: 1.25, py: 0.5 }}>
              {i < loadStep ? (
                <CheckIcon sx={{ fontSize: 16, color: 'success.main' }} />
              ) : i === loadStep ? (
                <CircularProgress size={14} thickness={5} />
              ) : (
                <Box sx={{ width: 16, display: 'flex', justifyContent: 'center' }}>
                  <Box
                    sx={{ width: 5, height: 5, borderRadius: '50%', bgcolor: 'text.disabled' }}
                  />
                </Box>
              )}
              <Typography
                variant="body2"
                sx={{ color: i <= loadStep ? 'text.primary' : 'text.disabled' }}
              >
                {step}
              </Typography>
            </Box>
          ))}
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

          {/* ── How the AI queried this (plan rendered as readable chips) ── */}
          {mode === 'ai' && planChips.length > 0 && (
            <Box
              sx={{ display: 'flex', alignItems: 'center', flexWrap: 'wrap', gap: 0.75, mb: 1.5 }}
            >
              {planChips.map((c) => (
                <Chip key={c} label={c} size="small" variant="outlined" />
              ))}
              <Tooltip title="View the raw query plan">
                <IconButton size="small" onClick={() => setPlanOpen((o) => !o)}>
                  <CodeIcon sx={{ fontSize: 16 }} />
                </IconButton>
              </Tooltip>
            </Box>
          )}
          {mode === 'ai' && nlPlan && (
            <Collapse in={planOpen}>
              <Paper variant="outlined" sx={{ p: 1.5, mb: 1.5, overflow: 'auto' }}>
                <Box
                  component="pre"
                  sx={{
                    m: 0,
                    fontSize: '0.72rem',
                    fontFamily: 'monospace',
                    color: 'text.secondary',
                  }}
                >
                  {JSON.stringify(nlPlan, null, 2)}
                </Box>
              </Paper>
            </Collapse>
          )}

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

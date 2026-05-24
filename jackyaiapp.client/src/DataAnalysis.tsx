import AddIcon from '@mui/icons-material/Add';
import CalendarTodayIcon from '@mui/icons-material/CalendarToday';
import DeleteIcon from '@mui/icons-material/Delete';
import FilterAltIcon from '@mui/icons-material/FilterAlt';
import PlayArrowIcon from '@mui/icons-material/PlayArrow';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Chip from '@mui/material/Chip';
import CircularProgress from '@mui/material/CircularProgress';
import Divider from '@mui/material/Divider';
import FormControl from '@mui/material/FormControl';
import IconButton from '@mui/material/IconButton';
import InputLabel from '@mui/material/InputLabel';
import MenuItem from '@mui/material/MenuItem';
import OutlinedInput from '@mui/material/OutlinedInput';
import Paper from '@mui/material/Paper';
import Select from '@mui/material/Select';
import Tab from '@mui/material/Tab';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableContainer from '@mui/material/TableContainer';
import TableHead from '@mui/material/TableHead';
import TablePagination from '@mui/material/TablePagination';
import TableRow from '@mui/material/TableRow';
import Tabs from '@mui/material/Tabs';
import Typography from '@mui/material/Typography';
import { useEffect, useState } from 'react';

import {
  useDetailsMutation,
  useGetDistinctValuesQuery,
  useGetMaxDateQuery,
  useGetSchemaQuery,
  useSummarizeMutation,
} from './apis/dataAnalysisApis';
import type {
  AggregationType,
  ColumnDataType,
  FieldDefinition,
  FilterDimensionDto,
  MeasureFieldDto,
} from './apis/dataAnalysisApis/types';

// ── Constants ─────────────────────────────────────────────────────────────────

const AGGREGATION_TYPES: AggregationType[] = [
  'Count', 'DistinctCount', 'Sum', 'Average', 'Max', 'Min',
];
const ROWS_PER_PAGE = 50;

// ── Types ─────────────────────────────────────────────────────────────────────

interface ActiveFilter {
  id: number;
  fieldName: string;
  values: string[];
}

// ── Helpers ───────────────────────────────────────────────────────────────────

function toFilterDtos(filters: ActiveFilter[]): FilterDimensionDto[] {
  return filters
    .filter((f) => f.fieldName && f.values.length > 0)
    .map(({ fieldName, values }) => ({ fieldName, values }));
}

function canonicalTypeColor(
  type: ColumnDataType,
): 'default' | 'primary' | 'secondary' | 'success' | 'warning' | 'info' {
  switch (type) {
    case 'Integer': case 'Float': case 'Decimal': return 'primary';
    case 'Boolean': return 'warning';
    case 'DateTime': case 'DateTimeOffset': case 'Date': case 'Time': return 'success';
    case 'Guid': return 'info';
    default: return 'default';
  }
}

function formatCellValue(value: unknown): string {
  if (value === null || value === undefined) return '—';
  if (typeof value === 'string') {
    if (/^\d{4}-\d{2}-\d{2}T/.test(value)) return value.substring(0, 10);
    return value;
  }
  if (typeof value === 'number') return Number.isInteger(value) ? String(value) : value.toFixed(2);
  return String(value);
}

// ── Sub-components ────────────────────────────────────────────────────────────

interface TabPanelProps { children?: React.ReactNode; index: number; value: number; }
function TabPanel({ children, value, index }: TabPanelProps) {
  return (
    <div role="tabpanel" hidden={value !== index}>
      {value === index && <Box sx={{ pt: 3 }}>{children}</Box>}
    </div>
  );
}

// FilterRow: one field selector + multi-select LoV for that field
interface FilterRowProps {
  filter: ActiveFilter;
  fields: FieldDefinition[];
  onUpdate: (f: ActiveFilter) => void;
  onRemove: () => void;
}
function FilterRow({ filter, fields, onUpdate, onRemove }: FilterRowProps) {
  const { data: lvData, isFetching } = useGetDistinctValuesQuery(
    filter.fieldName,
    { skip: !filter.fieldName },
  );
  const distinctValues = lvData?.data?.values ?? [];

  return (
    <Box sx={{ display: 'flex', gap: 1, mb: 1, alignItems: 'flex-start' }}>
      {/* Field selector */}
      <FormControl size="small" sx={{ flex: '0 0 160px' }}>
        <InputLabel>Field</InputLabel>
        <Select
          value={filter.fieldName}
          label="Field"
          onChange={(e) =>
            onUpdate({ ...filter, fieldName: e.target.value as string, values: [] })
          }
        >
          {fields.map((f) => (
            <MenuItem key={f.name} value={f.name}>{f.name}</MenuItem>
          ))}
        </Select>
      </FormControl>

      {/* Values multi-select (LoV) */}
      <FormControl size="small" sx={{ flex: 1 }}>
        <InputLabel>
          {isFetching ? 'Loading…' : filter.fieldName ? 'Values' : 'Select a field first'}
        </InputLabel>
        <Select
          multiple
          value={filter.values}
          disabled={!filter.fieldName || isFetching}
          onChange={(e) =>
            onUpdate({
              ...filter,
              values: typeof e.target.value === 'string'
                ? e.target.value.split(',')
                : (e.target.value as string[]),
            })
          }
          input={<OutlinedInput label={isFetching ? 'Loading…' : filter.fieldName ? 'Values' : 'Select a field first'} />}
          renderValue={(sel) => (
            <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 0.5 }}>
              {sel.map((v) => <Chip key={v} label={v} size="small" />)}
            </Box>
          )}
        >
          {isFetching
            ? <MenuItem disabled><em>Loading values…</em></MenuItem>
            : distinctValues.length === 0
              ? <MenuItem disabled><em>No values found</em></MenuItem>
              : distinctValues.map((v) => <MenuItem key={v} value={v}>{v}</MenuItem>)
          }
        </Select>
      </FormControl>

      <IconButton size="small" onClick={onRemove} title="Remove filter" sx={{ mt: 0.5 }}>
        <DeleteIcon fontSize="small" />
      </IconButton>
    </Box>
  );
}

// FilterEditor: manages a list of FilterRows
interface FilterEditorProps {
  filters: ActiveFilter[];
  fields: FieldDefinition[];
  onChange: (filters: ActiveFilter[]) => void;
}
function FilterEditor({ filters, fields, onChange }: FilterEditorProps) {
  const addFilter = () =>
    onChange([...filters, { id: Date.now(), fieldName: '', values: [] }]);

  const removeFilter = (id: number) =>
    onChange(filters.filter((f) => f.id !== id));

  const updateFilter = (updated: ActiveFilter) =>
    onChange(filters.map((f) => (f.id === updated.id ? updated : f)));

  const activeCount = toFilterDtos(filters).length;

  return (
    <Box>
      <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', mb: 1 }}>
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
          <FilterAltIcon fontSize="small" color="action" />
          <Typography variant="subtitle2">
            Filters
            {activeCount > 0 && (
              <Chip label={activeCount} size="small" color="primary" sx={{ ml: 1 }} />
            )}
          </Typography>
        </Box>
        <IconButton size="small" onClick={addFilter} title="Add filter">
          <AddIcon fontSize="small" />
        </IconButton>
      </Box>

      {filters.length === 0 && (
        <Typography variant="caption" color="text.secondary">
          No filters — click <strong>+</strong> to add one.
        </Typography>
      )}

      {filters.map((f) => (
        <FilterRow
          key={f.id}
          filter={f}
          fields={fields}
          onUpdate={updateFilter}
          onRemove={() => removeFilter(f.id)}
        />
      ))}
    </Box>
  );
}

// ── Main Page ─────────────────────────────────────────────────────────────────

export default function DataAnalysis() {
  const [tab, setTab] = useState(0);

  // ── Schema ──
  const { data: schemaData, isLoading: schemaLoading, error: schemaError } = useGetSchemaQuery();
  const fields: FieldDefinition[] = schemaData?.data?.fields ?? [];

  // ── Summarize ──
  const [viewByFields, setViewByFields] = useState<string[]>(['Region']);
  const [measures, setMeasures] = useState<MeasureFieldDto[]>([
    { fieldName: 'Revenue', aggregation: 'Sum' },
  ]);
  const [summarizeFilters, setSummarizeFilters] = useState<ActiveFilter[]>([]);
  const [summarize, { data: summarizeData, isLoading: summarizeLoading, error: summarizeError }] =
    useSummarizeMutation();

  // ── Details ──
  const [page, setPage] = useState(0);
  const [detailsFilters, setDetailsFilters] = useState<ActiveFilter[]>([]);
  const [details, { data: detailsData, isLoading: detailsLoading, error: detailsError }] =
    useDetailsMutation();

  // ── MaxDate ──
  const { data: maxDateData, isLoading: maxDateLoading } = useGetMaxDateQuery();

  // Auto-load details when Details tab is active, page changes, or filters change
  useEffect(() => {
    if (tab === 2) {
      details({
        orderBy: [{ fieldName: 'OrderDate', descending: true }],
        skip: page * ROWS_PER_PAGE,
        take: ROWS_PER_PAGE,
        filters: toFilterDtos(detailsFilters),
      });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tab, page, detailsFilters]);

  // ── Summarize handlers ──

  const handleRunSummarize = () => {
    if (viewByFields.length === 0 || measures.length === 0) return;
    summarize({
      viewBy: viewByFields,
      measures,
      filters: toFilterDtos(summarizeFilters),
    });
  };

  const addMeasure = () =>
    setMeasures((prev) => [...prev, { fieldName: fields[0]?.name ?? 'Quantity', aggregation: 'Sum' }]);

  const removeMeasure = (i: number) =>
    setMeasures((prev) => prev.filter((_, idx) => idx !== i));

  const updateMeasure = (i: number, patch: Partial<MeasureFieldDto>) =>
    setMeasures((prev) => prev.map((m, idx) => (idx === i ? { ...m, ...patch } : m)));

  // Reset page when details filters change
  const handleDetailsFiltersChange = (newFilters: ActiveFilter[]) => {
    setDetailsFilters(newFilters);
    setPage(0);
  };

  // ── Derived ──

  const summarizeRows = summarizeData?.data?.rows ?? [];
  const summarizeHeaders = summarizeRows.length > 0 ? Object.keys(summarizeRows[0]) : [];

  const detailsRows = detailsData?.data?.rows ?? [];
  const detailsHeaders = detailsRows.length > 0 ? Object.keys(detailsRows[0]) : [];
  const detailsTotalCount = detailsData?.data?.totalCount ?? 0;

  const maxDate = maxDateData?.data?.maxDate;

  // ── Render ────────────────────────────────────────────────────────────────

  return (
    <Box>
      <Typography variant="h5" gutterBottom>
        DataAnalysis Library — Live Demo
      </Typography>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
        All 4 query types demonstrated against{' '}
        <code style={{ background: '#f0f0f0', padding: '0 4px', borderRadius: 3 }}>dbo.Orders</code>
      </Typography>

      <Box sx={{ borderBottom: 1, borderColor: 'divider' }}>
        <Tabs value={tab} onChange={(_, v: number) => setTab(v)}>
          <Tab label="① Schema" />
          <Tab label="② Summarize" />
          <Tab label="③ Details" />
          <Tab label="④ Max Date" />
        </Tabs>
      </Box>

      {/* ── Tab 1: Schema ──────────────────────────────────────────────── */}
      <TabPanel value={tab} index={0}>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
          <strong>FieldListAsync</strong> — returns the schema of <code>dbo.Orders</code>.
        </Typography>

        {schemaLoading && <CircularProgress size={24} />}
        {schemaError && <Alert severity="error">Failed to load schema.</Alert>}

        {fields.length > 0 && (
          <TableContainer component={Paper} variant="outlined">
            <Table size="small">
              <TableHead>
                <TableRow>
                  <TableCell>Column</TableCell>
                  <TableCell>Native Type</TableCell>
                  <TableCell>Canonical Type</TableCell>
                  <TableCell>Nullable</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {fields.map((f) => (
                  <TableRow key={f.name} hover>
                    <TableCell sx={{ fontWeight: 500 }}>{f.name}</TableCell>
                    <TableCell><code>{f.dataType}</code></TableCell>
                    <TableCell>
                      <Chip label={f.canonicalType} size="small" color={canonicalTypeColor(f.canonicalType)} />
                    </TableCell>
                    <TableCell>{f.isNullable ? 'Yes' : 'No'}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </TableContainer>
        )}
      </TabPanel>

      {/* ── Tab 2: Summarize ───────────────────────────────────────────── */}
      <TabPanel value={tab} index={1}>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
          <strong>SummarizeAsync</strong> — GroupBy + aggregated Measures, with optional Filters.
        </Typography>

        <Paper variant="outlined" sx={{ p: 2, mb: 3 }}>
          {/* ViewBy */}
          <Typography variant="subtitle2" gutterBottom>Group By (ViewBy)</Typography>
          <FormControl fullWidth size="small" sx={{ mb: 2 }}>
            <InputLabel>Select fields to group by</InputLabel>
            <Select
              multiple
              value={viewByFields}
              onChange={(e) =>
                setViewByFields(
                  typeof e.target.value === 'string'
                    ? e.target.value.split(',')
                    : (e.target.value as string[]),
                )
              }
              input={<OutlinedInput label="Select fields to group by" />}
              renderValue={(sel) => (
                <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 0.5 }}>
                  {sel.map((v) => <Chip key={v} label={v} size="small" />)}
                </Box>
              )}
            >
              {fields.map((f) => <MenuItem key={f.name} value={f.name}>{f.name}</MenuItem>)}
            </Select>
          </FormControl>

          <Divider sx={{ my: 2 }} />

          {/* Measures */}
          <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', mb: 1 }}>
            <Typography variant="subtitle2">Measures</Typography>
            <IconButton size="small" onClick={addMeasure} title="Add measure">
              <AddIcon fontSize="small" />
            </IconButton>
          </Box>

          {measures.map((m, i) => (
            <Box key={i} sx={{ display: 'flex', gap: 1, mb: 1, alignItems: 'center' }}>
              <FormControl size="small" sx={{ flex: 2 }}>
                <InputLabel>Field</InputLabel>
                <Select
                  value={m.fieldName}
                  label="Field"
                  onChange={(e) => updateMeasure(i, { fieldName: e.target.value as string })}
                >
                  {fields.map((f) => <MenuItem key={f.name} value={f.name}>{f.name}</MenuItem>)}
                </Select>
              </FormControl>
              <FormControl size="small" sx={{ flex: 1 }}>
                <InputLabel>Aggregation</InputLabel>
                <Select
                  value={m.aggregation}
                  label="Aggregation"
                  onChange={(e) => updateMeasure(i, { aggregation: e.target.value as AggregationType })}
                >
                  {AGGREGATION_TYPES.map((a) => <MenuItem key={a} value={a}>{a}</MenuItem>)}
                </Select>
              </FormControl>
              <IconButton size="small" onClick={() => removeMeasure(i)} disabled={measures.length === 1}>
                <DeleteIcon fontSize="small" />
              </IconButton>
            </Box>
          ))}

          <Divider sx={{ my: 2 }} />

          {/* Filters */}
          <FilterEditor
            filters={summarizeFilters}
            fields={fields}
            onChange={setSummarizeFilters}
          />

          <Button
            variant="contained"
            startIcon={<PlayArrowIcon />}
            onClick={handleRunSummarize}
            disabled={summarizeLoading || viewByFields.length === 0 || measures.length === 0}
            sx={{ mt: 2 }}
          >
            {summarizeLoading ? 'Running…' : 'Run Query'}
          </Button>
        </Paper>

        {summarizeError && (
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
      </TabPanel>

      {/* ── Tab 3: Details ─────────────────────────────────────────────── */}
      <TabPanel value={tab} index={2}>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
          <strong>DetailsAsync</strong> — paginated row-level data, ordered by{' '}
          <code>OrderDate DESC</code>. Filters apply immediately.
        </Typography>

        {/* Filters panel */}
        <Paper variant="outlined" sx={{ p: 2, mb: 2 }}>
          <FilterEditor
            filters={detailsFilters}
            fields={fields}
            onChange={handleDetailsFiltersChange}
          />
        </Paper>

        {detailsLoading && <CircularProgress size={24} sx={{ mb: 1 }} />}
        {detailsError && <Alert severity="error" sx={{ mb: 2 }}>Failed to load rows.</Alert>}

        {detailsHeaders.length > 0 && (
          <Paper variant="outlined">
            <TableContainer sx={{ maxHeight: 500 }}>
              <Table size="small" stickyHeader>
                <TableHead>
                  <TableRow>
                    {detailsHeaders.map((h) => (
                      <TableCell key={h} sx={{ whiteSpace: 'nowrap' }}>{h}</TableCell>
                    ))}
                  </TableRow>
                </TableHead>
                <TableBody>
                  {detailsRows.map((row, i) => (
                    <TableRow key={i} hover>
                      {detailsHeaders.map((h) => (
                        <TableCell
                          key={h}
                          sx={{ maxWidth: 160, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}
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
              onPageChange={(_, newPage) => setPage(newPage)}
              labelDisplayedRows={({ from, to, count }) =>
                `${from}–${to} of ${count > 0 ? count : `more than ${to}`}`
              }
            />
          </Paper>
        )}
      </TabPanel>

      {/* ── Tab 4: Max Date ────────────────────────────────────────────── */}
      <TabPanel value={tab} index={3}>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
          <strong>MaxDateAsync</strong> — <code>MAX(OrderDate)</code> from <code>dbo.Orders</code>.
        </Typography>

        {maxDateLoading && <CircularProgress size={24} />}

        <Box sx={{ display: 'flex', justifyContent: 'center', pt: 2 }}>
          <Card variant="outlined" sx={{ minWidth: 320, textAlign: 'center' }}>
            <CardContent sx={{ py: 4 }}>
              <CalendarTodayIcon sx={{ fontSize: 56, color: 'primary.main', mb: 2 }} />
              <Typography variant="subtitle1" color="text.secondary" gutterBottom>
                Latest record in dbo.Orders
              </Typography>
              <Typography variant="h3" color="primary" sx={{ fontWeight: 700 }}>
                {maxDate
                  ? new Date(maxDate).toLocaleDateString('en-US', {
                      year: 'numeric', month: 'short', day: 'numeric',
                    })
                  : maxDateLoading ? '…' : '—'}
              </Typography>
              {maxDate && (
                <Typography variant="caption" color="text.secondary" sx={{ mt: 1, display: 'block' }}>
                  {maxDate}
                </Typography>
              )}
            </CardContent>
          </Card>
        </Box>
      </TabPanel>
    </Box>
  );
}

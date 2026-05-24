import AddIcon from '@mui/icons-material/Add';
import CalendarTodayIcon from '@mui/icons-material/CalendarToday';
import DeleteIcon from '@mui/icons-material/Delete';
import EditIcon from '@mui/icons-material/Edit';
import FilterAltIcon from '@mui/icons-material/FilterAlt';
import PlayArrowIcon from '@mui/icons-material/PlayArrow';
import SettingsIcon from '@mui/icons-material/Settings';
import WarningAmberIcon from '@mui/icons-material/WarningAmber';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Chip from '@mui/material/Chip';
import CircularProgress from '@mui/material/CircularProgress';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
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
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { useEffect, useState } from 'react';

import { useGetUserInfoQuery } from './apis/accountApis';
import {
  useCreateSourceMutation,
  useDeleteSourceMutation,
  useDetailsMutation,
  useGetDistinctValuesQuery,
  useGetMaxDateQuery,
  useGetSchemaQuery,
  useGetSourcesQuery,
  useSummarizeMutation,
  useUpdateSourceMutation,
} from './apis/dataAnalysisApis';
import type {
  AdxSourceConfig,
  AggregationType,
  ColumnDataType,
  DataSourceConfigDto,
  DataSourceConnectorType,
  FieldDefinition,
  FilterDimensionDto,
  MeasureFieldDto,
  MsSqlSourceConfig,
  SaveDataSourceConfigDto,
} from './apis/dataAnalysisApis/types';

// ── Date-type canonical types (used for auto-detecting the date field) ────────
const DATE_CANONICAL_TYPES: ColumnDataType[] = [
  'DateTime', 'DateTimeOffset', 'Date',
];

// ── Constants ─────────────────────────────────────────────────────────────────

const AGGREGATION_TYPES: AggregationType[] = [
  'Count', 'DistinctCount', 'Sum', 'Average', 'Max', 'Min',
];
const ROWS_PER_PAGE = 50;
const DEFAULT_TABLE = 'dbo.Orders';

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
  sourceId?: string;
  tableName?: string;
}
function FilterRow({ filter, fields, onUpdate, onRemove, sourceId, tableName }: FilterRowProps) {
  const { data: lvData, isFetching } = useGetDistinctValuesQuery(
    { field: filter.fieldName, sourceId, tableName },
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
  sourceId?: string;
  tableName?: string;
}
function FilterEditor({ filters, fields, onChange, sourceId, tableName }: FilterEditorProps) {
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
          sourceId={sourceId}
          tableName={tableName}
        />
      ))}
    </Box>
  );
}

// ── SourceManagerDialog ───────────────────────────────────────────────────────

interface SourceManagerDialogProps {
  open: boolean;
  onClose: () => void;
}

function SourceManagerDialog({ open, onClose }: SourceManagerDialogProps) {
  const { data: sourcesRes, isLoading } = useGetSourcesQuery(undefined, { skip: !open });
  const [createSource, { isLoading: creating }] = useCreateSourceMutation();
  const [updateSource, { isLoading: updating }] = useUpdateSourceMutation();
  const [deleteSource] = useDeleteSourceMutation();

  const [view, setView] = useState<'list' | 'form'>('list');
  const [editTarget, setEditTarget] = useState<DataSourceConfigDto | null>(null);

  // ── Form state ──
  const [formName, setFormName] = useState('');
  const [formType, setFormType] = useState<DataSourceConnectorType>('MsSql');
  const [formDefaultTable, setFormDefaultTable] = useState('');
  // MsSql
  const [formConnStr, setFormConnStr] = useState('');
  // Adx
  const [formClusterUri, setFormClusterUri] = useState('');
  const [formDatabase, setFormDatabase] = useState('');
  const [formTenantId, setFormTenantId] = useState('');
  const [formClientId, setFormClientId] = useState('');
  const [formClientSecret, setFormClientSecret] = useState('');
  const [saveError, setSaveError] = useState<string | null>(null);

  const sources = sourcesRes?.data ?? [];
  const saving = creating || updating;

  // Pre-fill form fields when switching to form view
  useEffect(() => {
    if (view !== 'form') return;
    setSaveError(null);
    if (editTarget) {
      setFormName(editTarget.name);
      setFormType(editTarget.connectorType);
      setFormDefaultTable(editTarget.defaultTableName ?? '');
      if (editTarget.connectorType === 'MsSql') {
        const c = editTarget.config as MsSqlSourceConfig;
        setFormConnStr(c.connectionString ?? '');
      } else {
        const c = editTarget.config as AdxSourceConfig;
        setFormClusterUri(c.clusterUri ?? '');
        setFormDatabase(c.database ?? '');
        setFormTenantId(c.tenantId ?? '');
        setFormClientId(c.clientId ?? '');
        setFormClientSecret(c.clientSecret ?? '');
      }
    } else {
      setFormName('');
      setFormType('MsSql');
      setFormDefaultTable('');
      setFormConnStr('');
      setFormClusterUri('');
      setFormDatabase('');
      setFormTenantId('');
      setFormClientId('');
      setFormClientSecret('');
    }
  }, [view, editTarget]);

  const openNewForm = () => { setEditTarget(null); setView('form'); };
  const openEditForm = (s: DataSourceConfigDto) => { setEditTarget(s); setView('form'); };

  const handleDelete = async (id: string) => {
    if (!window.confirm('Delete this data source? This action cannot be undone.')) return;
    try { await deleteSource(id).unwrap(); }
    catch { /* ignore — RTK Query logs it */ }
  };

  const handleSave = async () => {
    setSaveError(null);
    const config: MsSqlSourceConfig | AdxSourceConfig = formType === 'MsSql'
      ? { connectionString: formConnStr }
      : {
          clusterUri:       formClusterUri,
          database:         formDatabase,
          tenantId:         formTenantId     || null,
          clientId:         formClientId     || null,
          clientSecret:     formClientSecret || null,
          connectionString: null,
        };

    const dto: SaveDataSourceConfigDto = {
      name:             formName,
      connectorType:    formType,
      config,
      defaultTableName: formDefaultTable || null,
    };

    try {
      if (editTarget) {
        await updateSource({ id: editTarget.id, dto }).unwrap();
      } else {
        await createSource(dto).unwrap();
      }
      setView('list');
    } catch {
      setSaveError('Save failed — please check the connection details and try again.');
    }
  };

  const handleClose = () => { setView('list'); setEditTarget(null); onClose(); };

  const canSave = !!formName && (
    formType === 'MsSql'
      ? !!formConnStr
      : !!formClusterUri && !!formDatabase
  );

  return (
    <Dialog open={open} onClose={handleClose} maxWidth="md" fullWidth>
      <DialogTitle sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
        <SettingsIcon fontSize="small" />
        {view === 'list'
          ? 'Manage Data Sources'
          : editTarget ? `Edit Source — ${editTarget.name}` : 'New Data Source'}
      </DialogTitle>

      <DialogContent dividers sx={{ minHeight: 320 }}>
        {/* ── List view ── */}
        {view === 'list' && (
          <>
            {isLoading && <CircularProgress size={24} />}
            {!isLoading && sources.length === 0 && (
              <Typography color="text.secondary" sx={{ mt: 1 }}>
                No saved data sources yet. Click <strong>Add Source</strong> to get started.
              </Typography>
            )}
            {sources.length > 0 && (
              <TableContainer>
                <Table size="small">
                  <TableHead>
                    <TableRow>
                      <TableCell>Name</TableCell>
                      <TableCell>Type</TableCell>
                      <TableCell>Default Table</TableCell>
                      <TableCell>Created</TableCell>
                      <TableCell align="right">Actions</TableCell>
                    </TableRow>
                  </TableHead>
                  <TableBody>
                    {sources.map((s) => (
                      <TableRow key={s.id} hover>
                        <TableCell sx={{ fontWeight: 500 }}>{s.name}</TableCell>
                        <TableCell>
                          <Chip
                            label={s.connectorType}
                            size="small"
                            color={s.connectorType === 'Adx' ? 'secondary' : 'primary'}
                          />
                        </TableCell>
                        <TableCell>{s.defaultTableName ?? '—'}</TableCell>
                        <TableCell>{new Date(s.createdAt).toLocaleDateString()}</TableCell>
                        <TableCell align="right">
                          <IconButton size="small" onClick={() => openEditForm(s)} title="Edit">
                            <EditIcon fontSize="small" />
                          </IconButton>
                          <IconButton
                            size="small" color="error"
                            onClick={() => handleDelete(s.id)} title="Delete"
                          >
                            <DeleteIcon fontSize="small" />
                          </IconButton>
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </TableContainer>
            )}
          </>
        )}

        {/* ── Form view ── */}
        {view === 'form' && (
          <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2, pt: 1 }}>
            <TextField
              label="Source Name" size="small" fullWidth required
              value={formName} onChange={(e) => setFormName(e.target.value)}
              placeholder="e.g. My Production DB"
            />

            <FormControl size="small" fullWidth>
              <InputLabel>Connector Type</InputLabel>
              <Select
                value={formType}
                label="Connector Type"
                onChange={(e) => setFormType(e.target.value as DataSourceConnectorType)}
                disabled={!!editTarget}  // cannot change type after creation
              >
                <MenuItem value="MsSql">SQL Server (MsSql)</MenuItem>
                <MenuItem value="Adx">Azure Data Explorer (ADX / Kusto)</MenuItem>
              </Select>
            </FormControl>

            {/* ── MsSql fields ── */}
            {formType === 'MsSql' && (
              <TextField
                label="Connection String" size="small" fullWidth required
                value={formConnStr} onChange={(e) => setFormConnStr(e.target.value)}
                type="password"
                inputProps={{ autoComplete: 'new-password' }}
                placeholder="Server=myserver;Database=mydb;User Id=sa;Password=..."
                helperText={
                  editTarget
                    ? "Leave as 'Password=***' to keep the existing password unchanged."
                    : 'Full ADO.NET connection string including credentials.'
                }
              />
            )}

            {/* ── ADX fields ── */}
            {formType === 'Adx' && (
              <>
                <TextField
                  label="Cluster URI" size="small" fullWidth required
                  value={formClusterUri} onChange={(e) => setFormClusterUri(e.target.value)}
                  placeholder="https://mycluster.eastus.kusto.windows.net"
                />
                <TextField
                  label="Database" size="small" fullWidth required
                  value={formDatabase} onChange={(e) => setFormDatabase(e.target.value)}
                />
                <Divider><Typography variant="caption" color="text.secondary">Service Principal (optional)</Typography></Divider>
                <TextField
                  label="Tenant ID" size="small" fullWidth
                  value={formTenantId} onChange={(e) => setFormTenantId(e.target.value)}
                />
                <TextField
                  label="Client ID / App ID" size="small" fullWidth
                  value={formClientId} onChange={(e) => setFormClientId(e.target.value)}
                />
                <TextField
                  label="Client Secret" size="small" fullWidth
                  type="password" inputProps={{ autoComplete: 'new-password' }}
                  value={formClientSecret} onChange={(e) => setFormClientSecret(e.target.value)}
                  helperText={editTarget ? "Leave as '***' to keep the existing secret." : undefined}
                />
              </>
            )}

            <Divider />

            <TextField
              label="Default Table Name (optional)" size="small" fullWidth
              value={formDefaultTable} onChange={(e) => setFormDefaultTable(e.target.value)}
              placeholder="e.g. dbo.Orders or mydb.myTable"
              helperText="Pre-filled in the Table field when this source is selected."
            />

            {saveError && <Alert severity="error">{saveError}</Alert>}
          </Box>
        )}
      </DialogContent>

      <DialogActions>
        {view === 'list' ? (
          <>
            <Button onClick={openNewForm} startIcon={<AddIcon />} variant="outlined">
              Add Source
            </Button>
            <Box sx={{ flex: 1 }} />
            <Button onClick={handleClose}>Close</Button>
          </>
        ) : (
          <>
            <Button onClick={() => setView('list')}>Cancel</Button>
            <Box sx={{ flex: 1 }} />
            <Button
              variant="contained"
              disabled={saving || !canSave}
              onClick={handleSave}
            >
              {saving ? 'Saving…' : 'Save'}
            </Button>
          </>
        )}
      </DialogActions>
    </Dialog>
  );
}

// ── Main Page ─────────────────────────────────────────────────────────────────

export default function DataAnalysis() {
  // ── Auth state ──
  const { data: userInfoRes } = useGetUserInfoQuery();
  const isLoggedIn = !!userInfoRes?.data;

  // ── Source context state ──
  const [activeSourceId, setActiveSourceId]   = useState<string | null>(null);
  const [activeTableName, setActiveTableName] = useState(DEFAULT_TABLE);
  const [tableNameInput, setTableNameInput]   = useState(DEFAULT_TABLE);
  const [managerOpen, setManagerOpen]         = useState(false);

  // Sources list — only fetch when user is authenticated
  const { data: sourcesRes } = useGetSourcesQuery(undefined, { skip: !isLoggedIn });
  const sources = sourcesRes?.data ?? [];

  // If the active source is deleted, revert to default
  useEffect(() => {
    if (activeSourceId && sources.length > 0 && !sources.some((s) => s.id === activeSourceId)) {
      setActiveSourceId(null);
      setTableNameInput(DEFAULT_TABLE);
      setActiveTableName(DEFAULT_TABLE);
    }
  }, [sources, activeSourceId]);

  // Computed context for GET params
  const ctxSourceId   = activeSourceId   ?? undefined;
  const ctxTableName  = activeTableName  || undefined;

  // ── Schema ──
  const { data: schemaData, isLoading: schemaLoading, error: schemaError } =
    useGetSchemaQuery({ sourceId: ctxSourceId, tableName: ctxTableName });
  const fields: FieldDefinition[] = schemaData?.data?.fields ?? [];

  // ── Summarize ──
  const [tab, setTab] = useState(0);
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
  const [dateField, setDateField] = useState<string>('OrderDate');

  // Auto-detect: when schema loads, switch to the first date-typed field if the
  // current dateField doesn't exist in the new schema.
  useEffect(() => {
    if (fields.length === 0) return;
    const exists = fields.some((f) => f.name === dateField);
    if (!exists) {
      const firstDate = fields.find((f) => DATE_CANONICAL_TYPES.includes(f.canonicalType))?.name;
      setDateField(firstDate ?? fields[0].name);
    }
  }, [fields]); // eslint-disable-line react-hooks/exhaustive-deps

  const { data: maxDateData, isLoading: maxDateLoading } =
    useGetMaxDateQuery({ sourceId: ctxSourceId, tableName: ctxTableName, dateField });

  // Auto-load details when Details tab is active, page, filters, or source changes
  useEffect(() => {
    if (tab === 2) {
      details({
        context:  ctxSourceId ? { sourceId: ctxSourceId, tableName: ctxTableName } : undefined,
        orderBy:  dateField ? [{ fieldName: dateField, descending: true }] : [],
        skip:     page * ROWS_PER_PAGE,
        take:     ROWS_PER_PAGE,
        filters:  toFilterDtos(detailsFilters),
      });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tab, page, detailsFilters, activeSourceId, activeTableName, dateField]);

  // ── Source selector handlers ──

  const handleSourceChange = (id: string) => {
    const newId = id === '' ? null : id;
    setActiveSourceId(newId);
    if (newId === null) {
      setTableNameInput(DEFAULT_TABLE);
      setActiveTableName(DEFAULT_TABLE);
    } else {
      const src = sources.find((s) => s.id === newId);
      const tbl = src?.defaultTableName ?? '';
      setTableNameInput(tbl);
      setActiveTableName(tbl);
    }
    // Reset query config so stale field names don't confuse the new source
    setViewByFields([]);
    setMeasures([{ fieldName: '', aggregation: 'Sum' }]);
    setSummarizeFilters([]);
    setDetailsFilters([]);
    setPage(0);
    setDateField('OrderDate'); // will be overridden by auto-detect once schema loads
  };

  const applyTableName = () => setActiveTableName(tableNameInput);

  // ── Summarize handlers ──

  const handleRunSummarize = () => {
    if (viewByFields.length === 0 || measures.length === 0) return;
    summarize({
      context:  ctxSourceId ? { sourceId: ctxSourceId, tableName: ctxTableName } : undefined,
      viewBy:   viewByFields,
      measures,
      filters:  toFilterDtos(summarizeFilters),
    });
  };

  const addMeasure = () =>
    setMeasures((prev) => [...prev, { fieldName: fields[0]?.name ?? '', aggregation: 'Sum' }]);

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

  const summarizeRows    = summarizeData?.data?.rows ?? [];
  const summarizeHeaders = summarizeRows.length > 0 ? Object.keys(summarizeRows[0]) : [];

  const detailsRows      = detailsData?.data?.rows ?? [];
  const detailsHeaders   = detailsRows.length > 0 ? Object.keys(detailsRows[0]) : [];
  const detailsTotalCount = detailsData?.data?.totalCount ?? 0;

  const maxDate          = maxDateData?.data?.maxDate;
  const activeSource     = sources.find((s) => s.id === activeSourceId);

  // ── Render ────────────────────────────────────────────────────────────────

  return (
    <Box>
      <Typography variant="h5" gutterBottom>
        DataAnalysis Library — Live Demo
      </Typography>

      {/* ── Source Selector Panel ──────────────────────────────────────────── */}
      <Paper variant="outlined" sx={{ p: 2, mb: 2 }}>
        <Typography variant="subtitle2" gutterBottom sx={{ color: 'text.secondary' }}>
          Data Source
        </Typography>

        {isLoggedIn ? (
          /* ── Logged-in: full source selector ── */
          <Box sx={{ display: 'flex', gap: 1.5, alignItems: 'flex-start', flexWrap: 'wrap' }}>
            {/* Source dropdown */}
            <FormControl size="small" sx={{ minWidth: 260 }}>
              <InputLabel>Source</InputLabel>
              <Select
                value={activeSourceId ?? ''}
                label="Source"
                onChange={(e) => handleSourceChange(e.target.value as string)}
              >
                <MenuItem value="">
                  <em>Default (JackyAI SQL / dbo.Orders)</em>
                </MenuItem>
                {sources.map((s) => (
                  <MenuItem key={s.id} value={s.id}>
                    {s.name}&nbsp;
                    <Chip
                      label={s.connectorType}
                      size="small"
                      color={s.connectorType === 'Adx' ? 'secondary' : 'primary'}
                      sx={{ ml: 0.5 }}
                    />
                  </MenuItem>
                ))}
              </Select>
            </FormControl>

            {/* Table name input */}
            <TextField
              size="small"
              label="Table"
              value={tableNameInput}
              onChange={(e) => setTableNameInput(e.target.value)}
              onBlur={applyTableName}
              onKeyDown={(e) => { if (e.key === 'Enter') applyTableName(); }}
              sx={{ width: 220 }}
              helperText="Press Enter or click away to apply"
            />

            {/* Manage Sources button */}
            <Button
              size="small"
              variant="outlined"
              startIcon={<SettingsIcon />}
              onClick={() => setManagerOpen(true)}
              sx={{ mt: 0.25 }}
            >
              Manage Sources
            </Button>
          </Box>
        ) : (
          /* ── Not logged in: default-only, login prompt ── */
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, flexWrap: 'wrap' }}>
            <Chip label="Default (JackyAI SQL / dbo.Orders)" color="default" size="small" />
            <Typography variant="body2" color="text.secondary">
              —{' '}
              <a
                href={`/api/account/login/Google?ReturnUrl=${encodeURIComponent(window.location.pathname)}`}
                style={{ color: 'inherit' }}
              >
                Sign in
              </a>
              {' '}to configure and use custom data sources.
            </Typography>
          </Box>
        )}
      </Paper>

      {/* ── Warning when a custom (non-default) source is active ─────────── */}
      {activeSourceId && (
        <Alert severity="info" icon={<WarningAmberIcon />} sx={{ mb: 2 }}>
          <strong>Custom data source selected</strong> — querying{' '}
          <em>{activeSource?.name ?? activeSourceId}</em>.
          {' '}Credentials are encrypted and stored securely, and transmitted over HTTPS.
          Note: if this server is not reachable from the public internet, connections to external databases may fail.
        </Alert>
      )}

      {/* ── Description ────────────────────────────────────────────────────── */}
      <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
        {activeSourceId
          ? <>
              Querying <strong>{activeSource?.name}</strong>
              {activeTableName && (
                <>
                  {' → '}
                  <Box component="code" sx={{ bgcolor: 'action.selected', px: 0.5, borderRadius: '3px', fontFamily: 'monospace', fontSize: '0.875em' }}>
                    {activeTableName}
                  </Box>
                </>
              )}
            </>
          : <>
              All 4 query types demonstrated against{' '}
              <Box component="code" sx={{ bgcolor: 'action.selected', px: 0.5, borderRadius: '3px', fontFamily: 'monospace', fontSize: '0.875em' }}>
                {activeTableName || DEFAULT_TABLE}
              </Box>
            </>
        }
      </Typography>

      {/* ── Tabs ──────────────────────────────────────────────────────────── */}
      <Box sx={{ borderBottom: 1, borderColor: 'divider' }}>
        <Tabs value={tab} onChange={(_, v: number) => setTab(v)}>
          <Tab label="① Schema" />
          <Tab label="② Summarize" />
          <Tab label="③ Details" />
          <Tab label="④ Max Date" />
        </Tabs>
      </Box>

      {/* ── Tab 1: Schema ──────────────────────────────────────────────────── */}
      <TabPanel value={tab} index={0}>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
          <strong>FieldListAsync</strong> — returns the schema of the target table.
        </Typography>

        {schemaLoading && <CircularProgress size={24} />}
        {schemaError && <Alert severity="error">Failed to load schema.</Alert>}

        {fields.length > 0 && (
          <>
            {/* Date field selector — global setting used by MaxDate & Details */}
            <Paper variant="outlined" sx={{ p: 2, mb: 2, display: 'flex', alignItems: 'center', gap: 2, flexWrap: 'wrap' }}>
              <CalendarTodayIcon fontSize="small" color="action" />
              <FormControl size="small" sx={{ minWidth: 220 }}>
                <InputLabel>Date Field</InputLabel>
                <Select
                  value={fields.some((f) => f.name === dateField) ? dateField : ''}
                  label="Date Field"
                  onChange={(e) => setDateField(e.target.value as string)}
                >
                  {fields
                    .filter((f) => DATE_CANONICAL_TYPES.includes(f.canonicalType))
                    .map((f) => (
                      <MenuItem key={f.name} value={f.name}>{f.name}</MenuItem>
                    ))}
                  {/* Fallback: show all fields if no date fields found */}
                  {fields.filter((f) => DATE_CANONICAL_TYPES.includes(f.canonicalType)).length === 0 &&
                    fields.map((f) => (
                      <MenuItem key={f.name} value={f.name}>{f.name}</MenuItem>
                    ))
                  }
                </Select>
              </FormControl>
              <Typography variant="caption" color="text.secondary">
                Auto-detected from schema — used by <strong>Max Date</strong> and <strong>Details</strong> (default sort order).
              </Typography>
            </Paper>

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
          </>
        )}
      </TabPanel>

      {/* ── Tab 2: Summarize ───────────────────────────────────────────────── */}
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
            sourceId={ctxSourceId}
            tableName={ctxTableName}
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

      {/* ── Tab 3: Details ─────────────────────────────────────────────────── */}
      <TabPanel value={tab} index={2}>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
          <strong>DetailsAsync</strong> — paginated row-level data. Filters apply immediately.
        </Typography>

        {/* Filters panel */}
        <Paper variant="outlined" sx={{ p: 2, mb: 2 }}>
          <FilterEditor
            filters={detailsFilters}
            fields={fields}
            onChange={handleDetailsFiltersChange}
            sourceId={ctxSourceId}
            tableName={ctxTableName}
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

      {/* ── Tab 4: Max Date ────────────────────────────────────────────────── */}
      <TabPanel value={tab} index={3}>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
          <strong>MaxDateAsync</strong> — MAX of a date field from the target table.
        </Typography>

        {maxDateLoading && <CircularProgress size={24} />}

        <Box sx={{ display: 'flex', justifyContent: 'center' }}>
          <Card variant="outlined" sx={{ minWidth: 320, textAlign: 'center' }}>
            <CardContent sx={{ py: 4 }}>
              <CalendarTodayIcon sx={{ fontSize: 56, color: 'primary.main', mb: 2 }} />
              <Typography variant="subtitle1" color="text.secondary" gutterBottom>
                MAX(<code>{dateField}</code>) in{' '}
                <code>{activeTableName || DEFAULT_TABLE}</code>
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
              <Typography variant="caption" color="text.secondary" sx={{ mt: 2, display: 'block' }}>
                To change the date field, go to the <strong>① Schema</strong> tab.
              </Typography>
            </CardContent>
          </Card>
        </Box>
      </TabPanel>

      {/* ── Source Manager Dialog ──────────────────────────────────────────── */}
      <SourceManagerDialog open={managerOpen} onClose={() => setManagerOpen(false)} />
    </Box>
  );
}

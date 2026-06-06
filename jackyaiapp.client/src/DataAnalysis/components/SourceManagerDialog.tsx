import AddIcon from '@mui/icons-material/Add';
import DeleteIcon from '@mui/icons-material/Delete';
import EditIcon from '@mui/icons-material/Edit';
import SettingsIcon from '@mui/icons-material/Settings';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
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
import Select from '@mui/material/Select';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableContainer from '@mui/material/TableContainer';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { useEffect, useState } from 'react';

import {
  useCreateSourceMutation,
  useDeleteSourceMutation,
  useGetSourcesQuery,
  useUpdateSourceMutation,
} from '../../apis/dataAnalysisApis';

import type {
  AdxSourceConfig,
  DataSourceConfigDto,
  DataSourceConnectorType,
  MsSqlSourceConfig,
  SaveDataSourceConfigDto,
} from '../../apis/dataAnalysisApis/types';

interface SourceManagerDialogProps {
  open: boolean;
  onClose: () => void;
}

export function SourceManagerDialog({ open, onClose }: SourceManagerDialogProps) {
  const { data: sourcesRes, isLoading } = useGetSourcesQuery(undefined, { skip: !open });
  const [createSource, { isLoading: creating }] = useCreateSourceMutation();
  const [updateSource, { isLoading: updating }] = useUpdateSourceMutation();
  const [deleteSource] = useDeleteSourceMutation();

  const [view, setView] = useState<'list' | 'form'>('list');
  const [editTarget, setEditTarget] = useState<DataSourceConfigDto | null>(null);

  // Form state
  const [formName, setFormName] = useState('');
  const [formType, setFormType] = useState<DataSourceConnectorType>('MsSql');
  const [formDefaultTable, setFormDefaultTable] = useState('');
  const [formConnStr, setFormConnStr] = useState('');
  const [formClusterUri, setFormClusterUri] = useState('');
  const [formDatabase, setFormDatabase] = useState('');
  const [formTenantId, setFormTenantId] = useState('');
  const [formClientId, setFormClientId] = useState('');
  const [formClientSecret, setFormClientSecret] = useState('');
  const [saveError, setSaveError] = useState<string | null>(null);

  const sources = sourcesRes?.data ?? [];
  const saving = creating || updating;

  useEffect(() => {
    if (view !== 'form') return;
    setSaveError(null);
    if (editTarget) {
      setFormName(editTarget.name);
      setFormType(editTarget.connectorType);
      setFormDefaultTable(editTarget.defaultTableName ?? '');
      if (editTarget.connectorType === 'MsSql') {
        const c = editTarget.config as unknown as MsSqlSourceConfig;
        setFormConnStr(c.connectionString ?? '');
      } else {
        const c = editTarget.config as unknown as AdxSourceConfig;
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

  const openNewForm = () => {
    setEditTarget(null);
    setView('form');
  };
  const openEditForm = (s: DataSourceConfigDto) => {
    setEditTarget(s);
    setView('form');
  };

  const handleDelete = async (id: string) => {
    if (!window.confirm('Delete this data source? This action cannot be undone.')) return;
    try {
      await deleteSource(id).unwrap();
    } catch {
      /* RTK Query logs it */
    }
  };

  const handleSave = async () => {
    setSaveError(null);
    const config: MsSqlSourceConfig | AdxSourceConfig =
      formType === 'MsSql'
        ? { connectionString: formConnStr }
        : {
            clusterUri: formClusterUri,
            database: formDatabase,
            tenantId: formTenantId || null,
            clientId: formClientId || null,
            clientSecret: formClientSecret || null,
            connectionString: null,
          };

    const dto: SaveDataSourceConfigDto = {
      name: formName,
      connectorType: formType,
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

  const handleClose = () => {
    setView('list');
    setEditTarget(null);
    onClose();
  };

  const canSave =
    !!formName && (formType === 'MsSql' ? !!formConnStr : !!formClusterUri && !!formDatabase);

  return (
    <Dialog open={open} onClose={handleClose} maxWidth="md" fullWidth>
      <DialogTitle sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
        <SettingsIcon fontSize="small" />
        {view === 'list'
          ? 'Manage Data Sources'
          : editTarget
            ? `Edit Source — ${editTarget.name}`
            : 'New Data Source'}
      </DialogTitle>
      <DialogContent dividers sx={{ minHeight: 320 }}>
        {/* List view */}
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
                            size="small"
                            color="error"
                            onClick={() => handleDelete(s.id)}
                            title="Delete"
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

        {/* Form view */}
        {view === 'form' && (
          <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2, pt: 1 }}>
            <TextField
              label="Source Name"
              size="small"
              fullWidth
              required
              value={formName}
              onChange={(e) => setFormName(e.target.value)}
              placeholder="e.g. My Production DB"
            />

            <FormControl size="small" fullWidth>
              <InputLabel>Connector Type</InputLabel>
              <Select
                value={formType}
                label="Connector Type"
                onChange={(e) => setFormType(e.target.value as DataSourceConnectorType)}
                disabled={!!editTarget}
              >
                <MenuItem value="MsSql">SQL Server (MsSql)</MenuItem>
                <MenuItem value="Adx">Azure Data Explorer (ADX / Kusto)</MenuItem>
              </Select>
            </FormControl>

            {formType === 'MsSql' && (
              <TextField
                label="Connection String"
                size="small"
                fullWidth
                required
                value={formConnStr}
                onChange={(e) => setFormConnStr(e.target.value)}
                type="password"
                placeholder="Server=myserver;Database=mydb;User Id=sa;Password=..."
                helperText={
                  editTarget
                    ? "Leave as 'Password=***' to keep the existing password unchanged."
                    : 'Full ADO.NET connection string including credentials.'
                }
                slotProps={{
                  htmlInput: { autoComplete: 'new-password' },
                }}
              />
            )}

            {formType === 'Adx' && (
              <>
                <TextField
                  label="Cluster URI"
                  size="small"
                  fullWidth
                  required
                  value={formClusterUri}
                  onChange={(e) => setFormClusterUri(e.target.value)}
                  placeholder="https://mycluster.eastus.kusto.windows.net"
                />
                <TextField
                  label="Database"
                  size="small"
                  fullWidth
                  required
                  value={formDatabase}
                  onChange={(e) => setFormDatabase(e.target.value)}
                />
                <Divider>
                  <Typography variant="caption" color="text.secondary">
                    Service Principal (optional)
                  </Typography>
                </Divider>
                <TextField
                  label="Tenant ID"
                  size="small"
                  fullWidth
                  value={formTenantId}
                  onChange={(e) => setFormTenantId(e.target.value)}
                />
                <TextField
                  label="Client ID / App ID"
                  size="small"
                  fullWidth
                  value={formClientId}
                  onChange={(e) => setFormClientId(e.target.value)}
                />
                <TextField
                  label="Client Secret"
                  size="small"
                  fullWidth
                  type="password"
                  value={formClientSecret}
                  onChange={(e) => setFormClientSecret(e.target.value)}
                  helperText={
                    editTarget ? "Leave as '***' to keep the existing secret." : undefined
                  }
                  slotProps={{
                    htmlInput: { autoComplete: 'new-password' },
                  }}
                />
              </>
            )}

            <Divider />

            <TextField
              label="Default Table Name (optional)"
              size="small"
              fullWidth
              value={formDefaultTable}
              onChange={(e) => setFormDefaultTable(e.target.value)}
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
            <Button variant="contained" disabled={saving || !canSave} onClick={handleSave}>
              {saving ? 'Saving…' : 'Save'}
            </Button>
          </>
        )}
      </DialogActions>
    </Dialog>
  );
}

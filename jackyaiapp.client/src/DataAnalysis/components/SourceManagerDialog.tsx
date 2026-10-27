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
import { useTheme } from '@mui/material/styles';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableContainer from '@mui/material/TableContainer';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import useMediaQuery from '@mui/material/useMediaQuery';
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

import { getCaughtErrorMessage } from '@/apis/utils/errorMessage';
import { ActionTooltip } from '@/components/hints';

const SAVE_FAILED_FALLBACK = '儲存失敗 — 請檢查連線資訊後再試一次。';

interface SourceManagerDialogProps {
  open: boolean;
  onClose: () => void;
}

export function SourceManagerDialog({ open, onClose }: SourceManagerDialogProps) {
  const theme = useTheme();
  const fullScreen = useMediaQuery(theme.breakpoints.down('sm'));
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
    if (!window.confirm('刪除這個資料來源？此操作無法復原。')) return;
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
    } catch (error) {
      // The server says why it refused (e.g. an auth mode it will not run as); only fall back to
      // the generic line when it said nothing usable.
      setSaveError(getCaughtErrorMessage(error, SAVE_FAILED_FALLBACK));
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
    <Dialog open={open} onClose={handleClose} maxWidth="md" fullWidth fullScreen={fullScreen}>
      <DialogTitle sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
        <SettingsIcon fontSize="small" />
        {view === 'list'
          ? '管理資料來源'
          : editTarget
            ? `編輯來源 — ${editTarget.name}`
            : '新增資料來源'}
      </DialogTitle>
      <DialogContent dividers sx={{ minHeight: 320 }}>
        {/* List view */}
        {view === 'list' && (
          <>
            {isLoading && <CircularProgress size={24} />}
            {!isLoading && sources.length === 0 && (
              <Typography color="text.secondary" sx={{ mt: 1 }}>
                尚無已儲存的資料來源。點擊 <strong>新增來源</strong> 開始使用。
              </Typography>
            )}
            {sources.length > 0 && (
              <TableContainer>
                <Table size="small">
                  <TableHead>
                    <TableRow>
                      <TableCell>名稱</TableCell>
                      <TableCell>類型</TableCell>
                      <TableCell>預設資料表</TableCell>
                      <TableCell>建立時間</TableCell>
                      <TableCell align="right">操作</TableCell>
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
                          <ActionTooltip title="編輯">
                            <IconButton size="small" onClick={() => openEditForm(s)}>
                              <EditIcon fontSize="small" />
                            </IconButton>
                          </ActionTooltip>
                          <ActionTooltip title="刪除">
                            <IconButton
                              size="small"
                              color="error"
                              onClick={() => handleDelete(s.id)}
                            >
                              <DeleteIcon fontSize="small" />
                            </IconButton>
                          </ActionTooltip>
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
              label="來源名稱"
              size="small"
              fullWidth
              required
              value={formName}
              onChange={(e) => setFormName(e.target.value)}
              placeholder="例如：My Production DB"
            />

            <FormControl size="small" fullWidth>
              <InputLabel>連接器類型</InputLabel>
              <Select
                value={formType}
                label="連接器類型"
                onChange={(e) => setFormType(e.target.value as DataSourceConnectorType)}
                disabled={!!editTarget}
              >
                <MenuItem value="MsSql">SQL Server (MsSql)</MenuItem>
                <MenuItem value="Adx">Azure Data Explorer (ADX / Kusto)</MenuItem>
              </Select>
            </FormControl>

            {formType === 'MsSql' && (
              <TextField
                label="連線字串 (Connection String)"
                size="small"
                fullWidth
                required
                value={formConnStr}
                onChange={(e) => setFormConnStr(e.target.value)}
                type="password"
                placeholder="Server=tcp:myserver.database.windows.net,1433;Database=mydb;User Id=sa;Password=..."
                helperText={
                  editTarget
                    ? "保留 'Password=***' 以維持原密碼不變。"
                    : '完整的 ADO.NET 連線字串，含帳密資訊。'
                }
                slotProps={{
                  htmlInput: { autoComplete: 'new-password' },
                }}
              />
            )}

            {formType === 'Adx' && (
              <>
                <TextField
                  label="叢集 URI (Cluster URI)"
                  size="small"
                  fullWidth
                  required
                  value={formClusterUri}
                  onChange={(e) => setFormClusterUri(e.target.value)}
                  placeholder="https://mycluster.eastus.kusto.windows.net"
                />
                <TextField
                  label="資料庫"
                  size="small"
                  fullWidth
                  required
                  value={formDatabase}
                  onChange={(e) => setFormDatabase(e.target.value)}
                />
                <Divider>
                  <Typography variant="caption" color="text.secondary">
                    服務主體 (Service Principal，選填)
                  </Typography>
                </Divider>
                <TextField
                  label="租戶 ID (Tenant ID)"
                  size="small"
                  fullWidth
                  value={formTenantId}
                  onChange={(e) => setFormTenantId(e.target.value)}
                />
                <TextField
                  label="用戶端 ID / App ID"
                  size="small"
                  fullWidth
                  value={formClientId}
                  onChange={(e) => setFormClientId(e.target.value)}
                />
                <TextField
                  label="用戶端密碼 (Client Secret)"
                  size="small"
                  fullWidth
                  type="password"
                  value={formClientSecret}
                  onChange={(e) => setFormClientSecret(e.target.value)}
                  helperText={editTarget ? "保留 '***' 以維持原密碼不變。" : undefined}
                  slotProps={{
                    htmlInput: { autoComplete: 'new-password' },
                  }}
                />
              </>
            )}

            <Divider />

            <TextField
              label="預設資料表名稱（選填）"
              size="small"
              fullWidth
              value={formDefaultTable}
              onChange={(e) => setFormDefaultTable(e.target.value)}
              placeholder="例如：dbo.Orders 或 mydb.myTable"
              helperText="選擇此來源時會自動帶入「資料表」欄位。"
            />

            {saveError && <Alert severity="error">{saveError}</Alert>}
          </Box>
        )}
      </DialogContent>
      <DialogActions>
        {view === 'list' ? (
          <>
            <Button onClick={openNewForm} startIcon={<AddIcon />} variant="outlined">
              新增來源
            </Button>
            <Box sx={{ flex: 1 }} />
            <Button onClick={handleClose}>關閉</Button>
          </>
        ) : (
          <>
            <Button onClick={() => setView('list')}>取消</Button>
            <Box sx={{ flex: 1 }} />
            <Button variant="contained" disabled={saving || !canSave} onClick={handleSave}>
              {saving ? '儲存中…' : '儲存'}
            </Button>
          </>
        )}
      </DialogActions>
    </Dialog>
  );
}

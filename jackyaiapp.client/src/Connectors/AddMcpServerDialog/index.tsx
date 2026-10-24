import {
  Alert,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  MenuItem,
  Stack,
  Step,
  StepLabel,
  Stepper,
  TextField,
  Typography,
} from '@mui/material';
import { useState } from 'react';

import { EFFECT_LABELS } from '../toolPermission';
import ToolPermissionToggle from '../ToolPermissionToggle';

import {
  AddMcpStep,
  createRequest,
  credentialsProblem,
  CredentialsForm,
  EMPTY_CREDENTIALS,
  probeSummary,
  suggestedName,
  urlProblem,
} from './addMcpServer';

import type {
  CatalogEntry,
  InstalledConnector,
  McpProbeResult,
  ToolPermission,
} from '@/apis/connectorsApis/types';

import {
  useCreateInstallationMutation,
  useProbeMcpServerMutation,
  useSetToolPermissionMutation,
} from '@/apis/connectorsApis/connectorsApis';
import { getCaughtErrorMessage } from '@/apis/utils/errorMessage';

interface AddMcpServerDialogProps {
  open: boolean;
  /** A catalog entry that needs an API key: the dialog starts at the credentials, for it. */
  preset: CatalogEntry | null;
  onClose: () => void;
  /** An OAuth server was added; the page sends the reader to sign in. */
  onAuthorize: (connector: InstalledConnector) => void;
}

const STEPS: { key: AddMcpStep; label: string }[] = [
  { key: 'locate', label: '網址' },
  { key: 'credentials', label: '登入' },
  { key: 'done', label: '完成' },
];

/** A catalog entry asks only for its credentials: the server already knows where it is. */
const presetProbe = (entry: CatalogEntry): McpProbeResult => ({
  serverUrl: '',
  auth: entry.authKind === 'oauth' ? 'oauth' : entry.authKind === 'none' ? 'none' : 'apiKey',
  authorizationServer: null,
  registration: null,
  serverName: entry.displayName,
});

/**
 * Adding an MCP server in three steps (docs/ai-native/design/05 §3): the URL is checked by the
 * server (what sign-in it wants, and that it is not a private address), then the credentials, then
 * the tools it offers with their permissions, which can be changed right there.
 */
export default function AddMcpServerDialog(props: AddMcpServerDialogProps) {
  return (
    <Dialog open={props.open} onClose={props.onClose} fullWidth maxWidth="sm">
      {/* Mounted only while open, so every opening starts from an empty form. */}
      {props.open && <AddMcpServerForm {...props} />}
    </Dialog>
  );
}

function AddMcpServerForm({ preset, onClose, onAuthorize }: AddMcpServerDialogProps) {
  const [step, setStep] = useState<AddMcpStep>(preset ? 'credentials' : 'locate');
  const [url, setUrl] = useState('');
  const [urlTouched, setUrlTouched] = useState(false);
  const [probe, setProbe] = useState<McpProbeResult | null>(preset ? presetProbe(preset) : null);
  const [name, setName] = useState(preset?.displayName ?? '');
  const [form, setForm] = useState<CredentialsForm>(EMPTY_CREDENTIALS);
  const [added, setAdded] = useState<InstalledConnector | null>(null);
  const [error, setError] = useState<string | null>(null);

  const [probeServer, { isLoading: probing }] = useProbeMcpServerMutation();
  const [createInstallation, { isLoading: creating }] = useCreateInstallationMutation();
  const [setToolPermission, { isLoading: saving }] = useSetToolPermissionMutation();

  const urlError = urlProblem(url);
  const credentialsError = probe ? credentialsProblem(probe, name, form) : null;

  const detect = async () => {
    setUrlTouched(true);
    if (urlError) return;
    setError(null);
    try {
      const found = await probeServer({ serverUrl: url.trim() }).unwrap();
      setProbe(found);
      setName(suggestedName(found));
      setStep('credentials');
    } catch (e) {
      setError(getCaughtErrorMessage(e));
    }
  };

  const add = async () => {
    if (!probe || credentialsError) return;
    setError(null);
    const request = createRequest(probe, name, form);
    try {
      const connector = await createInstallation(
        preset ? { ...request, definitionKey: preset.key, serverUrl: undefined } : request,
      ).unwrap();
      if (request.authKind === 'oauth') {
        onAuthorize(connector);
        return;
      }
      setAdded(connector);
      setStep('done');
    } catch (e) {
      setError(getCaughtErrorMessage(e));
    }
  };

  const changePermission = async (toolName: string, permission: ToolPermission) => {
    if (!added) return;
    try {
      setAdded(
        await setToolPermission({
          definitionKey: added.definitionKey,
          installationId: added.id,
          toolName,
          permission,
        }).unwrap(),
      );
    } catch (e) {
      setError(getCaughtErrorMessage(e));
    }
  };

  const setField = (patch: Partial<CredentialsForm>) => setForm((f) => ({ ...f, ...patch }));

  return (
    <>
      <DialogTitle>{preset ? `連線 ${preset.displayName}` : '新增 MCP server'}</DialogTitle>
      <DialogContent>
        <Stepper activeStep={STEPS.findIndex((s) => s.key === step)} sx={{ mb: 3, mt: 1 }}>
          {STEPS.map((s) => (
            <Step key={s.key}>
              <StepLabel>{s.label}</StepLabel>
            </Step>
          ))}
        </Stepper>

        {error && (
          <Alert severity="error" sx={{ mb: 2 }}>
            {error}
          </Alert>
        )}

        {step === 'locate' && (
          <Stack spacing={2}>
            <Typography variant="body2" color="text.secondary">
              貼上 remote MCP server 的網址（Streamable
              HTTP）。我們會先檢查它要怎麼登入，還不會儲存任何東西。
            </Typography>
            <TextField
              label="MCP server 網址"
              placeholder="https://mcp.example.com/mcp"
              value={url}
              autoFocus
              onChange={(e) => setUrl(e.target.value)}
              onBlur={() => setUrlTouched(true)}
              onKeyDown={(e) => e.key === 'Enter' && detect()}
              error={urlTouched && urlError !== null}
              helperText={urlTouched && urlError ? urlError : ' '}
              fullWidth
            />
          </Stack>
        )}

        {step === 'credentials' && probe && (
          <Stack spacing={2}>
            {!preset && <Alert severity="success">{probeSummary(probe)}</Alert>}
            <TextField
              label="名稱"
              value={name}
              onChange={(e) => setName(e.target.value)}
              helperText="AI 對話裡會用這個名字稱呼它"
              fullWidth
            />
            {probe.auth === 'apiKey' && (
              <>
                <TextField
                  select
                  label="認證方式"
                  value={form.kind}
                  onChange={(e) => setField({ kind: e.target.value as CredentialsForm['kind'] })}
                >
                  <MenuItem value="bearer">Bearer token（Authorization header）</MenuItem>
                  <MenuItem value="apiKey">API key（自訂 header）</MenuItem>
                </TextField>
                {form.kind === 'apiKey' && (
                  <TextField
                    label="Header 名稱"
                    value={form.headerName}
                    onChange={(e) => setField({ headerName: e.target.value })}
                  />
                )}
                <TextField
                  label={form.kind === 'bearer' ? 'Token' : 'API key'}
                  type="password"
                  autoComplete="off"
                  value={form.value}
                  onChange={(e) => setField({ value: e.target.value })}
                  helperText="加密後儲存，只用來呼叫這個 server，不會再顯示"
                />
              </>
            )}
            {probe.auth === 'oauth' && probe.registration === 'manual' && (
              <>
                <Typography variant="body2" color="text.secondary">
                  這個授權伺服器不接受自動註冊，請先在它那邊建立 OAuth app，再填入：
                </Typography>
                <TextField
                  label="Client ID"
                  value={form.clientId}
                  onChange={(e) => setField({ clientId: e.target.value })}
                />
                <TextField
                  label="Client secret（選填）"
                  type="password"
                  autoComplete="off"
                  value={form.clientSecret}
                  onChange={(e) => setField({ clientSecret: e.target.value })}
                />
              </>
            )}
            {credentialsError && (
              <Typography variant="caption" color="text.secondary">
                {credentialsError}
              </Typography>
            )}
          </Stack>
        )}

        {step === 'done' && added && (
          <Stack spacing={2}>
            <Alert severity="success">
              {added.displayName} 已連線，有 {added.tools.length} 個工具。AI 助理現在就能使用它們。
            </Alert>
            <Typography variant="body2" color="text.secondary">
              讀取類的工具預設自動使用，其他的每次都會先問你。可以在這裡或之後在詳情頁調整。
            </Typography>
            {added.tools.map((t) => (
              <Stack key={t.name} direction="row" spacing={1} sx={{ alignItems: 'center' }}>
                <Stack sx={{ flex: 1, minWidth: 0 }}>
                  <Typography variant="body2" sx={{ fontWeight: 600 }}>
                    {t.title ?? t.name}
                  </Typography>
                  <Typography variant="caption" color="text.secondary" noWrap>
                    {EFFECT_LABELS[t.effect]}・{t.description}
                  </Typography>
                </Stack>
                <ToolPermissionToggle
                  toolName={t.title ?? t.name}
                  effect={t.effect}
                  value={t.permission}
                  disabled={saving}
                  onChange={(p) => changePermission(t.name, p)}
                />
              </Stack>
            ))}
          </Stack>
        )}
      </DialogContent>
      <DialogActions>
        {step === 'locate' && (
          <>
            <Button onClick={onClose}>取消</Button>
            <Button variant="contained" onClick={detect} disabled={probing}>
              {probing ? '檢查中…' : '檢查'}
            </Button>
          </>
        )}
        {step === 'credentials' && (
          <>
            {!preset && <Button onClick={() => setStep('locate')}>上一步</Button>}
            <Button onClick={onClose}>取消</Button>
            <Button
              variant="contained"
              onClick={add}
              disabled={creating || credentialsError !== null}
            >
              {probe?.auth === 'oauth' ? '前往授權' : creating ? '連線中…' : '連線'}
            </Button>
          </>
        )}
        {step === 'done' && (
          <Button variant="contained" onClick={onClose}>
            完成
          </Button>
        )}
      </DialogActions>
    </>
  );
}

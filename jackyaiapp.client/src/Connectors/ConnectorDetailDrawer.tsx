import { Close } from '@mui/icons-material';
import {
  Alert,
  Box,
  Button,
  Chip,
  Drawer,
  IconButton,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  Typography,
} from '@mui/material';
import { useState } from 'react';

import CategoryIcon from './CategoryIcon';
import { HEALTH_VIEW } from './connectorView';
import { EFFECT_LABELS } from './toolPermission';
import ToolPermissionToggle from './ToolPermissionToggle';

import type { InstalledConnector, ToolPermission } from '@/apis/connectorsApis/types';

import { ActionTooltip } from '@/components/hints';

const AUTH_LABELS: Record<InstalledConnector['authKind'], string> = {
  none: '不需要登入',
  apiKey: 'API key',
  bearer: 'Bearer token',
  oauth: 'OAuth',
  existingProvider: '帳號連線',
};

export interface ConnectorDetailActions {
  onTest: () => void;
  onSetEnabled: (enabled: boolean) => void;
  onRemove: () => void;
  onReauthorize: () => void;
  onPermission: (toolName: string, permission: ToolPermission) => void;
}

interface ConnectorDetailDrawerProps extends ConnectorDetailActions {
  connector: InstalledConnector | null;
  onClose: () => void;
  /** A request about this connector is on its way; its buttons wait. */
  busy: boolean;
}

/** One connector up close: where it is, how it signs in, and what the AI may do with each tool. */
export default function ConnectorDetailDrawer({
  connector,
  onClose,
  busy,
  ...actions
}: ConnectorDetailDrawerProps) {
  return (
    <Drawer
      anchor="right"
      open={connector !== null}
      onClose={onClose}
      slotProps={{ paper: { sx: { width: { xs: '100%', sm: 560 } } } }}
    >
      {connector && (
        <DetailBody
          // A different connector starts with the removal question closed.
          key={connector.id ?? connector.definitionKey}
          connector={connector}
          onClose={onClose}
          busy={busy}
          {...actions}
        />
      )}
    </Drawer>
  );
}

function DetailBody({
  connector,
  onClose,
  busy,
  onTest,
  onSetEnabled,
  onRemove,
  onReauthorize,
  onPermission,
}: ConnectorDetailDrawerProps & { connector: InstalledConnector }) {
  const [confirmingRemove, setConfirmingRemove] = useState(false);
  const health = HEALTH_VIEW[connector.health];
  const enabled = connector.health !== 'disabled';
  const canReauthorize = connector.authKind === 'oauth';

  return (
    <Box sx={{ p: 3 }}>
      <Stack direction="row" spacing={1.5} sx={{ alignItems: 'center', mb: 2 }}>
        <CategoryIcon category={connector.category} color="primary" />
        <Typography variant="h6" component="h2" sx={{ flex: 1 }}>
          {connector.displayName}
        </Typography>
        <Chip
          size="small"
          label={connector.alwaysOn ? '內建' : health.label}
          color={health.color}
        />
        <ActionTooltip title="關閉">
          <IconButton onClick={onClose}>
            <Close />
          </IconButton>
        </ActionTooltip>
      </Stack>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
        {connector.description}
      </Typography>

      {connector.lastError && (
        <Alert severity={connector.health === 'needsAuth' ? 'warning' : 'error'} sx={{ mb: 2 }}>
          {connector.lastError}
        </Alert>
      )}

      {!connector.alwaysOn && (
        <Box component="dl" sx={{ m: 0, mb: 2, '& dt': { color: 'text.secondary', mt: 1 } }}>
          {connector.serverUrl && (
            <>
              <Typography component="dt" variant="caption">
                網址
              </Typography>
              <Typography component="dd" variant="body2" sx={{ m: 0, wordBreak: 'break-all' }}>
                {connector.serverUrl}
              </Typography>
            </>
          )}
          <Typography component="dt" variant="caption">
            登入方式
          </Typography>
          <Typography component="dd" variant="body2" sx={{ m: 0 }}>
            {AUTH_LABELS[connector.authKind]}
          </Typography>
          {connector.scopes.length > 0 && (
            <>
              <Typography component="dt" variant="caption">
                授權範圍
              </Typography>
              <Typography component="dd" variant="body2" sx={{ m: 0 }}>
                {connector.scopes.join(' ')}
              </Typography>
            </>
          )}
          {connector.lastTestedAt && (
            <>
              <Typography component="dt" variant="caption">
                最後測試
              </Typography>
              <Typography component="dd" variant="body2" sx={{ m: 0 }}>
                {new Date(connector.lastTestedAt).toLocaleString('zh-TW')}
              </Typography>
            </>
          )}
        </Box>
      )}

      {!connector.alwaysOn && (
        <Stack direction="row" spacing={1} sx={{ mb: 3, flexWrap: 'wrap', rowGap: 1 }}>
          <Button variant="outlined" onClick={onTest} disabled={busy || !enabled}>
            測試連線
          </Button>
          {canReauthorize && (
            <Button variant="outlined" onClick={onReauthorize} disabled={busy}>
              重新授權
            </Button>
          )}
          <Button variant="outlined" onClick={() => onSetEnabled(!enabled)} disabled={busy}>
            {enabled ? '停用' : '啟用'}
          </Button>
          {confirmingRemove ? (
            <>
              <Button variant="contained" color="error" onClick={onRemove} disabled={busy}>
                確定移除
              </Button>
              <Button onClick={() => setConfirmingRemove(false)}>取消</Button>
            </>
          ) : (
            <Button color="error" onClick={() => setConfirmingRemove(true)} disabled={busy}>
              移除
            </Button>
          )}
        </Stack>
      )}
      {!enabled && (
        <Typography variant="caption" color="text.secondary" component="p" sx={{ mt: -2, mb: 2 }}>
          停用中：AI 看不到它的工具，也不能測試連線。
        </Typography>
      )}

      <Typography variant="subtitle1" component="h3" gutterBottom>
        工具（{connector.tools.length}）
      </Typography>
      <Typography variant="caption" color="text.secondary" component="p" sx={{ mb: 1 }}>
        「自動」AI 直接使用；「需確認」每次都先問你；「停用」AI 看不到。無法復原的動作一定會先問你。
      </Typography>
      {connector.tools.length === 0 ? (
        <Typography color="text.secondary">還沒讀到工具，按「測試連線」讀一次。</Typography>
      ) : (
        <Table size="small">
          <TableHead>
            <TableRow>
              <TableCell>工具</TableCell>
              <TableCell>效果</TableCell>
              <TableCell>權限</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {connector.tools.map((t) => (
              <TableRow key={t.name}>
                <TableCell>
                  <Typography variant="body2" sx={{ fontWeight: 600 }}>
                    {t.title ?? t.name}
                  </Typography>
                  <Typography variant="caption" color="text.secondary">
                    {t.description}
                  </Typography>
                </TableCell>
                <TableCell>
                  <Chip
                    size="small"
                    variant="outlined"
                    label={EFFECT_LABELS[t.effect]}
                    color={
                      t.effect === 'read' ? 'default' : t.effect === 'write' ? 'warning' : 'error'
                    }
                  />
                </TableCell>
                <TableCell>
                  <ToolPermissionToggle
                    toolName={t.title ?? t.name}
                    effect={t.effect}
                    value={t.permission}
                    disabled={busy}
                    onChange={(p) => onPermission(t.name, p)}
                  />
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
    </Box>
  );
}

import { Add } from '@mui/icons-material';
import { Alert, Box, Button, Stack, Typography } from '@mui/material';
import { useEffect, useState } from 'react';
import { useSearchParams } from 'react-router-dom';

import AddMcpServerDialog from './AddMcpServerDialog';
import { REGISTRY_PARAM, registrySearchOf } from './AddMcpServerDialog/registryView';
import CatalogSection from './CatalogSection';
import ConnectorDetailDrawer from './ConnectorDetailDrawer';
import {
  CONNECTOR_PARAM,
  connectAction,
  connectorToOpen,
  installedServerUrls,
} from './connectorView';
import InstalledSection from './InstalledSection';

import type { CatalogEntry, InstalledConnector } from '@/apis/connectorsApis/types';

import {
  useAuthorizeInstallationMutation,
  useCreateInstallationMutation,
  useGetConnectorCatalogQuery,
  useRemoveInstallationMutation,
  useSetInstallationEnabledMutation,
  useSetToolPermissionMutation,
  useTestInstallationMutation,
} from '@/apis/connectorsApis/connectorsApis';
import { getApiErrorMessage, getCaughtErrorMessage } from '@/apis/utils/errorMessage';
import CenteredSpinner from '@/components/CenteredSpinner';

interface AiConnectorsSectionProps {
  notify: (message: string, severity: 'success' | 'error') => void;
  /** Goes up each time the page asks for fresh status (its refresh button, a sign-in coming back). */
  refreshSignal?: number;
}

const keyOf = (c: InstalledConnector) => c.id ?? c.definitionKey;

/**
 * What the AI may use (docs/ai-native/design/05 §3): the connectors the reader has, the catalog,
 * and adding an MCP server of their own. The account connections below it are separate and keep
 * working when this part cannot load.
 */
export default function AiConnectorsSection({
  notify,
  refreshSignal = 0,
}: AiConnectorsSectionProps) {
  const { data: catalog, isLoading, error, refetch } = useGetConnectorCatalogQuery();
  useEffect(() => {
    if (refreshSignal > 0) refetch();
  }, [refreshSignal, refetch]);
  const [createInstallation] = useCreateInstallationMutation();
  const [authorizeInstallation] = useAuthorizeInstallationMutation();
  const [testInstallation, { isLoading: testing }] = useTestInstallationMutation();
  const [setEnabled, { isLoading: toggling }] = useSetInstallationEnabledMutation();
  const [removeInstallation, { isLoading: removing }] = useRemoveInstallationMutation();
  const [setToolPermission, { isLoading: saving }] = useSetToolPermissionMutation();

  const [selectedKey, setSelectedKey] = useState<string | null>(null);
  const [dialog, setDialog] = useState<{
    open: boolean;
    preset: CatalogEntry | null;
    browseRegistry?: boolean;
    registrySearch?: string;
  }>({
    open: false,
    preset: null,
  });
  const [busyKey, setBusyKey] = useState<string | null>(null);

  // Sent here to add a server Studio AI found (`?registry=`, D-124): the registry search opens with it, once,
  // and the address drops it so a reload does not open it again. At the limit there is nothing to add to.
  const [searchParams, setSearchParams] = useSearchParams();
  const askedSearch = registrySearchOf(searchParams);
  useEffect(() => {
    if (askedSearch === null || !catalog) return;
    const custom = catalog.installed.filter((c) => c.definitionKey === 'custom').length;
    if (custom < catalog.customLimit)
      setDialog({ open: true, preset: null, browseRegistry: true, registrySearch: askedSearch });
    else
      notify(`自訂 MCP server 最多 ${catalog.customLimit} 個，請先移除用不到的再加入。`, 'error');
    setSearchParams(
      (params) => {
        params.delete(REGISTRY_PARAM);
        return params;
      },
      { replace: true },
    );
  }, [askedSearch, catalog, setSearchParams, notify]);

  // Sent here to reconnect one connector (`?connector=<id>`, a failed call's card, D-223): its details open, once, and
  // the address drops it so a reload does not open them again.
  const asked = catalog ? connectorToOpen(searchParams, catalog.installed) : undefined;
  useEffect(() => {
    if (asked === undefined) return;
    if (asked) setSelectedKey(keyOf(asked));
    else notify('找不到這個 connector，可能已經移除了。', 'error');
    setSearchParams(
      (params) => {
        params.delete(CONNECTOR_PARAM);
        return params;
      },
      { replace: true },
    );
  }, [asked, setSearchParams, notify]);

  // The drawer shows the catalog's copy, so a change saved from it shows at once.
  const selected = catalog?.installed.find((c) => keyOf(c) === selectedKey) ?? null;

  if (isLoading) return <CenteredSpinner sx={{ minHeight: 160 }} />;
  if (error || !catalog) {
    return (
      <Alert severity="info" sx={{ mb: 4 }}>
        AI connectors 目前無法載入（{getApiErrorMessage(error)}）。下方的帳號連線不受影響。
      </Alert>
    );
  }

  const run = async (work: () => Promise<unknown>, done?: string) => {
    try {
      await work();
      if (done) notify(done, 'success');
    } catch (e) {
      notify(getCaughtErrorMessage(e), 'error');
    }
  };

  /** Sends the reader to sign in; they come back to this page. */
  const authorize = (connector: InstalledConnector) =>
    run(async () => {
      if (!connector.id) return;
      const { redirectUrl } = await authorizeInstallation(connector.id).unwrap();
      window.location.href = redirectUrl;
    });

  const connect = async (entry: CatalogEntry) => {
    const action = connectAction(entry);
    if (action === 'askCredentials') {
      setDialog({ open: true, preset: entry });
      return;
    }
    if (action === 'installed') return;
    setBusyKey(entry.key);
    await run(async () => {
      const connector = await createInstallation({
        definitionKey: entry.key,
        authKind: action === 'authorize' ? 'oauth' : 'none',
      }).unwrap();
      if (action === 'authorize') await authorize(connector);
      else notify(`${entry.displayName} 已連線，AI 現在可以使用它的工具`, 'success');
    });
    setBusyKey(null);
  };

  const customCount = catalog.installed.filter((c) => c.definitionKey === 'custom').length;
  const atLimit = customCount >= catalog.customLimit;

  return (
    <Box sx={{ mb: 2 }}>
      <Stack direction="row" sx={{ alignItems: 'center', mb: 1, gap: 2, flexWrap: 'wrap' }}>
        <Typography variant="body2" color="text.secondary" sx={{ flex: 1, minWidth: 240 }}>
          連上之後，各 app 的 AI 助理就能在對話裡使用這些工具；寫入類的動作每次都會先問你。
        </Typography>
        <Button
          variant="contained"
          startIcon={<Add />}
          disabled={atLimit}
          onClick={() => setDialog({ open: true, preset: null })}
        >
          新增 MCP server
        </Button>
      </Stack>
      {atLimit && (
        <Typography variant="caption" color="text.secondary" component="p" sx={{ mb: 1 }}>
          自訂 MCP server 最多 {catalog.customLimit} 個，請先移除用不到的。
        </Typography>
      )}

      <Box sx={{ mt: 3 }}>
        <InstalledSection connectors={catalog.installed} onOpen={(c) => setSelectedKey(keyOf(c))} />
        <CatalogSection
          entries={catalog.entries}
          busyKey={busyKey}
          onConnect={connect}
          onBrowseRegistry={
            atLimit ? null : () => setDialog({ open: true, preset: null, browseRegistry: true })
          }
        />
      </Box>

      <ConnectorDetailDrawer
        connector={selected}
        onClose={() => setSelectedKey(null)}
        busy={testing || toggling || removing || saving}
        onTest={() =>
          selected?.id &&
          run(async () => {
            const r = await testInstallation(selected.id!).unwrap();
            notify(
              r.ok
                ? `連線正常（${r.latencyMs} ms），${r.connector.tools.length} 個工具`
                : `連線失敗：${r.error ?? '未知原因'}`,
              r.ok ? 'success' : 'error',
            );
          })
        }
        onSetEnabled={(enabled) =>
          selected?.id &&
          run(
            () => setEnabled({ id: selected.id!, enabled }).unwrap(),
            enabled ? `已啟用 ${selected.displayName}` : `已停用 ${selected.displayName}`,
          )
        }
        onRemove={() =>
          selected?.id &&
          run(async () => {
            await removeInstallation(selected.id!).unwrap();
            setSelectedKey(null);
          }, `已移除 ${selected.displayName}`)
        }
        onReauthorize={() => selected && authorize(selected)}
        onPermission={(toolName, permission) =>
          selected &&
          run(async () => {
            const saved = await setToolPermission({
              definitionKey: selected.definitionKey,
              installationId: selected.id,
              toolName,
              permission,
            }).unwrap();
            // An always-on connector gets its row (and id) with its first change.
            setSelectedKey(keyOf(saved));
          })
        }
      />
      <AddMcpServerDialog
        open={dialog.open}
        preset={dialog.preset}
        browseRegistry={dialog.browseRegistry}
        registrySearch={dialog.registrySearch}
        installedUrls={installedServerUrls(catalog.installed)}
        onClose={() => setDialog({ open: false, preset: null })}
        onAuthorize={(c) => {
          setDialog({ open: false, preset: null });
          authorize(c);
        }}
      />
    </Box>
  );
}

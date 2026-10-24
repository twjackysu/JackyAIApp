import { DeleteOutlined, Hub } from '@mui/icons-material';
import { Alert, Box, Button, Chip, Paper, Stack, Typography } from '@mui/material';
import { useState } from 'react';

import { siteMcpUrl } from './clientConfigs';
import CopyField from './CopyField';
import CreateTokenDialog from './CreateTokenDialog';
import { tokenView } from './tokenView';

import type { SiteAccessToken } from '@/apis/connectorsApis/types';

import {
  useGetSiteTokensQuery,
  useRevokeSiteTokenMutation,
} from '@/apis/connectorsApis/connectorsApis';
import { getApiErrorMessage, getCaughtErrorMessage } from '@/apis/utils/errorMessage';

interface SiteMcpSectionProps {
  notify: (message: string, severity: 'success' | 'error') => void;
}

/**
 * The other way round from the sections above: the site as an MCP server another AI (Claude Desktop,
 * Claude Code, Cursor, VS Code) connects to with a token of the reader's, to use the site's tools in
 * their name. What it may use follows the site's own tools' permissions above.
 */
export default function SiteMcpSection({ notify }: SiteMcpSectionProps) {
  const { data, isLoading, error } = useGetSiteTokensQuery();
  const [revokeToken, { isLoading: revoking }] = useRevokeSiteTokenMutation();
  const [dialogOpen, setDialogOpen] = useState(false);
  const [confirming, setConfirming] = useState<string | null>(null);
  const serverUrl = siteMcpUrl(window.location.origin);

  const revoke = async (token: SiteAccessToken) => {
    try {
      await revokeToken(token.id).unwrap();
      notify(`已撤銷「${token.name}」，用它的 AI 從現在起連不上`, 'success');
    } catch (e) {
      notify(getCaughtErrorMessage(e), 'error');
    }
    setConfirming(null);
  };

  const tokens = data?.tokens ?? [];
  const atLimit = data ? tokens.length >= data.limit : false;
  const now = Date.now();

  return (
    <Paper
      component="section"
      aria-labelledby="site-mcp-title"
      variant="outlined"
      sx={{ p: { xs: 2, sm: 3 }, mb: 5 }}
    >
      <Stack direction="row" spacing={1.5} sx={{ alignItems: 'center', mb: 1, flexWrap: 'wrap' }}>
        <Hub color="primary" />
        <Typography id="site-mcp-title" variant="h6" component="h2" sx={{ flex: 1 }}>
          讓其他 AI 使用本站
        </Typography>
        <Chip size="small" label="MCP server" color="primary" variant="outlined" />
      </Stack>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
        支援 MCP 的 AI 可以用你的身分使用本站的工具：查字典、加單字、學習進度、投資組合、Google
        日曆，在 Claude、ChatGPT 裡還會畫出跟本站 AI
        助理一樣的表格與卡片。它們拿得到的工具跟上面「本站功能」的權限一致，停用的不會提供。
      </Typography>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
        Claude.ai、ChatGPT：在它們的 connector 設定貼上下面的網址，登入後在本站按同意就好。Claude
        Code、Claude Desktop、Cursor、VS Code：產生一個 token，複製對應的設定。
      </Typography>

      <Typography variant="subtitle2" gutterBottom>
        伺服器網址（Streamable HTTP）
      </Typography>
      <CopyField text={serverUrl} label="複製網址" />

      <Stack direction="row" sx={{ alignItems: 'center', mt: 3, mb: 1, gap: 2, flexWrap: 'wrap' }}>
        <Typography variant="subtitle2" sx={{ flex: 1 }}>
          你的 token
        </Typography>
        <Button variant="contained" onClick={() => setDialogOpen(true)} disabled={!data || atLimit}>
          產生 token
        </Button>
      </Stack>
      {atLimit && (
        <Typography variant="caption" color="text.secondary" component="p" sx={{ mb: 1 }}>
          最多 {data?.limit} 個 token，請先撤銷用不到的。
        </Typography>
      )}

      {isLoading ? null : error ? (
        <Alert severity="info">token 清單目前無法載入（{getApiErrorMessage(error)}）。</Alert>
      ) : tokens.length === 0 ? (
        <Typography variant="body2" color="text.secondary">
          還沒有 token。產生一個，把設定貼進你的 AI 就能用了。
        </Typography>
      ) : (
        <Stack spacing={1}>
          {tokens.map((t) => {
            const view = tokenView(t, now);
            return (
              <Box
                key={t.id}
                sx={{
                  display: 'flex',
                  alignItems: 'center',
                  gap: 1.5,
                  flexWrap: 'wrap',
                  p: 1.5,
                  border: 1,
                  borderColor: 'divider',
                  borderRadius: 1,
                  opacity: view.expired ? 0.6 : 1,
                }}
              >
                <Box sx={{ flex: 1, minWidth: 200 }}>
                  <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
                    <Typography variant="body1" sx={{ fontWeight: 600 }}>
                      {t.name}
                    </Typography>
                    <Chip
                      size="small"
                      label={t.allowWrites ? '可寫入' : '唯讀'}
                      color={t.allowWrites ? 'warning' : 'default'}
                      variant="outlined"
                    />
                    {t.viaOAuth && <Chip size="small" label="經 OAuth 登入" variant="outlined" />}
                  </Stack>
                  <Typography variant="caption" color="text.secondary" component="p">
                    <Box component="code" sx={{ fontFamily: 'monospace' }}>
                      {t.hint}
                    </Box>
                    {` ・ ${view.lastUsed} ・ ${view.expires}`}
                  </Typography>
                </Box>
                {confirming === t.id ? (
                  <Stack direction="row" spacing={1}>
                    <Button size="small" onClick={() => setConfirming(null)}>
                      取消
                    </Button>
                    <Button
                      size="small"
                      color="error"
                      variant="contained"
                      disabled={revoking}
                      onClick={() => revoke(t)}
                    >
                      確定撤銷
                    </Button>
                  </Stack>
                ) : (
                  <Button
                    size="small"
                    color="error"
                    startIcon={<DeleteOutlined />}
                    onClick={() => setConfirming(t.id)}
                  >
                    撤銷
                  </Button>
                )}
              </Box>
            );
          })}
        </Stack>
      )}

      <CreateTokenDialog
        open={dialogOpen}
        serverUrl={serverUrl}
        onClose={() => setDialogOpen(false)}
      />
    </Paper>
  );
}

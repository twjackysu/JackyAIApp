import {
  Alert,
  Box,
  Button,
  Checkbox,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  FormControlLabel,
  MenuItem,
  Stack,
  Tab,
  Tabs,
  TextField,
  Typography,
} from '@mui/material';
import { useState } from 'react';

import { clientConfigs } from './clientConfigs';
import CopyField from './CopyField';
import { LIFETIMES } from './tokenView';

import type { CreatedSiteAccessToken } from '@/apis/connectorsApis/types';

import { useCreateSiteTokenMutation } from '@/apis/connectorsApis/connectorsApis';
import { getCaughtErrorMessage } from '@/apis/utils/errorMessage';

interface CreateTokenDialogProps {
  open: boolean;
  serverUrl: string;
  onClose: () => void;
}

const MAX_NAME = 50;

/**
 * Makes a token for another AI: what to call it, whether it may add or change things, how long it
 * lasts. Then the token, once, with the settings for the AIs readers most often use.
 */
export default function CreateTokenDialog({ open, serverUrl, onClose }: CreateTokenDialogProps) {
  const [createToken, { isLoading }] = useCreateSiteTokenMutation();
  const [name, setName] = useState('Claude Desktop');
  const [allowWrites, setAllowWrites] = useState(false);
  const [days, setDays] = useState<number>(90);
  const [error, setError] = useState<string | null>(null);
  const [created, setCreated] = useState<CreatedSiteAccessToken | null>(null);
  const [client, setClient] = useState(0);

  const close = () => {
    onClose();
    // Forget the token as soon as the dialog goes: it is shown once.
    setCreated(null);
    setError(null);
    setClient(0);
  };

  const trimmed = name.trim();
  const submit = async () => {
    setError(null);
    try {
      setCreated(await createToken({ name: trimmed, allowWrites, expiresInDays: days }).unwrap());
    } catch (e) {
      setError(getCaughtErrorMessage(e));
    }
  };

  const configs = created ? clientConfigs(serverUrl, created.token) : [];
  const config = configs[client];

  return (
    <Dialog open={open} onClose={created ? undefined : close} fullWidth maxWidth="sm">
      <DialogTitle>{created ? 'Token 已產生' : '產生 token'}</DialogTitle>
      {created && config ? (
        <DialogContent>
          <Alert severity="warning" sx={{ mb: 2 }}>
            這是唯一一次看得到這個 token。請現在複製或貼進設定；之後只能撤銷再產生新的。
          </Alert>
          <CopyField text={created.token} label="複製 token" />
          <Typography variant="subtitle2" sx={{ mt: 3, mb: 1 }}>
            貼到你的 AI
          </Typography>
          <Tabs
            value={client}
            onChange={(_, v: number) => setClient(v)}
            variant="scrollable"
            allowScrollButtonsMobile
            sx={{ mb: 1.5 }}
          >
            {configs.map((c) => (
              <Tab key={c.key} label={c.label} />
            ))}
          </Tabs>
          <Typography variant="caption" color="text.secondary" component="p" sx={{ mb: 1 }}>
            {config.where}
          </Typography>
          <CopyField text={config.text} label={`複製 ${config.label} 設定`} block />
          <Typography variant="body2" color="text.secondary" sx={{ mt: 2 }}>
            加好之後，對那個 AI 說「查一下 serendipity 的意思」或「我的投資組合現在多少？」試試看。
          </Typography>
          {config.prompts && (
            <Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>
              也有現成指令（今日簡報、考我單字、解讀 dashboard…）：
              {config.prompts.split(/(\/mcp\S+)/).map((part, i) =>
                i % 2 === 1 ? (
                  <Box
                    key={i}
                    component="code"
                    sx={{
                      fontFamily: 'monospace',
                      color: 'text.primary',
                      // One line where it fits; a phone breaks it anywhere rather than overflow.
                      whiteSpace: { sm: 'nowrap' },
                      overflowWrap: 'anywhere',
                    }}
                  >
                    {part}
                  </Box>
                ) : (
                  part
                ),
              )}
              。
            </Typography>
          )}
        </DialogContent>
      ) : (
        <DialogContent>
          <Stack spacing={2.5} sx={{ pt: 1 }}>
            <TextField
              label="名稱"
              value={name}
              onChange={(e) => setName(e.target.value)}
              helperText="用來分辨是哪個 AI，例如 Claude Desktop、公司的 Cursor"
              slotProps={{ htmlInput: { maxLength: MAX_NAME } }}
              autoFocus
              fullWidth
            />
            <TextField
              select
              label="有效期限"
              value={days}
              onChange={(e) => setDays(Number(e.target.value))}
              fullWidth
            >
              {LIFETIMES.map((l) => (
                <MenuItem key={l.days} value={l.days}>
                  {l.label}
                </MenuItem>
              ))}
            </TextField>
            <Box>
              <FormControlLabel
                control={
                  <Checkbox
                    checked={allowWrites}
                    onChange={(e) => setAllowWrites(e.target.checked)}
                  />
                }
                label="也允許寫入（例如把單字加入單字庫）"
              />
              <Typography variant="caption" color="text.secondary" component="p" sx={{ pl: 4 }}>
                {allowWrites
                  ? '本站不會再跳出確認卡：由那個 AI 自己的確認機制把關（Claude、Cursor 預設每次都會問你）。無法復原的動作一律不開放。'
                  : '只開放讀取：查字典、學習進度、投資組合、Google 日曆、Jira。'}
              </Typography>
            </Box>
            {error && <Alert severity="error">{error}</Alert>}
          </Stack>
        </DialogContent>
      )}
      <DialogActions>
        {created ? (
          <Button variant="contained" onClick={close}>
            我已經複製好了
          </Button>
        ) : (
          <>
            <Button onClick={close}>取消</Button>
            <Button
              variant="contained"
              onClick={submit}
              disabled={isLoading || trimmed.length === 0}
            >
              產生
            </Button>
          </>
        )}
      </DialogActions>
    </Dialog>
  );
}

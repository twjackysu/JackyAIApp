import { Hub, Lock } from '@mui/icons-material';
import {
  Alert,
  Box,
  Button,
  Checkbox,
  FormControlLabel,
  Paper,
  Stack,
  Typography,
} from '@mui/material';
import { useMemo, useState } from 'react';
import { useLocation } from 'react-router-dom';

import { authorizeRequestOf } from './authorizeRequest';

import { useGetUserInfoQuery } from '@/apis/accountApis';
import { useDecideOAuthMutation, useGetOAuthConsentQuery } from '@/apis/oauthApis';
import { getApiErrorMessage, getCaughtErrorMessage } from '@/apis/utils/errorMessage';
import CenteredSpinner from '@/components/CenteredSpinner';

/**
 * Another AI (Claude.ai, ChatGPT…) asks to use the site's MCP server in the reader's name: who asks,
 * where the reader goes back to, what it may do. Signed-out readers sign in first and come back here
 * with the request intact.
 */
export default function OAuthConsent() {
  const { search, pathname } = useLocation();
  const request = useMemo(() => authorizeRequestOf(search), [search]);
  const { data: user, isFetching: checkingUser } = useGetUserInfoQuery();
  const signedIn = !!user?.data;

  return (
    <Box sx={{ maxWidth: 520, mx: 'auto', px: 2, py: { xs: 3, sm: 6 } }}>
      <Paper variant="outlined" sx={{ p: { xs: 2.5, sm: 4 } }}>
        <Stack direction="row" spacing={1.5} sx={{ alignItems: 'center', mb: 2 }}>
          <Hub color="primary" />
          <Typography variant="h6" component="h1">
            連線到 Jacky Studio
          </Typography>
        </Stack>
        {checkingUser ? (
          <CenteredSpinner sx={{ minHeight: 120 }} />
        ) : signedIn ? (
          <Consent request={request} />
        ) : (
          <>
            <Typography sx={{ mb: 3 }}>
              有一個 AI 想用你的 Jacky Studio 帳號。請先登入，登入後會回到這裡讓你決定。
            </Typography>
            <Button
              variant="contained"
              fullWidth
              onClick={() =>
                window.location.assign(
                  `/api/account/login/Google?ReturnUrl=${encodeURIComponent(pathname + search)}`,
                )
              }
            >
              用 Google 登入
            </Button>
          </>
        )}
      </Paper>
    </Box>
  );
}

function Consent({ request }: { request: ReturnType<typeof authorizeRequestOf> }) {
  const { data: consent, error, isLoading } = useGetOAuthConsentQuery(request);
  const [decide, { isLoading: deciding }] = useDecideOAuthMutation();
  const [allowWrites, setAllowWrites] = useState(false);
  const [failure, setFailure] = useState<string | null>(null);

  if (isLoading) return <CenteredSpinner sx={{ minHeight: 120 }} />;
  if (error || !consent) {
    return <Alert severity="error">{getApiErrorMessage(error)}</Alert>;
  }

  const answer = async (approve: boolean) => {
    setFailure(null);
    try {
      const { redirectUrl } = await decide({ request, approve, allowWrites }).unwrap();
      window.location.assign(redirectUrl);
    } catch (e) {
      setFailure(getCaughtErrorMessage(e));
    }
  };

  return (
    <>
      <Typography variant="body1" sx={{ mb: 2 }}>
        <strong>{consent.clientName}</strong> 想用你的身分使用 Jacky Studio 的工具，完成後會回到{' '}
        <strong>{consent.redirectHost}</strong>。
      </Typography>
      <Box component="ul" sx={{ pl: 2.5, mt: 0, mb: 2, '& li': { mb: 0.5 } }}>
        <li>查字典、讀你的學習進度、投資組合摘要、Google 日曆</li>
        <li>你在 Connectors 頁停用的本站工具不會提供</li>
        <li>隨時可以在 Connectors 頁的「讓其他 AI 使用本站」撤銷</li>
      </Box>
      <FormControlLabel
        control={
          <Checkbox checked={allowWrites} onChange={(e) => setAllowWrites(e.target.checked)} />
        }
        label={
          consent.wantsWrites
            ? '也允許寫入（它要求了，例如把單字加入單字庫）'
            : '也允許寫入（例如把單字加入單字庫）'
        }
      />
      <Typography variant="caption" color="text.secondary" component="p" sx={{ pl: 4, mb: 3 }}>
        寫入時本站不會再跳出確認卡，由那個 AI 自己的確認把關。無法復原的動作一律不開放。
      </Typography>
      {failure && (
        <Alert severity="error" sx={{ mb: 2 }}>
          {failure}
        </Alert>
      )}
      <Stack direction="row" spacing={1.5} sx={{ justifyContent: 'flex-end' }}>
        <Button onClick={() => answer(false)} disabled={deciding}>
          拒絕
        </Button>
        <Button
          variant="contained"
          startIcon={<Lock />}
          onClick={() => answer(true)}
          disabled={deciding}
        >
          允許
        </Button>
      </Stack>
    </>
  );
}

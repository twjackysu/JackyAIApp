import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import CircularProgress from '@mui/material/CircularProgress';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import FormControlLabel from '@mui/material/FormControlLabel';
import Radio from '@mui/material/Radio';
import RadioGroup from '@mui/material/RadioGroup';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { useState } from 'react';

import type { DashboardVisibility, ShareSettings } from '@/apis/dashboardApis/types';

import { useGetShareSettingsQuery, useUpdateShareSettingsMutation } from '@/apis/dashboardApis';

interface ShareDialogProps {
  open: boolean;
  onClose: () => void;
  dashboardId: string;
}

export function ShareDialog({ open, onClose, dashboardId }: ShareDialogProps) {
  const { data, isLoading } = useGetShareSettingsQuery(dashboardId, { skip: !open });

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="xs">
      <DialogTitle>分享 Dashboard</DialogTitle>
      {isLoading || !data?.data ? (
        <DialogContent>
          <CircularProgress size={24} sx={{ display: 'block', mx: 'auto', my: 2 }} />
        </DialogContent>
      ) : (
        // Remount per fetched settings so the form seeds from initial values via
        // useState initializers — no effect needed to sync fetched data into state.
        <ShareForm dashboardId={dashboardId} initial={data.data} onClose={onClose} />
      )}
    </Dialog>
  );
}

const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

function ShareForm({
  dashboardId,
  initial,
  onClose,
}: {
  dashboardId: string;
  initial: ShareSettings;
  onClose: () => void;
}) {
  const [visibility, setVisibility] = useState<DashboardVisibility>(initial.visibility);
  const [emails, setEmails] = useState<string[]>(
    initial.members.map((m) => m.email).filter((e): e is string => !!e),
  );
  const [emailInput, setEmailInput] = useState('');
  const [error, setError] = useState<string | null>(null);

  const [updateShare, { isLoading: saving }] = useUpdateShareSettingsMutation();

  const addEmail = () => {
    const e = emailInput.trim().toLowerCase();
    if (!e) return;
    if (!EMAIL_RE.test(e)) {
      setError('Email 格式不正確。');
      return;
    }
    if (!emails.includes(e)) setEmails((prev) => [...prev, e]);
    setEmailInput('');
    setError(null);
  };

  const handleSave = async () => {
    setError(null);
    try {
      await updateShare({ id: dashboardId, visibility, emails }).unwrap();
      onClose();
    } catch {
      setError('儲存失敗，請再試一次。');
    }
  };

  return (
    <>
      <DialogContent>
        <RadioGroup
          value={visibility}
          onChange={(e) => setVisibility(e.target.value as DashboardVisibility)}
        >
          <FormControlLabel
            value="Private"
            control={<Radio size="small" />}
            label="僅自己（私人）"
          />
          <FormControlLabel
            value="Public"
            control={<Radio size="small" />}
            label="所有登入的使用者"
          />
          <FormControlLabel
            value="Anyone"
            control={<Radio size="small" />}
            label="任何人（免登入，知道連結即可看）"
          />
          <FormControlLabel
            value="Restricted"
            control={<Radio size="small" />}
            label="指定使用者"
          />
        </RadioGroup>

        {visibility === 'Anyone' && (
          <Alert severity="info" sx={{ mt: 1 }}>
            任何拿到連結的人都能檢視此 dashboard，不需登入。
          </Alert>
        )}

        {visibility === 'Restricted' && (
          <Box sx={{ mt: 1, ml: 4 }}>
            <Box sx={{ display: 'flex', gap: 1 }}>
              <TextField
                size="small"
                fullWidth
                placeholder="輸入 email 後按 Enter"
                value={emailInput}
                onChange={(e) => setEmailInput(e.target.value)}
                onKeyDown={(e) => {
                  if (e.key === 'Enter') {
                    e.preventDefault();
                    addEmail();
                  }
                }}
              />
              <Button onClick={addEmail}>加入</Button>
            </Box>
            <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 0.75, mt: 1.5 }}>
              {emails.length === 0 ? (
                <Typography variant="caption" color="text.secondary">
                  尚未加入任何使用者。
                </Typography>
              ) : (
                emails.map((e) => (
                  <Chip
                    key={e}
                    label={e}
                    size="small"
                    onDelete={() => setEmails((prev) => prev.filter((x) => x !== e))}
                  />
                ))
              )}
            </Box>
            <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 1 }}>
              只有已註冊（登入過）的 email 會被授權，其餘會被忽略。
            </Typography>
          </Box>
        )}

        {error && (
          <Alert severity="error" sx={{ mt: 1.5 }}>
            {error}
          </Alert>
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose} disabled={saving}>
          取消
        </Button>
        <Button variant="contained" onClick={handleSave} disabled={saving}>
          {saving ? '儲存中…' : '儲存'}
        </Button>
      </DialogActions>
    </>
  );
}

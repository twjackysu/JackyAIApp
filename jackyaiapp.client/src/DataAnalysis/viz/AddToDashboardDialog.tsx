import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import CircularProgress from '@mui/material/CircularProgress';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import FormControlLabel from '@mui/material/FormControlLabel';
import MenuItem from '@mui/material/MenuItem';
import Radio from '@mui/material/Radio';
import RadioGroup from '@mui/material/RadioGroup';
import TextField from '@mui/material/TextField';
import { useState } from 'react';
import { useNavigate } from 'react-router-dom';

import type { SummarizeRequest } from '@/apis/dataAnalysisApis/types';

import {
  useAddWidgetMutation,
  useCreateDashboardMutation,
  useGetDashboardsQuery,
} from '@/apis/dashboardApis';

interface AddToDashboardDialogProps {
  open: boolean;
  onClose: () => void;
  vizType: string;
  plan: SummarizeRequest;
  gridDefault: { w: number; h: number };
}

export function AddToDashboardDialog({
  open,
  onClose,
  vizType,
  plan,
  gridDefault,
}: AddToDashboardDialogProps) {
  const navigate = useNavigate();
  const { data: dashRes, isLoading } = useGetDashboardsQuery(undefined, { skip: !open });
  const dashboards = dashRes?.data ?? [];

  const [mode, setMode] = useState<'new' | 'existing'>('new');
  const [name, setName] = useState('');
  const [selectedId, setSelectedId] = useState('');
  const [error, setError] = useState<string | null>(null);

  const [createDashboard, { isLoading: creating }] = useCreateDashboardMutation();
  const [addWidget, { isLoading: adding }] = useAddWidgetMutation();
  const busy = creating || adding;

  const title = plan.measures.map((m) => m.alias || m.fieldName).join(', ');

  const handleConfirm = async () => {
    setError(null);
    try {
      let dashboardId = selectedId;
      if (mode === 'new') {
        const n = name.trim();
        if (!n) {
          setError('請輸入 dashboard 名稱。');
          return;
        }
        const res = await createDashboard({ name: n }).unwrap();
        dashboardId = res.data.id;
      } else if (!dashboardId) {
        setError('請選擇一個 dashboard。');
        return;
      }
      await addWidget({
        dashboardId,
        widget: { vizType, plan, w: gridDefault.w, h: gridDefault.h, title },
      }).unwrap();
      onClose();
      navigate(`/dashboards/${dashboardId}`);
    } catch {
      setError('加入失敗，請再試一次。');
    }
  };

  return (
    <Dialog open={open} onClose={busy ? undefined : onClose} fullWidth maxWidth="xs">
      <DialogTitle>加入 Dashboard</DialogTitle>
      <DialogContent>
        {isLoading ? (
          <CircularProgress size={24} sx={{ display: 'block', mx: 'auto', my: 2 }} />
        ) : (
          <RadioGroup
            value={mode}
            onChange={(e) => {
              const m = e.target.value as 'new' | 'existing';
              setMode(m);
              if (m === 'existing' && !selectedId && dashboards.length > 0) {
                setSelectedId(dashboards[0].id);
              }
            }}
          >
            <FormControlLabel
              value="new"
              control={<Radio size="small" />}
              label="建立新的 dashboard"
            />
            {mode === 'new' && (
              <TextField
                autoFocus
                size="small"
                fullWidth
                placeholder="Dashboard 名稱"
                value={name}
                onChange={(e) => setName(e.target.value)}
                sx={{ mb: 1, ml: 4, width: 'auto' }}
              />
            )}
            <FormControlLabel
              value="existing"
              control={<Radio size="small" />}
              label="加入現有 dashboard"
              disabled={dashboards.length === 0}
            />
            {mode === 'existing' && dashboards.length > 0 && (
              <TextField
                select
                size="small"
                fullWidth
                value={selectedId}
                onChange={(e) => setSelectedId(e.target.value)}
                sx={{ ml: 4, width: 'auto' }}
              >
                {dashboards.map((d) => (
                  <MenuItem key={d.id} value={d.id}>
                    {d.name}（{d.widgetCount}）
                  </MenuItem>
                ))}
              </TextField>
            )}
          </RadioGroup>
        )}
        {error && (
          <Alert severity="error" sx={{ mt: 1.5 }}>
            {error}
          </Alert>
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose} disabled={busy}>
          取消
        </Button>
        <Button variant="contained" onClick={handleConfirm} disabled={busy}>
          {busy ? '加入中…' : '加入'}
        </Button>
      </DialogActions>
    </Dialog>
  );
}

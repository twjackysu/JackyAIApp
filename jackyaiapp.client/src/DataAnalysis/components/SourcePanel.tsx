import AccessTimeIcon from '@mui/icons-material/AccessTime';
import ClearIcon from '@mui/icons-material/Clear';
import CloudUploadIcon from '@mui/icons-material/CloudUpload';
import SettingsIcon from '@mui/icons-material/Settings';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import FormControl from '@mui/material/FormControl';
import IconButton from '@mui/material/IconButton';
import InputLabel from '@mui/material/InputLabel';
import MenuItem from '@mui/material/MenuItem';
import Paper from '@mui/material/Paper';
import Select from '@mui/material/Select';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';

import type { DataSourceConfigDto, SessionInfo } from '../../apis/dataAnalysisApis/types';

import { ActionTooltip } from '@/components/hints';
import { fmtNum } from '@/utils/format';

interface SourcePanelProps {
  isLoggedIn: boolean;
  sources: DataSourceConfigDto[];
  activeSourceId: string | null;
  tableNameInput: string;
  sessionInfo: SessionInfo | null;
  onSourceChange: (id: string) => void;
  onTableNameChange: (val: string) => void;
  onApplyTableName: () => void;
  onOpenManager: () => void;
  onOpenUpload: () => void;
  onClearSession: () => void;
}

export function SourcePanel({
  isLoggedIn,
  sources,
  activeSourceId,
  tableNameInput,
  sessionInfo,
  onSourceChange,
  onTableNameChange,
  onApplyTableName,
  onOpenManager,
  onOpenUpload,
  onClearSession,
}: SourcePanelProps) {
  const expiresLabel = sessionInfo
    ? (() => {
        const diffMs = new Date(sessionInfo.expiresAt).getTime() - Date.now();
        const diffMin = Math.round(diffMs / 60_000);
        if (diffMin <= 0) return '已過期';
        if (diffMin < 60) return `${diffMin} 分鐘後過期`;
        return `${Math.round(diffMin / 60)} 小時後過期`;
      })()
    : '';

  return (
    <Paper variant="outlined" sx={{ p: 2, mb: 2 }}>
      <Typography variant="subtitle2" gutterBottom sx={{ color: 'text.secondary' }}>
        資料來源
      </Typography>

      {/* ── Session mode ──────────────────────────────────────────────────── */}
      {sessionInfo ? (
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, flexWrap: 'wrap' }}>
          <Chip
            icon={<AccessTimeIcon />}
            label={`${sessionInfo.name} — ${fmtNum(sessionInfo.rowCount)} 筆`}
            color="warning"
            variant="outlined"
          />
          <Chip label={expiresLabel} color="warning" size="small" />
          <Typography variant="caption" color="text.secondary">
            暫存資料 — 不會儲存到任何資料庫
          </Typography>
          <ActionTooltip title="清除暫存並返回資料庫來源">
            <IconButton size="small" onClick={onClearSession}>
              <ClearIcon fontSize="small" />
            </IconButton>
          </ActionTooltip>
        </Box>
      ) : isLoggedIn ? (
        /* ── Logged-in normal mode ──────────────────────────────────────── */
        <Box sx={{ display: 'flex', gap: 1.5, alignItems: 'flex-start', flexWrap: 'wrap' }}>
          <FormControl size="small" sx={{ minWidth: { xs: '100%', sm: 260 } }}>
            <InputLabel>來源</InputLabel>
            <Select
              value={activeSourceId ?? ''}
              label="來源"
              onChange={(e) => onSourceChange(e.target.value as string)}
            >
              <MenuItem value="">
                <em>預設 (JackyAI SQL / dbo.Orders)</em>
              </MenuItem>
              {sources.map((s) => (
                <MenuItem key={s.id} value={s.id}>
                  {s.name}&nbsp;
                  <Chip
                    label={s.connectorType}
                    size="small"
                    color={s.connectorType === 'Adx' ? 'secondary' : 'primary'}
                    sx={{ ml: 0.5 }}
                  />
                </MenuItem>
              ))}
            </Select>
          </FormControl>

          <TextField
            size="small"
            label="資料表"
            value={tableNameInput}
            onChange={(e) => onTableNameChange(e.target.value)}
            onBlur={onApplyTableName}
            onKeyDown={(e) => {
              if (e.key === 'Enter') onApplyTableName();
            }}
            sx={{ width: { xs: '100%', sm: 220 } }}
            helperText="按 Enter 或點擊其他地方套用"
          />

          <Button
            size="small"
            variant="outlined"
            startIcon={<SettingsIcon />}
            onClick={onOpenManager}
            sx={{ mt: 0.25 }}
          >
            管理來源
          </Button>

          <Button
            size="small"
            variant="outlined"
            color="warning"
            startIcon={<CloudUploadIcon />}
            onClick={onOpenUpload}
            sx={{ mt: 0.25 }}
          >
            上傳資料
          </Button>
        </Box>
      ) : (
        /* ── Not logged in ──────────────────────────────────────────────── */
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, flexWrap: 'wrap' }}>
          <Chip label="預設 (JackyAI SQL / dbo.Orders)" color="default" size="small" />
          <Typography variant="body2" color="text.secondary">
            —{' '}
            <a
              href={`/api/account/login/Google?ReturnUrl=${encodeURIComponent(window.location.pathname)}`}
              style={{ color: 'inherit' }}
            >
              登入
            </a>{' '}
            以設定自訂資料來源。
          </Typography>
          <Button
            size="small"
            variant="outlined"
            color="warning"
            startIcon={<CloudUploadIcon />}
            onClick={onOpenUpload}
          >
            上傳資料（暫存）
          </Button>
        </Box>
      )}
    </Paper>
  );
}

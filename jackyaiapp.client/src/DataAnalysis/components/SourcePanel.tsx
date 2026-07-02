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
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';

import type { DataSourceConfigDto, SessionInfo } from '../../apis/dataAnalysisApis/types';

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
        if (diffMin <= 0) return 'expired';
        if (diffMin < 60) return `expires in ${diffMin}m`;
        return `expires in ${Math.round(diffMin / 60)}h`;
      })()
    : '';

  return (
    <Paper variant="outlined" sx={{ p: 2, mb: 2 }}>
      <Typography variant="subtitle2" gutterBottom sx={{ color: 'text.secondary' }}>
        Data Source
      </Typography>

      {/* ── Session mode ──────────────────────────────────────────────────── */}
      {sessionInfo ? (
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, flexWrap: 'wrap' }}>
          <Chip
            icon={<AccessTimeIcon />}
            label={`${sessionInfo.name} — ${fmtNum(sessionInfo.rowCount)} rows`}
            color="warning"
            variant="outlined"
          />
          <Chip label={expiresLabel} color="warning" size="small" />
          <Typography variant="caption" color="text.secondary">
            Temporary data — not saved to any database
          </Typography>
          <Tooltip title="Clear session and return to database sources">
            <IconButton size="small" onClick={onClearSession}>
              <ClearIcon fontSize="small" />
            </IconButton>
          </Tooltip>
        </Box>
      ) : isLoggedIn ? (
        /* ── Logged-in normal mode ──────────────────────────────────────── */
        <Box sx={{ display: 'flex', gap: 1.5, alignItems: 'flex-start', flexWrap: 'wrap' }}>
          <FormControl size="small" sx={{ minWidth: { xs: '100%', sm: 260 } }}>
            <InputLabel>Source</InputLabel>
            <Select
              value={activeSourceId ?? ''}
              label="Source"
              onChange={(e) => onSourceChange(e.target.value as string)}
            >
              <MenuItem value="">
                <em>Default (JackyAI SQL / dbo.Orders)</em>
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
            label="Table"
            value={tableNameInput}
            onChange={(e) => onTableNameChange(e.target.value)}
            onBlur={onApplyTableName}
            onKeyDown={(e) => {
              if (e.key === 'Enter') onApplyTableName();
            }}
            sx={{ width: { xs: '100%', sm: 220 } }}
            helperText="Press Enter or click away to apply"
          />

          <Button
            size="small"
            variant="outlined"
            startIcon={<SettingsIcon />}
            onClick={onOpenManager}
            sx={{ mt: 0.25 }}
          >
            Manage Sources
          </Button>

          <Button
            size="small"
            variant="outlined"
            color="warning"
            startIcon={<CloudUploadIcon />}
            onClick={onOpenUpload}
            sx={{ mt: 0.25 }}
          >
            Upload Data
          </Button>
        </Box>
      ) : (
        /* ── Not logged in ──────────────────────────────────────────────── */
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, flexWrap: 'wrap' }}>
          <Chip label="Default (JackyAI SQL / dbo.Orders)" color="default" size="small" />
          <Typography variant="body2" color="text.secondary">
            —{' '}
            <a
              href={`/api/account/login/Google?ReturnUrl=${encodeURIComponent(window.location.pathname)}`}
              style={{ color: 'inherit' }}
            >
              Sign in
            </a>{' '}
            to configure custom data sources.
          </Typography>
          <Button
            size="small"
            variant="outlined"
            color="warning"
            startIcon={<CloudUploadIcon />}
            onClick={onOpenUpload}
          >
            Upload Data (temp)
          </Button>
        </Box>
      )}
    </Paper>
  );
}

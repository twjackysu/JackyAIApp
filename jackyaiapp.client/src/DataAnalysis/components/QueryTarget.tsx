import WarningAmberIcon from '@mui/icons-material/WarningAmber';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Typography from '@mui/material/Typography';

import { DEFAULT_TABLE } from '../constants';

const TABLE_SX = {
  bgcolor: 'action.selected',
  px: 0.5,
  borderRadius: '3px',
  fontFamily: 'monospace',
  fontSize: '0.875em',
} as const;

interface QueryTargetProps {
  /** null for the built-in source. */
  activeSourceId: string | null;
  activeSourceName: string | undefined;
  activeTableName: string;
}

/** What is being queried — for a custom source, a note that it is one — when not on an upload. */
export function QueryTarget({
  activeSourceId,
  activeSourceName,
  activeTableName,
}: QueryTargetProps) {
  return (
    <>
      {activeSourceId && (
        <Alert severity="info" icon={<WarningAmberIcon />} sx={{ mb: 2 }}>
          <strong>已選擇自訂資料來源</strong> — 正在查詢{' '}
          <em>{activeSourceName ?? activeSourceId}</em>。連線資訊已加密儲存並透過 HTTPS
          傳輸。注意：若此伺服器無法從公開網路連線，連接外部資料庫可能會失敗。
        </Alert>
      )}

      <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
        {activeSourceId ? (
          <>
            查詢中 <strong>{activeSourceName}</strong>
            {activeTableName && (
              <>
                {' → '}
                <Box component="code" sx={TABLE_SX}>
                  {activeTableName}
                </Box>
              </>
            )}
          </>
        ) : (
          <>
            查詢中{' '}
            <Box component="code" sx={TABLE_SX}>
              {activeTableName || DEFAULT_TABLE}
            </Box>
          </>
        )}
      </Typography>
    </>
  );
}

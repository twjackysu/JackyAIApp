import CheckIcon from '@mui/icons-material/Check';
import CloseIcon from '@mui/icons-material/Close';
import GppMaybeOutlinedIcon from '@mui/icons-material/GppMaybeOutlined';
import { Box, Button, ToggleButton, ToggleButtonGroup, Typography } from '@mui/material';
import { alpha, useTheme } from '@mui/material/styles';
import { useState } from 'react';

import {
  ApprovalCall,
  ApprovalDecision,
  ApprovalState,
  decisionsFor,
  formatArgument,
  PendingApproval,
  SITE_CONNECTOR,
} from '../utils/approval';

interface ApprovalCardProps {
  approval: PendingApproval;
  state: ApprovalState;
  /** What was decided, once sent (shown in place of the buttons). */
  decisions?: readonly ApprovalDecision[];
  onSubmit: (decisions: ApprovalDecision[]) => void;
}

const where = (call: ApprovalCall) =>
  call.connector === SITE_CONNECTOR
    ? null
    : `這個動作會在 ${call.connectorName ?? call.connector} 上執行`;

/**
 * The assistant asking before it changes something: each call it wants to make, with what it
 * would send, to allow or refuse one by one, then one 送出. A single call gets 允許 / 拒絕 straight
 * away. Once sent, the card keeps what was decided.
 */
export default function ApprovalCard({ approval, state, decisions, onSubmit }: ApprovalCardProps) {
  const theme = useTheme();
  const [chosen, setChosen] = useState<Record<string, boolean>>({});
  const single = approval.calls.length === 1;
  const open = state === 'pending' || state === 'failed';
  const decided = new Map(decisions?.map((d) => [d.toolUseId, d.approve]));
  const allChosen = approval.calls.every((c) => c.toolUseId in chosen);

  return (
    <Box
      data-testid="approval-card"
      role="group"
      aria-label="需要你的確認"
      sx={{
        my: 1,
        p: 1.5,
        borderRadius: 3,
        border: `1px solid ${alpha(theme.palette.warning.main, 0.5)}`,
        bgcolor: alpha(theme.palette.warning.main, theme.palette.mode === 'dark' ? 0.08 : 0.05),
      }}
    >
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.75, mb: 1 }}>
        <GppMaybeOutlinedIcon sx={{ fontSize: 18, color: 'warning.main' }} />
        <Typography variant="subtitle2" sx={{ fontWeight: 700 }}>
          需要你的確認
        </Typography>
      </Box>

      {approval.calls.map((call) => (
        <Box
          key={call.toolUseId}
          data-testid="approval-call"
          sx={{ py: 1, '& + &': { borderTop: `1px dashed ${alpha(theme.palette.divider, 0.6)}` } }}
        >
          <Typography variant="body2" sx={{ fontWeight: 600 }}>
            {call.connectorName && (
              <Box component="span" sx={{ color: 'text.secondary', fontWeight: 400, mr: 1 }}>
                {call.connectorName}
              </Box>
            )}
            {call.displayName}
            {call.effect === 'destructive' && (
              <Box component="span" sx={{ color: 'error.main', ml: 1, fontSize: '0.75rem' }}>
                無法復原
              </Box>
            )}
          </Typography>
          {where(call) && (
            <Typography variant="caption" color="text.secondary" sx={{ display: 'block' }}>
              {where(call)}
            </Typography>
          )}
          <Box component="dl" sx={{ m: 0, mt: 0.5, fontSize: '0.8rem' }}>
            {Object.entries(call.input).map(([key, value]) => (
              <Box key={key} sx={{ display: 'flex', gap: 1, minWidth: 0 }}>
                <Box component="dt" sx={{ color: 'text.secondary', flexShrink: 0 }}>
                  {key}:
                </Box>
                <Box component="dd" sx={{ m: 0, wordBreak: 'break-word' }}>
                  {formatArgument(value)}
                </Box>
              </Box>
            ))}
          </Box>

          {!open ? (
            <Typography
              variant="caption"
              sx={{
                display: 'block',
                mt: 0.5,
                color: decided.get(call.toolUseId) ? 'success.main' : 'text.secondary',
              }}
            >
              {decided.get(call.toolUseId) ? '已允許' : '已拒絕'}
            </Typography>
          ) : single ? (
            <Box sx={{ display: 'flex', gap: 1, mt: 1 }}>
              <Button
                size="small"
                variant="contained"
                startIcon={<CheckIcon />}
                onClick={() => onSubmit(decisionsFor(approval.calls, { [call.toolUseId]: true }))}
              >
                允許
              </Button>
              <Button
                size="small"
                variant="outlined"
                color="inherit"
                startIcon={<CloseIcon />}
                onClick={() => onSubmit(decisionsFor(approval.calls, {}))}
              >
                拒絕
              </Button>
            </Box>
          ) : (
            <ToggleButtonGroup
              size="small"
              exclusive
              value={call.toolUseId in chosen ? (chosen[call.toolUseId] ? 'yes' : 'no') : null}
              onChange={(_, v: 'yes' | 'no' | null) =>
                v && setChosen((prev) => ({ ...prev, [call.toolUseId]: v === 'yes' }))
              }
              aria-label={`${call.displayName}：允許或拒絕`}
              sx={{ mt: 1 }}
            >
              <ToggleButton value="yes">允許</ToggleButton>
              <ToggleButton value="no">拒絕</ToggleButton>
            </ToggleButtonGroup>
          )}
        </Box>
      ))}

      {open && !single && (
        <Button
          size="small"
          variant="contained"
          disabled={!allChosen}
          onClick={() => onSubmit(decisionsFor(approval.calls, chosen))}
          sx={{ mt: 1 }}
        >
          送出
        </Button>
      )}
      {open && !single && !allChosen && (
        <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 0.5 }}>
          每一項都選好允許或拒絕後才能送出
        </Typography>
      )}
      {state === 'sending' && (
        <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 0.5 }}>
          送出中…
        </Typography>
      )}
      {state === 'failed' && (
        <Typography variant="caption" color="error.main" sx={{ display: 'block', mt: 0.5 }}>
          沒有送達，請再試一次。
        </Typography>
      )}
      {approval.tainted && (
        <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 1 }}>
          這段對話讀過外部內容，寫入動作一律需要確認。
        </Typography>
      )}
    </Box>
  );
}

import CheckIcon from '@mui/icons-material/Check';
import CloseIcon from '@mui/icons-material/Close';
import GppMaybeOutlinedIcon from '@mui/icons-material/GppMaybeOutlined';
import {
  Box,
  Button,
  Checkbox,
  FormControlLabel,
  ToggleButton,
  ToggleButtonGroup,
  Typography,
} from '@mui/material';
import { alpha, useTheme } from '@mui/material/styles';
import { useState } from 'react';

import {
  ApprovalCall,
  ApprovalDecision,
  ApprovalState,
  canAlwaysAllow,
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
  /** The calls the reader allowed and asked to let run on their own from now on (`canAlwaysAllow`). */
  onAlwaysAllow?: (calls: ApprovalCall[]) => void;
}

/** One tool of one connector: what a permission is set for. */
const toolKey = (call: ApprovalCall) => `${call.connector}/${call.tool}`;

const where = (call: ApprovalCall) =>
  call.connector === SITE_CONNECTOR
    ? null
    : `這個動作會在 ${call.connectorName ?? call.connector} 上執行`;

/**
 * The assistant asking before it changes something: each call it wants to make, with what it
 * would send, to allow or refuse one by one, then one 送出. A single call gets 允許 / 拒絕 straight
 * away. Once sent, the card keeps what was decided. A call the site describes in words (`summary`, written by
 * the site's own code from the arguments) shows that, its raw arguments (ids such as a chartId) behind a
 * toggle; one without (an outside connector's) always shows its arguments.
 *
 * Several calls (a whole dashboard's charts saved at once) can also be decided together: 全部拒絕 always, and
 * 全部允許 when all are the site's own tools, none of them cannot be undone and the conversation has not read outside
 * content — an outside server's tool says its own effect, and after outside content an extra call
 * an injected text slipped in among the expected ones is exactly what one press would wave through, so each is decided
 * on its own. Letting a tool run on its own from now on is offered once per tool, under its first call.
 */
export default function ApprovalCard({
  approval,
  state,
  decisions,
  onSubmit,
  onAlwaysAllow,
}: ApprovalCardProps) {
  const theme = useTheme();
  const [chosen, setChosen] = useState<Record<string, boolean>>({});
  const [always, setAlways] = useState<Record<string, boolean>>({});
  const [shown, setShown] = useState<Record<string, boolean>>({});
  // The first call of each tool: where its 「以後直接執行」 box is, and what is handed on for it.
  const firstOfTool = new Map<string, ApprovalCall>();
  for (const c of approval.calls) if (!firstOfTool.has(toolKey(c))) firstOfTool.set(toolKey(c), c);
  const submit = (sent: ApprovalDecision[]) => {
    onSubmit(sent);
    const approved = new Set(sent.filter((d) => d.approve).map((d) => d.toolUseId));
    // A tool ticked to run on its own, for which at least one of its calls was allowed.
    const allowed = [...firstOfTool.entries()]
      .filter(
        ([key]) =>
          always[key] &&
          approval.calls.some((c) => toolKey(c) === key && approved.has(c.toolUseId)),
      )
      .map(([, c]) => c);
    if (allowed.length > 0) onAlwaysAllow?.(allowed);
  };
  const single = approval.calls.length === 1;
  // Only the site's own tools, whose effect is our own code's word (an outside server's is a hint it may leave out).
  const canAllowAll =
    !approval.tainted &&
    approval.calls.every((c) => c.connector === SITE_CONNECTOR && c.effect !== 'destructive');
  const allAs = (approve: boolean) =>
    submit(approval.calls.map((c) => ({ toolUseId: c.toolUseId, approve })));
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
          {call.summary && (
            <Typography variant="body2" sx={{ mt: 0.5 }} data-testid="approval-summary">
              {call.summary}
            </Typography>
          )}
          {call.summary && Object.keys(call.input).length > 0 && (
            <Button
              size="small"
              color="inherit"
              aria-expanded={shown[call.toolUseId] === true}
              onClick={() =>
                setShown((prev) => ({ ...prev, [call.toolUseId]: !prev[call.toolUseId] }))
              }
              sx={{ mt: 0.25, px: 0.5, minWidth: 0, fontSize: '0.75rem', color: 'text.secondary' }}
            >
              {shown[call.toolUseId] ? '隱藏送出的參數' : '顯示送出的參數'}
            </Button>
          )}
          {(!call.summary || shown[call.toolUseId]) && (
            <Box
              component="dl"
              data-testid="approval-arguments"
              sx={{ m: 0, mt: 0.5, fontSize: '0.8rem' }}
            >
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
          )}

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
            <Box sx={{ display: 'flex', gap: 1, mt: 1, '& .MuiButton-root': { minHeight: 44 } }}>
              <Button
                size="small"
                variant="contained"
                startIcon={<CheckIcon />}
                onClick={() => submit(decisionsFor(approval.calls, { [call.toolUseId]: true }))}
              >
                允許
              </Button>
              <Button
                size="small"
                variant="outlined"
                color="inherit"
                startIcon={<CloseIcon />}
                onClick={() => submit(decisionsFor(approval.calls, {}))}
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
              sx={{ mt: 1, '& .MuiToggleButton-root': { minHeight: 44, px: 2 } }}
            >
              <ToggleButton value="yes">允許</ToggleButton>
              <ToggleButton value="no">拒絕</ToggleButton>
            </ToggleButtonGroup>
          )}
          {open &&
            onAlwaysAllow &&
            firstOfTool.get(toolKey(call)) === call &&
            canAlwaysAllow(call, approval.tainted) && (
              <FormControlLabel
                sx={{
                  mt: 0.5,
                  ml: 0,
                  minHeight: 44,
                  '& .MuiFormControlLabel-label': { fontSize: '0.8rem' },
                }}
                control={
                  <Checkbox
                    size="small"
                    checked={always[toolKey(call)] === true}
                    onChange={(e) =>
                      setAlways((prev) => ({ ...prev, [toolKey(call)]: e.target.checked }))
                    }
                  />
                }
                label={`允許後，以後「${call.displayName}」直接執行（可到 Connectors 改回）`}
              />
            )}
        </Box>
      ))}

      {open && !single && (
        <Box
          sx={{
            display: 'flex',
            flexWrap: 'wrap',
            gap: 1,
            mt: 1,
            '& .MuiButton-root': { minHeight: 44 },
          }}
        >
          {canAllowAll && (
            <Button
              size="small"
              variant="contained"
              startIcon={<CheckIcon />}
              onClick={() => allAs(true)}
            >
              全部允許（{approval.calls.length}）
            </Button>
          )}
          <Button
            size="small"
            variant="outlined"
            color="inherit"
            startIcon={<CloseIcon />}
            onClick={() => allAs(false)}
          >
            全部拒絕
          </Button>
          <Button
            size="small"
            variant={canAllowAll ? 'outlined' : 'contained'}
            disabled={!allChosen}
            onClick={() => submit(decisionsFor(approval.calls, chosen))}
          >
            {canAllowAll ? '照上面的選擇送出' : '送出'}
          </Button>
        </Box>
      )}
      {open && !single && !allChosen && (
        <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 0.5 }}>
          {canAllowAll
            ? '也可以逐項選好允許或拒絕，再按「照上面的選擇送出」'
            : '每一項都選好允許或拒絕後才能送出'}
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

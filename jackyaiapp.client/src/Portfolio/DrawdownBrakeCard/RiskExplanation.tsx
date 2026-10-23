import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import Accordion from '@mui/material/Accordion';
import AccordionDetails from '@mui/material/AccordionDetails';
import AccordionSummary from '@mui/material/AccordionSummary';
import Box from '@mui/material/Box';
import Typography from '@mui/material/Typography';

import { PROFILE_KEYS, PROFILES } from '../drawdownBrake';
import {
  BACKTEST_NEXT_DAY,
  BACKTEST_PERIOD,
  BACKTEST_SCOPE_NOTE,
  RISK_POINTS,
  SIMULATED_NOTE,
} from '../drawdownBrakeCopy';

/** 風險等級說明（#431 §10 三點）與回測績效。績效一律同時顯示期間與最大回撤，且不單獨突顯最高
 * 報酬（PM 的 R3）；用隔日成交版本，與第一階段「收盤後才提醒」的時點一致（R4）。 */
export function RiskExplanation() {
  return (
    <Accordion
      disableGutters
      elevation={0}
      sx={{
        border: '1px solid',
        borderColor: 'divider',
        borderRadius: 2,
        '&:before': { display: 'none' },
      }}
    >
      <AccordionSummary expandIcon={<ExpandMoreIcon />}>
        <Typography variant="body2" sx={{ fontWeight: 600 }}>
          風險等級說明
        </Typography>
      </AccordionSummary>
      <AccordionDetails>
        <Box component="ul" sx={{ m: 0, pl: 2.5 }}>
          {RISK_POINTS.map((point) => (
            <Typography
              component="li"
              variant="body2"
              key={point}
              sx={{ mb: 0.5, overflowWrap: 'anywhere' }}
            >
              {point}
            </Typography>
          ))}
        </Box>

        <Typography variant="body2" sx={{ fontWeight: 600, mt: 2, mb: 1 }}>
          歷史回測（{BACKTEST_PERIOD}，隔日成交版本）
        </Typography>
        <Typography variant="body2" sx={{ mb: 1, fontWeight: 600, overflowWrap: 'anywhere' }}>
          {BACKTEST_SCOPE_NOTE}
        </Typography>
        <Box role="table" aria-label="歷史回測績效" sx={{ display: 'grid', gap: 0.5 }}>
          <Box
            role="row"
            sx={{
              display: 'grid',
              gridTemplateColumns: '1fr 1fr 1fr',
              gap: 1,
              color: 'text.secondary',
            }}
          >
            <Typography role="columnheader" variant="caption">
              等級
            </Typography>
            <Typography role="columnheader" variant="caption">
              年化報酬
            </Typography>
            <Typography role="columnheader" variant="caption">
              最大回撤
            </Typography>
          </Box>
          {PROFILE_KEYS.map((key) => (
            <Box
              role="row"
              key={key}
              sx={{ display: 'grid', gridTemplateColumns: '1fr 1fr 1fr', gap: 1 }}
            >
              <Typography role="cell" variant="body2">
                {PROFILES[key].label}
              </Typography>
              <Typography role="cell" variant="body2">
                {BACKTEST_NEXT_DAY[key].annualPct.toFixed(1)}%
              </Typography>
              <Typography role="cell" variant="body2">
                {BACKTEST_NEXT_DAY[key].maxDrawdownPct}%
              </Typography>
            </Box>
          ))}
        </Box>
        <Typography
          variant="caption"
          color="text.secondary"
          sx={{ display: 'block', mt: 1, overflowWrap: 'anywhere' }}
        >
          {`回測期間 ${BACKTEST_PERIOD}。本站只在收盤後提醒，等同隔日成交，所以採用隔日成交版本的數字。${SIMULATED_NOTE}`}
        </Typography>
      </AccordionDetails>
    </Accordion>
  );
}

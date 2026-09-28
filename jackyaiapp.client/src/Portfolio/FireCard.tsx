import Box from '@mui/material/Box';
import Grid from '@mui/material/Grid';
import InputAdornment from '@mui/material/InputAdornment';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { useState } from 'react';

import {
  computeYearsToGoal,
  defaultAnnualGrowthRatePct,
  FireTarget,
  loadFireTarget,
  saveFireTarget,
} from './fireTarget';
import { usePrivacy } from './privacy';

import { PortfolioSummaryDto } from '@/apis/portfolioApis/types';
import { fmtNum } from '@/utils/format';

function defaultTarget(summary: PortfolioSummaryDto): FireTarget {
  return {
    targetNetWorthTWD: 0,
    annualGrowthRatePct: defaultAnnualGrowthRatePct(summary.annualizedReturnPct),
    monthlyContributionTWD: 0,
  };
}

interface FireCardProps {
  summary: PortfolioSummaryDto;
}

/** Target net worth / growth-rate override / monthly contribution are a per-browser localStorage
 * preference (see `fireTarget.ts`), not account data — same pattern as `RebalancingCard`/
 * `rebalancingTarget.ts`. Reuses the already-fetched `PortfolioSummaryDto`; no second request, no
 * AI call. */
export function FireCard({ summary }: FireCardProps) {
  const { value } = usePrivacy();

  // First-run (nothing saved yet): seed the growth rate from the user's own historical XIRR when
  // available, or the documented placeholder otherwise — never silently treat a null historical
  // rate as 0%.
  const [target, setTarget] = useState<FireTarget>(
    () => loadFireTarget() ?? defaultTarget(summary),
  );

  // Inputs are edited as raw strings, separate from the committed numeric `target`. A `type="number"`
  // field sanitizes an in-progress value like "-" to "" before it becomes a full number, so coercing
  // straight to `target` on every keystroke (the previous behavior) reset it to 0 mid-edit and silently
  // dropped the sign — e.g. typing "-5" one character at a time landed on "5". Keeping the raw string
  // lets the field show exactly what was typed and only commits to `target`/localStorage once it
  // parses as a finite number.
  const [rawInputs, setRawInputs] = useState<Record<keyof FireTarget, string>>({
    targetNetWorthTWD: String(target.targetNetWorthTWD),
    annualGrowthRatePct: String(target.annualGrowthRatePct),
    monthlyContributionTWD: String(target.monthlyContributionTWD),
  });

  const updateField = (field: keyof FireTarget, raw: string) => {
    setRawInputs((prev) => ({ ...prev, [field]: raw }));
    const parsed = parseFloat(raw);
    if (!Number.isFinite(parsed)) return;
    const next = { ...target, [field]: parsed };
    setTarget(next);
    saveFireTarget(next);
  };

  // On blur, resync the raw display to the last committed value — clears a leftover
  // incomplete/invalid string (e.g. "-" or "") instead of leaving the field showing something that
  // was never actually saved.
  const commitField = (field: keyof FireTarget) => {
    setRawInputs((prev) => ({ ...prev, [field]: String(target[field]) }));
  };

  const placeholderRatePct = defaultAnnualGrowthRatePct(summary.annualizedReturnPct);
  const showPlaceholderRateCaption =
    summary.annualizedReturnPct == null && target.annualGrowthRatePct === placeholderRatePct;

  const hasTarget = target.targetNetWorthTWD > 0;
  const yearsToGoal = hasTarget ? computeYearsToGoal(summary.totalNetWorthTWD, target) : null;

  let resultText: string;
  if (!hasTarget) {
    resultText = '請先輸入目標淨值';
  } else if (yearsToGoal === 0) {
    resultText = '已達成目標';
  } else if (yearsToGoal === null) {
    resultText = '此假設下無法在 100 年內達成';
  } else {
    resultText = `預估 ${yearsToGoal} 年後達成目標`;
  }

  return (
    <Box>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
        設定目標淨值與預期成長假設，估算還要幾年能達成
      </Typography>

      <Grid container spacing={1.5} sx={{ mb: 1 }}>
        <Grid size={{ xs: 12, sm: 4 }}>
          <TextField
            size="small"
            type="number"
            label="目標淨值"
            value={rawInputs.targetNetWorthTWD}
            onChange={(e) => updateField('targetNetWorthTWD', e.target.value)}
            onBlur={() => commitField('targetNetWorthTWD')}
            fullWidth
            slotProps={{
              input: { startAdornment: <InputAdornment position="start">NT$</InputAdornment> },
              htmlInput: { min: 0, step: 10000 },
            }}
          />
        </Grid>
        <Grid size={{ xs: 12, sm: 4 }}>
          <TextField
            size="small"
            // `type="number"` sanitizes an in-progress value like "-" to "" before React ever
            // sees it (the HTML value-sanitization algorithm for number inputs), which silently
            // drops the sign when typing a negative rate character-by-character — this is the
            // only one of the three fields where a negative value is legitimate, so it's `text`
            // with a numeric keyboard hint instead.
            type="text"
            inputMode="decimal"
            label="預估年化成長率"
            value={rawInputs.annualGrowthRatePct}
            onChange={(e) => updateField('annualGrowthRatePct', e.target.value)}
            onBlur={() => commitField('annualGrowthRatePct')}
            fullWidth
            slotProps={{
              input: { endAdornment: <InputAdornment position="end">%</InputAdornment> },
            }}
          />
        </Grid>
        <Grid size={{ xs: 12, sm: 4 }}>
          <TextField
            size="small"
            type="number"
            label="每月投入"
            value={rawInputs.monthlyContributionTWD}
            onChange={(e) => updateField('monthlyContributionTWD', e.target.value)}
            onBlur={() => commitField('monthlyContributionTWD')}
            fullWidth
            slotProps={{
              input: { startAdornment: <InputAdornment position="start">NT$</InputAdornment> },
              htmlInput: { min: 0, step: 1000 },
            }}
          />
        </Grid>
      </Grid>

      {showPlaceholderRateCaption && (
        <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mb: 1 }}>
          尚無足夠歷史資料計算實際報酬率（需累積 30
          天以上淨值記錄），目前為預估年化成長率，可自行調整。
        </Typography>
      )}

      <Typography variant="body1" sx={{ fontWeight: 600, mt: 1 }}>
        {resultText}
      </Typography>

      <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 2 }}>
        目前淨值 NT${value(fmtNum(summary.totalNetWorthTWD))}
        。目標僅儲存在這個瀏覽器，不會同步到其他裝置。
      </Typography>
    </Box>
  );
}

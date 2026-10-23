import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Collapse from '@mui/material/Collapse';
import TextField from '@mui/material/TextField';
import ToggleButton from '@mui/material/ToggleButton';
import ToggleButtonGroup from '@mui/material/ToggleButtonGroup';
import Typography from '@mui/material/Typography';
import { useState } from 'react';

import {
  BrakeSettings,
  DEFENSE_E_OPTIONS,
  MIN_CASH_RATIO_MAX,
  MIN_CASH_RATIO_MIN,
  PROFILE_KEYS,
  PROFILES,
  ProfileKey,
} from '../drawdownBrake';
import { fmtMultiple } from '../drawdownBrakeCopy';

interface SettingsPanelProps {
  settings: BrakeSettings;
  onChange: (next: BrakeSettings) => void;
}

/** 風險等級，與收在「進階設定」裡的防守 E、最低現金比例 k。 */
export function SettingsPanel({ settings, onChange }: SettingsPanelProps) {
  const [advancedOpen, setAdvancedOpen] = useState(false);
  const [kText, setKText] = useState(
    settings.minCashRatio != null ? String(settings.minCashRatio) : '',
  );

  const profile = PROFILES[settings.profile];
  const effectiveK = settings.minCashRatio ?? profile.k;

  const parsedK = kText.trim() === '' ? undefined : Number(kText);
  const kInvalid =
    parsedK !== undefined &&
    (!Number.isFinite(parsedK) || parsedK < MIN_CASH_RATIO_MIN || parsedK > MIN_CASH_RATIO_MAX);

  const handleK = (raw: string) => {
    setKText(raw);
    if (raw.trim() === '') {
      onChange({ ...settings, minCashRatio: undefined });
      return;
    }
    const n = Number(raw);
    if (Number.isFinite(n) && n >= MIN_CASH_RATIO_MIN && n <= MIN_CASH_RATIO_MAX) {
      onChange({ ...settings, minCashRatio: n });
    }
  };

  return (
    <Box sx={{ mb: 2 }}>
      <Typography variant="body2" sx={{ fontWeight: 600, mb: 1 }}>
        風險等級
      </Typography>
      <ToggleButtonGroup
        exclusive
        fullWidth
        size="small"
        color="primary"
        aria-label="風險等級"
        value={settings.profile}
        onChange={(_, next: ProfileKey | null) => {
          if (next) onChange({ ...settings, profile: next });
        }}
      >
        {PROFILE_KEYS.map((key) => (
          <ToggleButton key={key} value={key}>
            {PROFILES[key].label}
          </ToggleButton>
        ))}
      </ToggleButtonGroup>
      <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 0.5 }}>
        進攻有效曝險 {fmtMultiple(profile.eOn)} 倍、防守 {fmtMultiple(settings.defenseE)}{' '}
        倍、最低現金 {Math.round(effectiveK * 100)}%
      </Typography>

      <Button
        size="small"
        onClick={() => setAdvancedOpen((open) => !open)}
        sx={{ mt: 1 }}
        aria-expanded={advancedOpen}
      >
        {advancedOpen ? '收合進階設定' : '進階設定'}
      </Button>
      <Collapse in={advancedOpen} unmountOnExit>
        <Box sx={{ pt: 1 }}>
          <Typography variant="body2" sx={{ fontWeight: 600, mb: 1 }}>
            防守時的有效曝險 E
          </Typography>
          <ToggleButtonGroup
            exclusive
            fullWidth
            size="small"
            color="primary"
            aria-label="防守時的有效曝險 E"
            value={settings.defenseE}
            onChange={(_, next: number | null) => {
              if (next != null) onChange({ ...settings, defenseE: next === 0.3 ? 0.3 : 0 });
            }}
          >
            {DEFENSE_E_OPTIONS.map((e) => (
              <ToggleButton key={e} value={e}>
                {e === 0 ? '0（全現金）' : '0.3（0050 30% / 現金 70%）'}
              </ToggleButton>
            ))}
          </ToggleButtonGroup>
          <TextField
            size="small"
            type="number"
            label="最低現金比例 k"
            value={kText}
            onChange={(e) => handleK(e.target.value)}
            error={kInvalid}
            fullWidth
            sx={{ mt: 2 }}
            placeholder={String(profile.k)}
            helperText={
              kInvalid
                ? `請輸入 ${MIN_CASH_RATIO_MIN} 到 ${MIN_CASH_RATIO_MAX} 之間的數值`
                : `${MIN_CASH_RATIO_MIN} ~ ${MIN_CASH_RATIO_MAX}，留空則依風險等級（目前 ${profile.k}）`
            }
            slotProps={{
              htmlInput: { min: MIN_CASH_RATIO_MIN, max: MIN_CASH_RATIO_MAX, step: 0.05 },
            }}
          />
        </Box>
      </Collapse>
    </Box>
  );
}

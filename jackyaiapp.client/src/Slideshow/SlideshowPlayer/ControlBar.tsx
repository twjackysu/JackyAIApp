import NavigateBeforeIcon from '@mui/icons-material/NavigateBefore';
import NavigateNextIcon from '@mui/icons-material/NavigateNext';
import PauseIcon from '@mui/icons-material/Pause';
import PlayArrowIcon from '@mui/icons-material/PlayArrow';
import { Box, Checkbox, FormControlLabel, IconButton, Slider, Typography } from '@mui/material';

import { chromeSx } from './chrome';

import type { SlideshowSettings } from '../settings';

import { ActionTooltip } from '@/components/hints';

interface ControlBarProps {
  idle: boolean;
  playing: boolean;
  settings: SlideshowSettings;
  onPrevious: () => void;
  onTogglePlay: () => void;
  onNext: () => void;
  onSettingsChange: (patch: Partial<SlideshowSettings>) => void;
  onToggleShuffle: (on: boolean) => void;
}

/** 下列：播放控制 */
export function ControlBar({
  idle,
  playing,
  settings,
  onPrevious,
  onTogglePlay,
  onNext,
  onSettingsChange,
  onToggleShuffle,
}: ControlBarProps) {
  return (
    <Box
      sx={{
        ...chromeSx(idle),
        bottom: 0,
        background: 'linear-gradient(to top, rgba(0,0,0,.75), rgba(0,0,0,0))',
      }}
    >
      <ActionTooltip title="上一張 (←)">
        <IconButton size="small" onClick={onPrevious} sx={{ color: 'common.white' }}>
          <NavigateBeforeIcon />
        </IconButton>
      </ActionTooltip>
      <ActionTooltip title={playing ? '暫停 (Space)' : '播放 (Space)'}>
        <IconButton size="small" onClick={onTogglePlay} sx={{ color: 'common.white' }}>
          {playing ? <PauseIcon /> : <PlayArrowIcon />}
        </IconButton>
      </ActionTooltip>
      <ActionTooltip title="下一張 (→)">
        <IconButton size="small" onClick={onNext} sx={{ color: 'common.white' }}>
          <NavigateNextIcon />
        </IconButton>
      </ActionTooltip>

      <Typography variant="caption" sx={{ color: 'grey.400', ml: 1, minWidth: 28 }}>
        {settings.interval}s
      </Typography>
      <Slider
        size="small"
        min={1}
        max={30}
        value={settings.interval}
        onChange={(_, v) => onSettingsChange({ interval: v as number })}
        aria-label="每張秒數"
        sx={{ width: 120, color: 'primary.light' }}
      />

      <FormControlLabel
        control={
          <Checkbox
            size="small"
            checked={settings.loop}
            onChange={(e) => onSettingsChange({ loop: e.target.checked })}
            sx={{ color: 'grey.500' }}
          />
        }
        label={<Typography variant="caption">循環</Typography>}
        sx={{ color: 'grey.400', mr: 0 }}
      />
      <FormControlLabel
        control={
          <Checkbox
            size="small"
            checked={settings.shuffle}
            onChange={(e) => onToggleShuffle(e.target.checked)}
            sx={{ color: 'grey.500' }}
          />
        }
        label={<Typography variant="caption">隨機播放</Typography>}
        sx={{ color: 'grey.400', mr: 0 }}
      />
      <FormControlLabel
        control={
          <Checkbox
            size="small"
            checked={settings.fit === 'cover'}
            onChange={(e) => onSettingsChange({ fit: e.target.checked ? 'cover' : 'contain' })}
            sx={{ color: 'grey.500' }}
          />
        }
        label={<Typography variant="caption">填滿裁切</Typography>}
        sx={{ color: 'grey.400', mr: 0 }}
      />
    </Box>
  );
}

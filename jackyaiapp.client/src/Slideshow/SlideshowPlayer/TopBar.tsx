import CloseIcon from '@mui/icons-material/Close';
import FullscreenIcon from '@mui/icons-material/Fullscreen';
import FullscreenExitIcon from '@mui/icons-material/FullscreenExit';
import { Box, IconButton, Typography } from '@mui/material';

import { chromeSx } from './chrome';

import { ActionTooltip } from '@/components/hints';

interface TopBarProps {
  idle: boolean;
  path: string | undefined;
  /** 1-based. */
  position: number;
  total: number;
  isFullscreen: boolean;
  onExit: () => void;
  onToggleFullscreen: () => void;
}

/** 上列：離開 + 檔名 + 張數 + 全螢幕 */
export function TopBar({
  idle,
  path,
  position,
  total,
  isFullscreen,
  onExit,
  onToggleFullscreen,
}: TopBarProps) {
  return (
    <Box
      sx={{
        ...chromeSx(idle),
        top: 0,
        background: 'linear-gradient(to bottom, rgba(0,0,0,.75), rgba(0,0,0,0))',
      }}
    >
      <ActionTooltip title="回到選擇資料夾 (Esc)">
        <IconButton size="small" onClick={onExit} sx={{ color: 'common.white' }}>
          <CloseIcon fontSize="small" />
        </IconButton>
      </ActionTooltip>
      <Typography
        variant="caption"
        sx={{
          color: 'grey.400',
          overflow: 'hidden',
          textOverflow: 'ellipsis',
          whiteSpace: 'nowrap',
          maxWidth: '55vw',
        }}
      >
        {path}
      </Typography>
      <Box sx={{ flex: 1 }} />
      <Typography variant="caption" sx={{ color: 'grey.400' }}>
        {position} / {total}
      </Typography>
      <ActionTooltip title={isFullscreen ? '離開全螢幕 (F)' : '全螢幕 (F)'}>
        <IconButton size="small" onClick={onToggleFullscreen} sx={{ color: 'common.white' }}>
          {isFullscreen ? (
            <FullscreenExitIcon fontSize="small" />
          ) : (
            <FullscreenIcon fontSize="small" />
          )}
        </IconButton>
      </ActionTooltip>
    </Box>
  );
}

import ArrowDropDownIcon from '@mui/icons-material/ArrowDropDown';
import ClearAllIcon from '@mui/icons-material/ClearAll';
import DeleteOutlinedIcon from '@mui/icons-material/DeleteOutlined';
import RestartAltIcon from '@mui/icons-material/RestartAlt';
import SwapVertIcon from '@mui/icons-material/SwapVert';
import TimelineIcon from '@mui/icons-material/Timeline';
import UndoIcon from '@mui/icons-material/Undo';
import Button from '@mui/material/Button';
import ListItemIcon from '@mui/material/ListItemIcon';
import Menu from '@mui/material/Menu';
import MenuItem from '@mui/material/MenuItem';
import Stack from '@mui/material/Stack';
import { useState } from 'react';

import { trendlineColor, trendlineName } from '../../utils/trendlineChartOption';

import type { TrendlineSide } from '../../utils/trendlines';

const otherSide = (side: TrendlineSide): TrendlineSide =>
  side === 'support' ? 'resistance' : 'support';

interface LineToolbarProps {
  /** The kind of line being drawn, if any. */
  drawing: TrendlineSide | null;
  onDraw: (side: TrendlineSide) => void;
  onCancelDrawing: () => void;
  /** The side of the selected line, if one is selected. */
  selectedSide: TrendlineSide | null;
  onSwapSide: (side: TrendlineSide) => void;
  onDeleteSelected: () => void;
  canUndo: boolean;
  onUndo: () => void;
  hasLines: boolean;
  onClear: () => void;
  customized: boolean;
  onRestoreAutomatic: () => void;
}

/** Drawing a line, what can be done to the selected one, and undo / clear / restore. */
export function LineToolbar({
  drawing,
  onDraw,
  onCancelDrawing,
  selectedSide,
  onSwapSide,
  onDeleteSelected,
  canUndo,
  onUndo,
  hasLines,
  onClear,
  customized,
  onRestoreAutomatic,
}: LineToolbarProps) {
  const [drawMenu, setDrawMenu] = useState<HTMLElement | null>(null);
  return (
    <Stack
      direction="row"
      spacing={0.5}
      sx={{ flexWrap: 'wrap', rowGap: 0.5, alignItems: 'center' }}
    >
      {drawing ? (
        <Button
          size="small"
          variant="contained"
          startIcon={<TimelineIcon />}
          sx={{ bgcolor: trendlineColor(drawing) }}
          onClick={onCancelDrawing}
        >
          取消畫{trendlineName(drawing)}
        </Button>
      ) : (
        <Button
          size="small"
          variant="outlined"
          startIcon={<TimelineIcon />}
          endIcon={<ArrowDropDownIcon />}
          aria-haspopup="menu"
          aria-expanded={drawMenu !== null}
          onClick={(e) => setDrawMenu(e.currentTarget)}
        >
          畫線
        </Button>
      )}
      <Menu anchorEl={drawMenu} open={drawMenu !== null} onClose={() => setDrawMenu(null)}>
        {(['support', 'resistance'] as const).map((side) => (
          <MenuItem
            key={side}
            onClick={() => {
              setDrawMenu(null);
              onDraw(side);
            }}
          >
            <ListItemIcon>
              <TimelineIcon sx={{ color: trendlineColor(side) }} />
            </ListItemIcon>
            {trendlineName(side)}
          </MenuItem>
        ))}
      </Menu>
      {selectedSide && !drawing && (
        <>
          <Button
            size="small"
            startIcon={<SwapVertIcon />}
            onClick={() => onSwapSide(otherSide(selectedSide))}
          >
            改為{trendlineName(otherSide(selectedSide))}
          </Button>
          <Button
            size="small"
            color="error"
            startIcon={<DeleteOutlinedIcon />}
            onClick={onDeleteSelected}
          >
            刪除這條線
          </Button>
        </>
      )}
      {canUndo && (
        <Button size="small" startIcon={<UndoIcon />} onClick={onUndo}>
          復原
        </Button>
      )}
      {hasLines && (
        <Button size="small" startIcon={<ClearAllIcon />} onClick={onClear}>
          清除全部
        </Button>
      )}
      {customized && (
        <Button size="small" startIcon={<RestartAltIcon />} onClick={onRestoreAutomatic}>
          還原自動線
        </Button>
      )}
    </Stack>
  );
}

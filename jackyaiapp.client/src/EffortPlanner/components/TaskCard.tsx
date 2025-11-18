import CloseIcon from '@mui/icons-material/Close';
import ExitToAppIcon from '@mui/icons-material/ExitToApp';
import InboxIcon from '@mui/icons-material/Inbox';
import ListAltIcon from '@mui/icons-material/ListAlt';
import {
  IconButton,
  ListItemIcon,
  ListItemText,
  Menu,
  MenuItem,
  Paper,
  Tooltip,
  Typography,
} from '@mui/material';
import React, { useContext, useState } from 'react';

import { useGetJiraConfigQuery } from '@/apis/jiraApis';

import { STASHED, TASK, WORK_BAR } from '../constants';
import EffortPlannerContext from '../context/EffortPlannerContext';
import { Task } from '../types';

interface TaskCardProps {
  task: Task;
  onDelete: () => void;
  onDragStart: (e: React.DragEvent<HTMLDivElement>) => void;

  location: typeof WORK_BAR | typeof STASHED;
  moveToStashed?: () => void;
  moveToWorkBar?: () => void;
}

const TaskCard: React.FC<TaskCardProps> = ({
  task,
  onDelete,
  onDragStart,
  location,
  moveToStashed,
  moveToWorkBar,
}) => {
  const [menuAnchorEl, setMenuAnchorEl] = useState<null | HTMLElement>(null);
  const isMenuOpen = Boolean(menuAnchorEl);
  const { selectedJiraConfigId } = useContext(EffortPlannerContext);

  const { data } = useGetJiraConfigQuery();
  const jiraDomain = data?.data.find((config) => config.id === selectedJiraConfigId)?.domain ?? '';

  const handleCardClick = (event: React.MouseEvent<HTMLDivElement>) => {
    event.stopPropagation();
    setMenuAnchorEl(event.currentTarget);
  };

  const handleMenuClose = () => {
    setMenuAnchorEl(null);
  };

  const handleOpenJira = () => {
    if (task.name && jiraDomain) {
      const url = `https://${jiraDomain}.atlassian.net/browse/${task.name}`;
      window.open(url, '_blank');
    }
    handleMenuClose();
  };

  const handleMoveToStashed = () => {
    if (moveToStashed) {
      moveToStashed();
    }
    handleMenuClose();
  };

  const handleMoveToWorkBar = () => {
    if (moveToWorkBar) {
      moveToWorkBar();
    }
    handleMenuClose();
  };
  return (
    <>
      <Paper
        draggable
        onDragStart={onDragStart}
        onClick={handleCardClick}
        sx={{
          p: 1,
          color: 'black',
          backgroundColor: TASK[task.key].color,
          borderRadius: 1,
          position: 'relative',
          width: 140,
        }}
      >
        {task.name && <Typography fontWeight="bold">{task.name}</Typography>}
        <Tooltip title={task.label}>
          <Typography
            sx={{
              overflow: 'hidden',
              whiteSpace: 'nowrap',
              textOverflow: 'ellipsis',
            }}
            variant="body1"
          >
            {task.label}
          </Typography>
        </Tooltip>
        <div>
          <Tooltip title={task.description}>
            <Typography
              sx={{
                overflow: 'hidden',
                whiteSpace: 'nowrap',
                textOverflow: 'ellipsis',
                display: 'block',
              }}
              variant="caption"
            >
              {task.description}
            </Typography>
          </Tooltip>
        </div>
        <IconButton
          onClick={onDelete}
          sx={{
            position: 'absolute',
            top: 0,
            right: 0,
            color: 'gray',
          }}
          size="small"
        >
          <CloseIcon fontSize="small" />
        </IconButton>
      </Paper>
      <Menu
        anchorEl={menuAnchorEl}
        open={isMenuOpen}
        onClose={handleMenuClose}
        anchorOrigin={{
          vertical: 'bottom',
          horizontal: 'left',
        }}
        transformOrigin={{
          vertical: 'top',
          horizontal: 'left',
        }}
      >
        {task.name && (
          <MenuItem onClick={handleOpenJira}>
            <ListItemIcon>
              <ExitToAppIcon fontSize="small" />
            </ListItemIcon>
            <ListItemText>Open Jira in a new tab</ListItemText>
          </MenuItem>
        )}

        {location === 'workBar' && (
          <MenuItem onClick={handleMoveToStashed}>
            <ListItemIcon>
              <InboxIcon fontSize="small" />
            </ListItemIcon>
            <ListItemText>Move to Stashed</ListItemText>
          </MenuItem>
        )}

        {location === 'stashed' && (
          <MenuItem onClick={handleMoveToWorkBar}>
            <ListItemIcon>
              <ListAltIcon fontSize="small" />
            </ListItemIcon>
            <ListItemText>Move to Work Bar</ListItemText>
          </MenuItem>
        )}
      </Menu>
    </>
  );
};

export default TaskCard;

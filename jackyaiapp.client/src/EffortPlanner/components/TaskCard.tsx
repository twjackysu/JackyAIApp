import CloseIcon from '@mui/icons-material/Close';
import { IconButton, Paper, Tooltip, Typography } from '@mui/material';
import React from 'react';
import { TASK } from '../constants';
import { Task } from '../types';

interface TaskCardProps {
  task: Task;
  onDelete: () => void;
  onDragStart: (e: React.DragEvent<HTMLDivElement>) => void;
}

const TaskCard: React.FC<TaskCardProps> = ({ task, onDelete, onDragStart }) => {
  return (
    <Paper
      draggable
      onDragStart={onDragStart}
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
      {/* {task.labels &&
        task.labels.map((l) => (
          <Chip key={l} label={l} color="primary" size="small" sx={{ mt: 0.5, mr: 0.5 }} />
        ))} */}
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
  );
};

export default TaskCard;

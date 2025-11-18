import { Box, Paper, Typography } from '@mui/material';
import React from 'react';

import { TASK } from '../constants';
import { Task } from '../types';

interface TaskPoolProps {
  tasks: Task[];
  onDragStart: (e: React.DragEvent<HTMLDivElement>, taskId: number) => void;
}

const TaskPool: React.FC<TaskPoolProps> = ({ tasks, onDragStart }) => {
  return (
    <Box>
      <Typography variant="h6">Task Pool</Typography>
      <Box sx={{ display: 'flex', gap: 2 }}>
        {tasks.map((task) => (
          <Paper
            key={task.id}
            draggable
            onDragStart={(e) => onDragStart(e, task.id)}
            sx={{
              p: 2,
              color: 'black',
              backgroundColor: TASK[task.key].color,
              borderRadius: 1,
              cursor: 'grab',
              textAlign: 'center',
            }}
          >
            {task.label}
          </Paper>
        ))}
      </Box>
    </Box>
  );
};

export default TaskPool;

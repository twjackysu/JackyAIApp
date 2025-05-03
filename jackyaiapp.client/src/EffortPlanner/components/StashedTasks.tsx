import { Box, Typography } from '@mui/material';
import React from 'react';
import { STASHED } from '../constants';
import TaskCard from './TaskCard';

interface StashedTasksProps {
  tasks: any[];
  onDrop: React.DragEventHandler<HTMLDivElement>;
  onDragStart: (e: React.DragEvent<HTMLDivElement>, taskId: number) => void;
  onDelete: (taskId: number) => void;

  moveTaskToWorkBar: (taskId: number) => void;
}

const StashedTasks: React.FC<StashedTasksProps> = ({
  tasks,
  onDrop,
  onDragStart,
  onDelete,
  moveTaskToWorkBar,
}) => {
  return (
    <Box>
      <Typography variant="h6">Stashed Tasks</Typography>
      <Box
        onDragOver={(e) => e.preventDefault()}
        onDrop={onDrop}
        sx={{
          minHeight: 100,
          p: 2,
          border: '2px dashed #999',
          borderRadius: 1,
        }}
      >
        <Box sx={{ display: 'flex', gap: 1, flexWrap: 'wrap' }}>
          {tasks.map((task) => (
            <TaskCard
              key={task.id}
              task={task}
              onDelete={() => onDelete(task.id)}
              onDragStart={(e) => onDragStart(e, task.id)}
              location={STASHED}
              moveToWorkBar={() => moveTaskToWorkBar(task.id)}
            />
          ))}
        </Box>
      </Box>
    </Box>
  );
};

export default StashedTasks;

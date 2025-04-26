import { Box, Button, Typography } from '@mui/material';
import { red } from '@mui/material/colors';
import React from 'react';
import TaskCard from './TaskCard';

interface WorkBarProps {
  tasks: any[];
  maxDays: number;
  totalDays: number;
  onDrop: React.DragEventHandler<HTMLDivElement>;
  onDragStart: (e: React.DragEvent<HTMLDivElement>, index: number) => void;
  onDelete: (index: number) => void;
  reset: () => void;
}

const WorkBar: React.FC<WorkBarProps> = ({
  tasks,
  maxDays,
  totalDays,
  onDrop,
  onDragStart,
  onDelete,
  reset,
}) => {
  const isOverCapacity = totalDays > maxDays;

  return (
    <Box>
      <Typography variant="h6">{maxDays}-Day Work Bar</Typography>
      <Box
        onDragOver={(e) => e.preventDefault()}
        onDrop={onDrop}
        sx={{
          minHeight: 100,
          p: 2,
          border: isOverCapacity ? `2px dashed ${red[500]}` : '2px dashed #999',
          borderRadius: 1,
          mb: 2,
          backgroundColor: isOverCapacity ? red[50] : 'transparent',
        }}
      >
        <Box sx={{ display: 'flex', gap: 1, flexWrap: 'wrap' }}>
          {tasks.map((task, index) => (
            <TaskCard
              key={index}
              task={task}
              onDelete={() => onDelete(index)}
              onDragStart={(e) => onDragStart(e, index)}
            />
          ))}
        </Box>
      </Box>
      <Typography color={isOverCapacity ? 'error' : 'inherit'}>
        Total Assigned Days: {totalDays} / {maxDays}
        {isOverCapacity && ' (Overloaded!)'}
      </Typography>
      <Button variant="outlined" onClick={reset} sx={{ mt: 1 }}>
        Reset
      </Button>
    </Box>
  );
};

export default WorkBar;

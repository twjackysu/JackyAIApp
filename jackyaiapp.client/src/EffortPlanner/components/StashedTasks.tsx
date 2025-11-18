import { Box, Typography } from '@mui/material';
import React, { useContext } from 'react';

import { STASHED } from '../constants';
import EffortPlannerContext from '../context/EffortPlannerContext';

import TaskCard from './TaskCard';

interface StashedTasksProps {
  onDragStart: (e: React.DragEvent<HTMLDivElement>, taskId: number) => void;
}

const StashedTasks: React.FC<StashedTasksProps> = ({ onDragStart }) => {
  const { assigned, setSavedTasks, setAssigned, savedTasks, maxDays, totalDays } =
    useContext(EffortPlannerContext);

  const handleStashDrop: React.DragEventHandler<HTMLDivElement> = (event) => {
    const index = parseInt(event.dataTransfer.getData('assigned-index'), 10);
    if (isNaN(index)) return;
    const task = assigned[index];
    if (!task) return;
    const newId = Date.now();
    setSavedTasks([...savedTasks, { ...task, id: newId }]);
    const updated = [...assigned];
    updated.splice(index, 1);
    setAssigned(updated);
  };

  const deleteSavedTask = (taskId: number) => {
    setSavedTasks(savedTasks.filter((t) => t.id !== taskId));
  };

  const moveTaskToWorkBar = (taskId: number) => {
    const task = savedTasks.find((t) => t.id === taskId);
    if (!task) return;

    const newTotal = totalDays + task.days;
    if (newTotal <= maxDays || (newTotal > maxDays && totalDays <= maxDays)) {
      setAssigned([...assigned, { ...task, name: task.name }]);
      setSavedTasks(savedTasks.filter((t) => t.id !== taskId));
    } else {
      alert('WARNING: Adding this task will cause the workload to exceed capacity!');
    }
  };
  return (
    <Box>
      <Typography variant="h6">Stashed Tasks</Typography>
      <Box
        onDragOver={(e) => e.preventDefault()}
        onDrop={handleStashDrop}
        sx={{
          minHeight: 100,
          p: 2,
          border: '2px dashed #999',
          borderRadius: 1,
        }}
      >
        <Box sx={{ display: 'flex', gap: 1, flexWrap: 'wrap' }}>
          {savedTasks.map((task) => (
            <TaskCard
              key={task.id}
              task={task}
              onDelete={() => deleteSavedTask(task.id)}
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
